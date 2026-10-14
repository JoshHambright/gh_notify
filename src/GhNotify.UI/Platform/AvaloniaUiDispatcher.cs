using Avalonia.Threading;

namespace GhNotify.UI;

/// <summary>Avalonia's UI thread as an <see cref="IUiDispatcher"/>.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
