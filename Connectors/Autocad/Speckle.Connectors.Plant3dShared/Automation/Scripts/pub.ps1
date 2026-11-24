# Publishes the mapped drawings of a Plant 3D project to Speckle, retrying the whole run after a crash, a hang or
# retryable drawing failures. The exit code is the overall result (see specs\publish-retry-design.md).
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
    [Nullable[int]]$DrawingRetries
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
# ===============================================================================

# Parameters win; otherwise use the settings above.
$acadExe = if ($AcadPath) { $AcadPath } else { $defaultAcadPath }
$plantProject = if ($Project) { $Project } else { $defaultProject }
$drawingMappings = if ($Mappings) { $Mappings } else { $defaultMappings }
$publishForce = if ($PSBoundParameters.ContainsKey('Force')) { $Force.IsPresent } else { $defaultForce }
$retryCount = if ($PSBoundParameters.ContainsKey('DrawingRetries')) { $DrawingRetries } else { $defaultDrawingRetries }

foreach ($path in $acadExe, $plantProject, $drawingMappings) {
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

$env:PLANT_PUBLISH_RUN_ID = $runId
# The command stops starting drawings 5 minutes before this deadline.
$env:PLANT_PUBLISH_DEADLINE_UTC = $deadline.ToString('o')
# pub.scr loads pub.lsp from this folder, as .scr files cannot resolve relative paths.
$env:PLANT_PUBLISH_SCRIPTS_DIR = $PSScriptRoot

$status = $null
$result = $null

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Output "Run $runId - attempt $attempt of $MaxAttempts"
    Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
    $result = $null

    $process = Start-Process -FilePath $acadExe -ArgumentList '/product', 'PLNT3D', '/b', "`"$PSScriptRoot\pub.scr`"" -PassThru
    $remainingMs = [Math]::Max(0, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (-not $process.WaitForExit($remainingMs)) {
        # Stop only the Plant 3D process this script started; other sessions may be open.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $status = 'Timeout'
        break
    }

    if (Test-Path -LiteralPath $resultPath) {
        try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json }
        catch { Write-Output "Unreadable result file: $($_.Exception.Message)" }
    }

    if ($null -eq $result) {
        $status = 'Crashed'
    }
    else {
        $status = $result.status
        if ($status -in 'Success', 'ConfigError') { break }

        $retryable = @($result.failures | Where-Object { $_.failureKind -in 'Transient', 'Cancelled' })
        if ($retryable.Count -eq 0) { break }
    }

    if ($attempt -eq $MaxAttempts) { break }

    $delay = $RetryDelaySeconds * [Math]::Pow(2, $attempt - 1)
    # The command starts no drawings in the last 5 minutes, so a retry needs more time than that.
    if ([DateTime]::UtcNow.AddSeconds($delay).AddMinutes(10) -ge $deadline) { break }
    Write-Output "Attempt $attempt ended with $status. Retrying in $delay seconds."
    Start-Sleep -Seconds $delay
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
