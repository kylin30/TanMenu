namespace TanMenu.Core.Services;

/// <summary>Shows and removes the preview overlay. Implemented in the UI project so Core stays free of Avalonia.</summary>
public interface IIslandPreviewController : IDisposable
{
    void Show();
    void Hide();
}
