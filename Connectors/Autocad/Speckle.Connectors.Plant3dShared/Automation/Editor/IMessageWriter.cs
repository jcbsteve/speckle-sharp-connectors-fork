namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Ensures that messages are not written to a non-active editor and commands do not raise eNotApplicable errors.
/// </summary>
public interface IMessageWriter
{
  void WriteMessage(string message);
}
