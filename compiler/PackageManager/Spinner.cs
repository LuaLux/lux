namespace Nebra.PackageManager;

/// <summary>
/// Lightweight terminal spinner for long-running PM operations (git fetch,
/// alias resolve, linking, …). Renders on <c>stderr</c> via carriage return
/// so it doesn't pollute redirected stdout. Becomes a no-op when the output
/// is not a TTY — CI logs and pipes stay clean.
/// </summary>
public sealed class Spinner : IDisposable
{
    private static readonly string[] Frames =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _renderLoop;
    private readonly bool _enabled;
    private string _message;
    private int _lastWidth;

    public Spinner(string initialMessage)
    {
        _message = initialMessage;
        _enabled = !Console.IsOutputRedirected && !Console.IsErrorRedirected;
        _renderLoop = _enabled ? RunAsync(_cts.Token) : Task.CompletedTask;
    }

    public void Update(string message) => _message = message;

    private async Task RunAsync(CancellationToken ct)
    {
        var i = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Draw(Frames[i % Frames.Length], _message);
                i++;
                await Task.Delay(80, ct);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    private void Draw(string frame, string message)
    {
        var line = $"  {frame} {message}";
        var pad = Math.Max(0, _lastWidth - line.Length);
        Console.Error.Write("\r" + line + new string(' ', pad));
        _lastWidth = line.Length;
    }

    /// <summary>
    /// Stops the spinner. If <paramref name="finalLine"/> is non-null it
    /// replaces the spinner line and is followed by a newline; otherwise the
    /// spinner line is erased cleanly.
    /// </summary>
    public void Stop(string? finalLine = null)
    {
        _cts.Cancel();
        try { _renderLoop.GetAwaiter().GetResult(); } catch { }
        if (!_enabled)
        {
            if (finalLine != null) Console.WriteLine(finalLine);
            return;
        }
        var clear = new string(' ', Math.Max(_lastWidth, 1));
        Console.Error.Write("\r" + clear + "\r");
        if (finalLine != null) Console.WriteLine(finalLine);
    }

    public void Dispose() => Stop();
}
