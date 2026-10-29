$env:PLANT_PROJECT = "C:\Users\SuttonS4\AppData\Local\Autodesk\AutoCAD Plant 3D\CollaborationCache\Plant3D_Speckle\Project.xml"
$env:PLANT_DRAWING_MAPPINGS = "C:\git\SpecklePoc\SpeckleModelSync\bin\Debug\net10.0\DrawingModelMappings.json"


& "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe" `
    /product PLNT3D `
    /b "C:\git\speckle-sharp-connectors-jacobs\Connectors\Autocad\Speckle.Connectors.Plant3dShared\Automation\Scripts\pub.scr"
