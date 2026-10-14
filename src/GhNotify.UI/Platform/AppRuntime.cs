namespace GhNotify.UI;

/// <summary>Runs work on the UI thread. The app uses Avalonia's dispatcher; tests run inline.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Where the app keeps its files; resolved by the host (env overrides included).</summary>
public sealed record AppDirs(string DataDir, string Logs, string Worktrees, string Scratch);

/// <summary>What the host app gives the UI besides <see cref="PlatformServices"/>.</summary>
/// <param name="Dispatcher">The UI thread.</param>
/// <param name="Dirs">Data folders.</param>
/// <param name="Quit">Shuts the application down.</param>
/// <param name="CopyText">Puts text on the clipboard (the UI wires this to the top-level clipboard once a window exists).</param>
public sealed record AppRuntime(IUiDispatcher Dispatcher, AppDirs Dirs, Action Quit, Action<string> CopyText);
