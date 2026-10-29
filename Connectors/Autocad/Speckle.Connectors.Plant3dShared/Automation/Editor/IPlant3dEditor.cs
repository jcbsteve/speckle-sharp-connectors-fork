using Autodesk.AutoCAD.EditorInput;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Provides access to the active document's editor for user messaging/prompts during Plant3D automation.
/// Ensures that messages are not written to a non-active editor and commands do not raise eNotApplicable errors.
/// </summary>
public interface IPlant3dEditor
{
  Editor Current { get; }

  string GetString(string prompt);

  string GetKeywords(string prompt, params string[] keywords);
}
