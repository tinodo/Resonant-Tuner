using Microsoft.UI.Dispatching;
using Resonant.Core.Audio;

namespace Resonant.App;

/// <summary>Posts work to the WinUI dispatcher queue captured on the UI thread.</summary>
public sealed class DispatcherQueueDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueDispatcher(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Post(Action action)
    {
        if (!_queue.TryEnqueue(() => action()))
            throw new InvalidOperationException("The UI dispatcher is shutting down.");
    }
}
