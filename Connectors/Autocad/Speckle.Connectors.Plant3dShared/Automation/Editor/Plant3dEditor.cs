using Autodesk.AutoCAD.EditorInput;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Provides access to the active document's editor for user messaging/prompts during Plant3D automation.
/// Ensures that messages are not written to a non-active editor and commands do not raise eNotApplicable errors.
/// </summary>
public class Plant3dEditor : IPlant3dEditor, IMessageWriter
{
  public Editor Current => Application.DocumentManager.MdiActiveDocument.Editor;

  public void WriteMessage(string message) => Current.WriteMessage($"\n{message}\n");

  public string GetString(string prompt) => Current.GetString(prompt).StringResult;

  public string GetKeywords(string prompt, params string[] keywords) =>
    Current.GetKeywords(prompt, keywords).StringResult;
}
