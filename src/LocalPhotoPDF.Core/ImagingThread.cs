using System.Windows.Threading;

namespace LocalPhotoPDF.Core;

/// <summary>
/// One dedicated STA thread that owns a single <see cref="Dispatcher"/>, used for all WPF imaging
/// work (thumbnail creation and page rendering).
/// </summary>
/// <remarks>
/// <para>
/// Previously both services called <c>Task.Run</c>. Every WPF visual is a <c>DispatcherObject</c>,
/// and constructing one calls <c>Dispatcher.CurrentDispatcher</c>, which <em>creates</em> a
/// Dispatcher for whatever thread it happens to land on. <c>RenderTargetBitmap.Render</c> then
/// attaches a MediaContext, with its render-thread channel and unmanaged resources, to that same
/// Dispatcher. Nothing ever shut them down, so each fresh pool thread that serviced the work added
/// one permanently and memory grew across a session and never came back.
/// </para>
/// <para>
/// Shutting the Dispatcher down at the end of each <c>Task.Run</c> would be worse, not better:
/// pool threads are reused, and a thread carrying an already shut-down Dispatcher throws the next
/// time anything asks it for one. Hence a thread we own, whose lifetime we control.
/// </para>
/// <para>
/// The thread is a background thread, so it can never keep the process alive on its own, and the
/// cost is bounded at exactly one Dispatcher no matter how many photos or batches are processed.
/// </para>
/// </remarks>
internal sealed class ImagingThread : IDisposable
{
    private static readonly Lazy<ImagingThread> LazyShared = new(
        () => new ImagingThread(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;
    private bool _isDisposed;

    private ImagingThread()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "LocalPhotoPDF imaging",
        };

        // WIC and several codecs expect an STA host thread.
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    internal static ImagingThread Shared => LazyShared.Value;

    /// <summary>
    /// Runs <paramref name="work"/> on the imaging thread and returns its result.
    /// </summary>
    internal Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        return _dispatcher!
            .InvokeAsync(work, DispatcherPriority.Normal, cancellationToken)
            .Task;
    }

    /// <summary>
    /// Shuts the imaging thread down. Called once as the application exits; safe to call more than
    /// once, and safe never to call at all because the thread is a background thread.
    /// </summary>
    internal static void ShutdownShared()
    {
        if (LazyShared.IsValueCreated)
        {
            LazyShared.Value.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _dispatcher?.InvokeShutdown();

        // Bounded: never block application exit on a stuck codec.
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void ThreadMain()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }
}
