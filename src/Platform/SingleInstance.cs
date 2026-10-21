using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ScreenLossless;

/// <summary>One UI per user and desktop session; subsequent launches only request activation.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private bool _disposed;
    public bool IsPrimary { get; }

    internal static string DefaultName
    {
        get
        {
            string identity = Environment.UserDomainName + "\\" + Environment.UserName + ":" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
            return "ScreenLossless-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        }
    }
    public SingleInstance(string? name = null)
    {
        _pipeName = name ?? DefaultName;
        _mutex = new Mutex(true, @"Local\" + _pipeName, out bool created);
        IsPrimary = created;
    }
    public void Listen(Action activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Activation listener is already owned.");
        _listener = Task.Run(async () =>
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_shutdown.Token);
                    // An incomplete client must not block subsequent launches indefinitely.
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    request.CancelAfter(1500);
                    var command = new byte[1];
                    if (await server.ReadAsync(command, request.Token) == 1 && command[0] == 1)
                    {
                        activate();
                        await server.WriteAsync(new byte[] { 1 }, request.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { }
                catch (Exception e) { System.Diagnostics.Trace.WriteLine(L10n.T("唤回窗口失败：") + e.Message); break; }
            }
        });
    }
    public async Task<bool> ActivateExistingAsync(int timeoutMs = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(new byte[] { 1 }, timeout.Token);
            var reply = new byte[1];
            return await client.ReadAsync(reply, timeout.Token) == 1 && reply[0] == 1;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        // The creating STA thread owns the mutex for the lifetime of Application.Run.
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        if (_listener is { } task) _ = task.ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
        else _shutdown.Dispose();
    }
}