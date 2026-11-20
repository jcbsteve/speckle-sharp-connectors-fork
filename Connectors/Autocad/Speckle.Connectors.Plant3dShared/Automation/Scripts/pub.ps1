# Publishes the mapped drawings of a Plant 3D project to Speckle, retrying the whole run after a crash, a hang or
# retryable drawing failures. The exit code is the overall result (see specs\publish-retry-design.md).
param(
    [int]$TimeoutMinutes = 120,
    [int]$MaxAttempts = 3,
    [int]$RetryDelaySeconds = 60
)

$ErrorActionPreference = 'Stop'

$env:PLANT_PROJECT = "C:\Users\SuttonS4\AppData\Local\Autodesk\AutoCAD Plant 3D\CollaborationCache\Plant3D_Speckle\Project.xml"
$env:PLANT_DRAWING_MAPPINGS = "C:\git\SpecklePoc\SpeckleModelSync\bin\Debug\net10.0\DrawingModelMappings.json"
# Optional: publish all mapped drawings once per run, even when unchanged.
# $env:PLANT_PUBLISH_FORCE = "true"
# Optional: extra tries per drawing for transient failures (default 2).
# $env:PLANT_PUBLISH_DRAWING_RETRIES = "2"

$acadPath = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe"

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
    Remove-Item $resultPath -ErrorAction SilentlyContinue
    $result = $null

    $process = Start-Process -FilePath $acadPath -ArgumentList '/product', 'PLNT3D', '/b', "`"$PSScriptRoot\pub.scr`"" -PassThru
    $remainingMs = [Math]::Max(0, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (-not $process.WaitForExit($remainingMs)) {
        # Stop only the Plant 3D process this script started; other sessions may be open.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $status = 'Timeout'
        break
    }

    if (-not (Test-Path $resultPath)) {
        $status = 'Crashed'
    }
    else {
        $result = Get-Content $resultPath -Raw | ConvertFrom-Json
        $status = $result.status
        if ($status -in 'Success', 'ConfigError') { break }

        $retryable = @($result.failures | Where-Object { $_.failureKind -in 'Transient', 'Cancelled' })
        if ($retryable.Count -eq 0) { break }
    }

    if ($attempt -eq $MaxAttempts) { break }

    $delay = $RetryDelaySeconds * [Math]::Pow(2, $attempt - 1)
    if ([DateTime]::UtcNow.AddSeconds($delay) -ge $deadline) { break }
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
    foreach ($failure in $result.failures) {
        Write-Output "  $($failure.failureKind): $($failure.path) - $($failure.error)"
    }
}
Write-Output "Log: $logPath"
Write-Output "Result: $resultPath"

exit $exitCode
