namespace Muted.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\Muted.Desktop.Singleton";
    private const string ActivationEventName = @"Local\Muted.Desktop.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly ManualResetEvent? _shutdownEvent;
    private readonly Thread? _listenerThread;
    private readonly object _activationSync = new();
    private EventHandler? _activationRequested;
    private bool _pendingActivation;
    private int _disposed;

    public SingleInstanceService()
    {
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimary = createdNew;

        if (!IsPrimary)
        {
            _activationEvent.Set();
            return;
        }

        _shutdownEvent = new ManualResetEvent(false);
        _listenerThread = new Thread(Listen)
        {
            IsBackground = true,
            Name = "Muted activation listener"
        };
        _listenerThread.Start();
    }

    public bool IsPrimary { get; }

    public event EventHandler? ActivationRequested
    {
        add
        {
            var invokePending = false;
            lock (_activationSync)
            {
                _activationRequested += value;
                if (_pendingActivation)
                {
                    _pendingActivation = false;
                    invokePending = true;
                }
            }

            if (invokePending)
            {
                value?.Invoke(this, EventArgs.Empty);
            }
        }
        remove
        {
            lock (_activationSync)
            {
                _activationRequested -= value;
            }
        }
    }

    private void Listen()
    {
        if (_shutdownEvent is null)
        {
            return;
        }

        WaitHandle[] handles = [_activationEvent, _shutdownEvent];
        while (WaitHandle.WaitAny(handles) == 0)
        {
            EventHandler? handler;
            lock (_activationSync)
            {
                handler = _activationRequested;
                if (handler is null)
                {
                    _pendingActivation = true;
                }
            }

            handler?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdownEvent?.Set();
        _listenerThread?.Join(TimeSpan.FromSeconds(1));
        _activationEvent.Dispose();
        _shutdownEvent?.Dispose();
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ownership can already be gone during abnormal shutdown.
            }
        }

        _mutex.Dispose();
    }
}
