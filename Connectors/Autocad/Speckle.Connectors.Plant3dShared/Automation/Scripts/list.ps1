$Project = "C:\Users\SuttonS4\AppData\Local\Autodesk\AutoCAD Plant 3D\CollaborationCache\Plant3D_Speckle\Project.xml"
$Config  = "C:\ProgramData\Autodesk\Autodesk AutoCAD Plant 3D 2026\R25.1\enu\ReportCreator\ReportFiles\DrawingListExport.rcfx"
$exe = "C:\Program Files\Autodesk\AutoCAD 2026\PLNT3D\PnPReportCreator.exe"

$process = Start-Process -FilePath $exe -Wait -PassThru `
    -ArgumentList "/PROJECT `"$Project`" /CONFIG `"$Config`" /HIDDEN /QUIET"
    
if ($process.ExitCode -ne 0) {
    throw "PnPReportCreator failed with exit code $($process.ExitCode)"
}
