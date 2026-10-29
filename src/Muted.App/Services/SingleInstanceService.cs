using System.IO;
using System.IO.Pipes;
using Muted.Core.Support;

namespace Muted.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\Muted.Desktop.Singleton";
    private const string ActivationEventName = @"Local\Muted.Desktop.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly ManualResetEvent? _shutdownEvent;
    private readonly Thread? _listenerThread;
    private readonly Thread? _commandThread;
    private readonly CancellationTokenSource _commandCancellation = new();
    private readonly object _activationSync = new();
    private EventHandler? _activationRequested;
    private bool _pendingActivation;
    private int _disposed;

    /// <summary>
    /// One pipe per Windows session, so two people signed in at once each drive their own copy.
    /// The default pipe security only lets the same user write to it.
    /// </summary>
    private static readonly string CommandPipeName =
        $"Muted.Desktop.Command.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <param name="activateExisting">
    /// False when this launch only carries a command, like "--toggle-mute", so the
    /// running copy does its thing without jumping to the front.
    /// </param>
    public SingleInstanceService(bool activateExisting = true)
    {
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimary = createdNew;

        if (!IsPrimary)
        {
            if (activateExisting)
            {
                _activationEvent.Set();
            }

            return;
        }

        _shutdownEvent = new ManualResetEvent(false);
        _listenerThread = new Thread(Listen)
        {
            IsBackground = true,
            Name = "Muted activation listener"
        };
        _listenerThread.Start();

        _commandThread = new Thread(ListenForCommands)
        {
            IsBackground = true,
            Name = "Muted command listener"
        };
        _commandThread.Start();
    }

    /// <summary>Raised on a background thread for each command another launch sends.</summary>
    public event EventHandler<RemoteRequest>? CommandReceived;

    /// <summary>Hands a command to the copy that is already running. False when none answered.</summary>
    public static bool TrySend(RemoteRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out);
            client.Connect(2_000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(request.Serialize());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ListenForCommands()
    {
        var token = _commandCancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    CommandPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                using var reader = new StreamReader(server);
                var readTask = reader.ReadLineAsync(token).AsTask();
                if (!readTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    continue;
                }

                var request = RemoteRequest.Parse(readTask.Result);
                if (request is not null)
                {
                    CommandReceived?.Invoke(this, request.Value);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (AggregateException exception) when (exception.InnerException is OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A broken connection only loses that one command. Back off a little so
                // a pipe that keeps failing cannot spin.
                if (token.WaitHandle.WaitOne(250))
                {
                    return;
                }
            }
        }
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
        _commandCancellation.Cancel();
        _commandThread?.Join(TimeSpan.FromSeconds(1));
        _commandCancellation.Dispose();
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
