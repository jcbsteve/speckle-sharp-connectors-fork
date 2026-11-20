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

Write-Output "Status: $status (exit code $exitCode)"
if ($null -ne $result) {
    foreach ($failure in $result.failures) {
        Write-Output "  $($failure.failureKind): $($failure.path) - $($failure.error)"
    }
}
Write-Output "Log: $logPath"
Write-Output "Result: $resultPath"

exit $exitCode
