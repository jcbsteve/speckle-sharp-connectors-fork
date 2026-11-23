# Shows the Plant 3D automated publish environment variables for this session, the user and the machine.
# Read-only: nothing is changed.

$names = @(
    'PLANT_PROJECT',
    'PLANT_DRAWING_MAPPINGS',
    'PLANT_PUBLISH_FORCE',
    'PLANT_PUBLISH_DRAWING_RETRIES',
    'PLANT_PUBLISH_RUN_ID',
    'PLANT_PUBLISH_DEADLINE_UTC',
    'PLANT_PUBLISH_SCRIPTS_DIR'
)

foreach ($name in $names) {
    Write-Output $name
    foreach ($scope in 'Process', 'User', 'Machine') {
        $value = [Environment]::GetEnvironmentVariable($name, $scope)
        Write-Output ("  {0,-8} {1}" -f $scope, ($value ?? '(not set)'))
    }
}

foreach ($name in 'PLANT_PROJECT', 'PLANT_DRAWING_MAPPINGS') {
    $path = [Environment]::GetEnvironmentVariable($name)
    if ($path -and -not (Test-Path $path)) { Write-Warning "$name path not found: $path" }
}

foreach ($name in 'PLANT_PUBLISH_RUN_ID', 'PLANT_PUBLISH_DEADLINE_UTC', 'PLANT_PUBLISH_SCRIPTS_DIR') {
    if ([Environment]::GetEnvironmentVariable($name, 'User') -or [Environment]::GetEnvironmentVariable($name, 'Machine')) {
        Write-Warning "$name is set persistently. It should only be set per run by pub.ps1."
    }
}
