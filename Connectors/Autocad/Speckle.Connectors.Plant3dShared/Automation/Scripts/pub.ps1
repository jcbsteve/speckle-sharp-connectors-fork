# Publishes the mapped drawings of a Plant 3D project to Speckle, retrying the whole run after a crash, a hang or
# retryable drawing failures. The exit code is the overall result (see specs\publish-retry-design.md).
# With a mapper configured, missing or empty mappings are created from a drawing inventory first, and drawings
# without a mapping are remapped and published in the same run (see specs\plant3d-mapping-options.md, option C).
param(
    [ValidateRange(1, 1440)]
    [int]$TimeoutMinutes = 120,
    [ValidateRange(1, 10)]
    [int]$MaxAttempts = 3,
    [ValidateRange(0, 3600)]
    [int]$RetryDelaySeconds = 60,

    # Optional overrides. When not passed, the USER SETTINGS below are used.
    [string]$AcadPath,
    [string]$Project,
    [string]$Mappings,
    [switch]$Force,
    [ValidateRange(0, 10)]
    [Nullable[int]]$DrawingRetries,
    [string]$MapperPath,
    [Nullable[bool]]$RemapOnDrift,
    [string]$MapperConfig,
    [string]$MapperDefaults
)

$ErrorActionPreference = 'Stop'

# ===================== USER SETTINGS - defaults, edit these =====================
# AutoCAD executable used to launch Plant 3D (match the installed version).
$defaultAcadPath = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe"
# Plant 3D project file to open.
$defaultProject = "C:\Users\SuttonS4\AppData\Local\Autodesk\AutoCAD Plant 3D\CollaborationCache\Plant3D_Speckle\Project.xml"
# Drawing-to-model mappings file.
$defaultMappings = "C:\git\SpecklePoc\SpeckleModelSync\bin\Debug\net10.0\DrawingModelMappings.json"
# $true publishes all mapped drawings once per run, even when unchanged.
$defaultForce = $false
# Extra tries per drawing for transient failures. $null = command default (2).
$defaultDrawingRetries = $null
# SpeckleModelSync.exe, which writes the mappings from a drawing inventory. $null = never remap.
$defaultMapperPath = "C:\git\SpecklePoc\SpeckleModelSync\bin\Debug\net10.0\SpeckleModelSync.exe"
# $true remaps and publishes again when the run finds drawings without a mapping.
$defaultRemapOnDrift = $true
# Publish set file the mapper reads the Speckle project from. Its plant.mappingsPath must match the mappings above.
$defaultMapperConfig = $null
# Optional machine defaults file for the publish set. $null = the mapper's PUBLISH_DEFAULTS_PATH setting.
$defaultMapperDefaults = $null
# ===============================================================================

# Parameters win; otherwise use the settings above.
$acadExe = if ($AcadPath) { $AcadPath } else { $defaultAcadPath }
$plantProject = if ($Project) { $Project } else { $defaultProject }
$drawingMappings = if ($Mappings) { $Mappings } else { $defaultMappings }
$publishForce = if ($PSBoundParameters.ContainsKey('Force')) { $Force.IsPresent } else { $defaultForce }
$retryCount = if ($PSBoundParameters.ContainsKey('DrawingRetries')) { $DrawingRetries } else { $defaultDrawingRetries }
$mapperExe = if ($MapperPath) { $MapperPath } else { $defaultMapperPath }
$remap = if ($null -ne $RemapOnDrift) { $RemapOnDrift } else { $defaultRemapOnDrift }
$mapperConfig = if ($MapperConfig) { $MapperConfig } else { $defaultMapperConfig }
$mapperDefaults = if ($MapperDefaults) { $MapperDefaults } else { $defaultMapperDefaults }

# The mappings file may be missing when the mapper can create it.
$requiredFiles = @($acadExe, $plantProject) + $(if ($mapperExe) { @($mapperExe, $mapperConfig) } else { $drawingMappings })
foreach ($path in $requiredFiles) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Write-Output "Status: ConfigError (exit code 4)"
        Write-Output "  File not found: '$path'"
        exit 4
    }
}

# Unset values remove the variable, so values saved in the user profile cannot leak into the run.
$env:PLANT_PROJECT = $plantProject
$env:PLANT_DRAWING_MAPPINGS = $drawingMappings
$env:PLANT_PUBLISH_FORCE = if ($publishForce) { 'true' } else { $null }
$env:PLANT_PUBLISH_DRAWING_RETRIES = if ($null -ne $retryCount) { "$retryCount" } else { $null }

$projectName = (Split-Path (Split-Path $env:PLANT_PROJECT -Parent) -Leaf) -replace '[^A-Za-z0-9_-]', '_'
$startUtc = [DateTime]::UtcNow
$runId = "$projectName-$($startUtc.ToString('yyyyMMddTHHmmssZ'))"
$deadline = $startUtc.AddMinutes($TimeoutMinutes)
$logsDir = Join-Path $env:LOCALAPPDATA 'Speckle\Logs'
$logPath = Join-Path $logsDir "plant3d-automation-$runId.log"
$resultPath = Join-Path $logsDir "plant3d-automation-$runId.result.json"
$inventoryPath = Join-Path $logsDir "plant3d-automation-$runId.drawings.json"

$env:PLANT_PUBLISH_RUN_ID = $runId
# The command stops starting drawings 5 minutes before this deadline.
$env:PLANT_PUBLISH_DEADLINE_UTC = $deadline.ToString('o')
# pub.scr loads pub.lsp from this folder, as .scr files cannot resolve relative paths.
$env:PLANT_PUBLISH_SCRIPTS_DIR = $PSScriptRoot

$status = $null
$result = $null

# Runs one Plant 3D batch command. Returns $false when the run deadline is reached.
function Invoke-Plant3d([string]$batchCommand) {
    $env:PLANT_BATCH_COMMAND = $batchCommand
    $process = Start-Process -FilePath $acadExe -ArgumentList '/product', 'PLNT3D', '/b', "`"$PSScriptRoot\pub.scr`"" -PassThru
    $remainingMs = [Math]::Max(0, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (-not $process.WaitForExit($remainingMs)) {
        # Stop only the Plant 3D process this script started; other sessions may be open.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        return $false
    }
    return $true
}

# Writes the mappings from this run's drawing inventory. Returns $true when the mappings file changed.
function Invoke-Mapper {
    if (-not (Test-Path -LiteralPath $inventoryPath)) {
        Write-Host "No drawing inventory at '$inventoryPath'. Mappings not updated."
        return $false
    }
    $before = if (Test-Path -LiteralPath $drawingMappings) { (Get-FileHash -LiteralPath $drawingMappings).Hash }
    Write-Host "Updating mappings from '$inventoryPath'."
    $mapperArgs = @('--config', $mapperConfig, '--inventory', $inventoryPath)
    if ($mapperDefaults) { $mapperArgs += @('--defaults', $mapperDefaults) }
    & $mapperExe @mapperArgs | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Mapper failed with exit code $LASTEXITCODE. Mappings not updated."
        return $false
    }
    $after = if (Test-Path -LiteralPath $drawingMappings) { (Get-FileHash -LiteralPath $drawingMappings).Hash }
    return $before -ne $after
}

function Test-MappingsEmpty {
    if (-not (Test-Path -LiteralPath $drawingMappings)) { return $true }
    try { return @(Get-Content -LiteralPath $drawingMappings -Raw | ConvertFrom-Json).Count -eq 0 }
    catch { return $false } # Invalid mappings are reported by the publish run.
}

# Publishes with retries and sets $status and $result.
function Invoke-Publish {
for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Output "Run $runId - attempt $attempt of $MaxAttempts"
    Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
    $script:result = $null

    if (-not (Invoke-Plant3d '-SPECKLEPUBLISH')) {
        $script:status = 'Timeout'
        break
    }

    if (Test-Path -LiteralPath $resultPath) {
        try { $script:result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json }
        catch { Write-Output "Unreadable result file: $($_.Exception.Message)" }
    }

    if ($null -eq $script:result) {
        $script:status = 'Crashed'
    }
    else {
        $script:status = $script:result.status
        if ($script:status -in 'Success', 'ConfigError') { break }

        $retryable = @($script:result.failures | Where-Object { $_.failureKind -in 'Transient', 'Cancelled' })
        if ($retryable.Count -eq 0) { break }
    }

    if ($attempt -eq $MaxAttempts) { break }

    $delay = $RetryDelaySeconds * [Math]::Pow(2, $attempt - 1)
    # The command starts no drawings in the last 5 minutes, so a retry needs more time than that.
    if ([DateTime]::UtcNow.AddSeconds($delay).AddMinutes(10) -ge $deadline) { break }
    Write-Output "Attempt $attempt ended with $($script:status). Retrying in $delay seconds."
    Start-Sleep -Seconds $delay
}
}

$canPublish = $true
if ($mapperExe -and (Test-MappingsEmpty)) {
    # First run: list the drawings without publishing, then create the mappings from that inventory.
    Write-Output "No mappings in '$drawingMappings'. Listing drawings to create them."
    if (-not (Invoke-Plant3d '-SPECKLELIST')) {
        $status = 'Timeout'
        $canPublish = $false
    }
    elseif (-not (Invoke-Mapper) -or (Test-MappingsEmpty)) {
        $status = 'ConfigError'
        $canPublish = $false
    }
}

if ($canPublish) {
    Invoke-Publish

    # The publish run writes the live inventory, so drawings it found without a mapping can be mapped and
    # published now. Already published drawings are skipped by the second run.
    $drift = $null -ne $result -and $result.counts.drawingsWithoutMapping -gt 0
    if ($remap -and $mapperExe -and $drift -and $status -ne 'Timeout') {
        Write-Output "$($result.counts.drawingsWithoutMapping) drawing(s) without a mapping. Remapping."
        if (Invoke-Mapper) { Invoke-Publish }
        else { Write-Output "Mappings unchanged. Not publishing again." }
    }
}

$hasTransient = $null -ne $result -and @($result.failures | Where-Object { $_.failureKind -eq 'Transient' }).Count -gt 0
$exitCode = switch ($true) {
    ($status -eq 'Timeout') { 124; break }
    ($status -eq 'Crashed') { 5; break }
    ($status -eq 'ConfigError') { 4; break }
    ($status -eq 'Cancelled') { 140; break }
    ($status -eq 'Success') { 0; break }
    $hasTransient { 3; break }
    ($status -eq 'Partial') { 1; break }
    default { 2 }
}

Write-Output "Status: $status (exit code $exitCode)"
if ($null -ne $result) {
    $c = $result.counts
    if ($c.mappingsWithoutDrawing -gt 0 -or $c.drawingsWithoutMapping -gt 0) {
        Write-Output "Warning: mappings out of sync - $($c.mappingsWithoutDrawing) mapping(s) without drawing, $($c.drawingsWithoutMapping) drawing(s) without mapping. See log."
    }
    foreach ($failure in $result.failures) {
        Write-Output "  $($failure.failureKind): $($failure.path) - $($failure.error)"
    }
}
Write-Output "Log: $logPath"
Write-Output "Result: $resultPath"

exit $exitCode
