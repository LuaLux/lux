using System.Text;
using Nebra.Runtime.Bindings;
using SysConsole = System.Console;

namespace Nebra.Runtime.Library;

/// <summary>
/// Terminal/prompt utilities exposed to Lua as the <c>console</c> global.
/// Provides colored output plus interactive prompts (input, password, confirm,
/// select, multiselect, spinner, progress bar). Non-interactive streams fall back
/// to defaults and print a warning; prompts without defaults error out.
/// </summary>
[NebraExport("console")]
public sealed class ConsoleLib
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[2m";
    private const string Bold = "\x1b[1m";
    private const string Cyan = "\x1b[36m";
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Red = "\x1b[31m";
    private const string Magenta = "\x1b[35m";

    private static bool IsInteractive => !SysConsole.IsInputRedirected && !SysConsole.IsOutputRedirected;

    [NebraExport("print")]
    public static void Print(string message) => SysConsole.WriteLine(message);

    [NebraExport("info")]
    public static void Info(string message) => SysConsole.WriteLine($"{Cyan}ℹ{Reset} {message}");

    [NebraExport("success")]
    public static void Success(string message) => SysConsole.WriteLine($"{Green}✓{Reset} {message}");

    [NebraExport("warn")]
    public static void Warn(string message) => SysConsole.WriteLine($"{Yellow}⚠{Reset} {message}");

    [NebraExport("error")]
    public static void ErrorMsg(string message) => SysConsole.WriteLine($"{Red}✗{Reset} {message}");

    [NebraExport("header")]
    public static void Header(string message)
        => SysConsole.WriteLine($"{Bold}{Magenta}{message}{Reset}");

    [NebraExport("dim")]
    public static void DimMsg(string message) => SysConsole.WriteLine($"{Dim}{message}{Reset}");

    [NebraExport("blank")]
    public static void Blank() => SysConsole.WriteLine();

    /// <summary>
    /// Reads a line from stdin. If <paramref name="defaultValue"/> is provided,
    /// an empty input falls back to it. Non-interactive runs return the default
    /// (or empty string) without blocking.
    /// </summary>
    [NebraExport("input")]
    public static string Input(string prompt, string? defaultValue = null)
    {
        var suffix = defaultValue != null ? $" {Dim}[{defaultValue}]{Reset}" : "";
        SysConsole.Write($"{Cyan}?{Reset} {prompt}{suffix} ");

        if (SysConsole.IsInputRedirected)
        {
            var piped = SysConsole.In.ReadLine();
            var pipedResult = !string.IsNullOrEmpty(piped) ? piped : defaultValue ?? "";
            SysConsole.WriteLine(pipedResult);
            return pipedResult;
        }

        var line = SysConsole.ReadLine() ?? "";
        return string.IsNullOrEmpty(line) ? defaultValue ?? "" : line;
    }

    /// <summary>
    /// Reads a line from stdin with masked echo. Falls back to a plain read
    /// when stdin is redirected.
    /// </summary>
    [NebraExport("password")]
    public static string Password(string prompt)
    {
        SysConsole.Write($"{Cyan}?{Reset} {prompt} ");

        if (SysConsole.IsInputRedirected)
        {
            var line = SysConsole.In.ReadLine() ?? "";
            SysConsole.WriteLine();
            return line;
        }

        var sb = new StringBuilder();
        while (true)
        {
            var key = SysConsole.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { SysConsole.WriteLine(); return sb.ToString(); }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
                SysConsole.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                SysConsole.Write('*');
            }
        }
    }

    /// <summary>
    /// Yes/no prompt. Accepts y/yes/n/no (case-insensitive). Blank input uses
    /// <paramref name="defaultValue"/>.
    /// </summary>
    [NebraExport("confirm")]
    public static bool Confirm(string prompt, bool defaultValue = false)
    {
        var hint = defaultValue ? "[Y/n]" : "[y/N]";
        SysConsole.Write($"{Cyan}?{Reset} {prompt} {Dim}{hint}{Reset} ");

        if (SysConsole.IsInputRedirected)
        {
            var line = SysConsole.In.ReadLine() ?? "";
            SysConsole.WriteLine(line);
            return ParseBool(line, defaultValue);
        }

        var input = SysConsole.ReadLine() ?? "";
        return ParseBool(input, defaultValue);
    }

    private static bool ParseBool(string input, bool fallback)
    {
        var trimmed = input.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "" => fallback,
            "y" or "yes" or "true" or "1" => true,
            "n" or "no" or "false" or "0" => false,
            _ => fallback
        };
    }

    /// <summary>
    /// Arrow-key select. Returns the chosen option as a string. When stdin is
    /// redirected, returns the option at <paramref name="defaultIndex"/>.
    /// </summary>
    [NebraExport("select")]
    public static string Select(string prompt, IList<object?> options, long defaultIndex = 1)
    {
        var opts = options.Select(o => o?.ToString() ?? "").ToList();
        if (opts.Count == 0) throw new ArgumentException("select: options must not be empty");

        var current = Math.Clamp((int)defaultIndex - 1, 0, opts.Count - 1);

        if (!IsInteractive)
        {
            SysConsole.WriteLine($"{Cyan}?{Reset} {prompt} {Dim}→ {opts[current]}{Reset}");
            return opts[current];
        }

        SysConsole.WriteLine($"{Cyan}?{Reset} {prompt} {Dim}(↑/↓, Enter){Reset}");
        RenderSelect(opts, current, null);
        SysConsole.CursorVisible = false;

        try
        {
            while (true)
            {
                var key = SysConsole.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.UpArrow && current > 0) { current--; RenderSelect(opts, current, null); }
                else if (key.Key == ConsoleKey.DownArrow && current < opts.Count - 1) { current++; RenderSelect(opts, current, null); }
                else if (key.Key == ConsoleKey.Enter)
                {
                    MoveCursorDown(opts.Count);
                    SysConsole.WriteLine($"  {Dim}→ {opts[current]}{Reset}");
                    return opts[current];
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    MoveCursorDown(opts.Count);
                    SysConsole.WriteLine($"  {Dim}→ {opts[current]}{Reset}");
                    return opts[current];
                }
            }
        }
        finally
        {
            SysConsole.CursorVisible = true;
        }
    }

    /// <summary>
    /// Multi-select via Space (toggle) and Enter (confirm). Returns a list of
    /// selected option strings. When stdin is redirected, returns the defaults.
    /// </summary>
    [NebraExport("multiselect")]
    public static List<object?> MultiSelect(string prompt, IList<object?> options, IList<object?>? defaults = null)
    {
        var opts = options.Select(o => o?.ToString() ?? "").ToList();
        if (opts.Count == 0) throw new ArgumentException("multiselect: options must not be empty");

        var selected = new HashSet<int>();
        if (defaults != null)
        {
            foreach (var d in defaults)
            {
                var s = d?.ToString();
                var idx = opts.IndexOf(s ?? "");
                if (idx >= 0) selected.Add(idx);
            }
        }

        if (!IsInteractive)
        {
            var picks = selected.OrderBy(i => i).Select(i => (object?)opts[i]).ToList();
            SysConsole.WriteLine($"{Cyan}?{Reset} {prompt} {Dim}→ [{string.Join(", ", picks)}]{Reset}");
            return picks;
        }

        var current = 0;
        SysConsole.WriteLine($"{Cyan}?{Reset} {prompt} {Dim}(Space toggle, Enter confirm){Reset}");
        RenderMulti(opts, selected, current);
        SysConsole.CursorVisible = false;

        try
        {
            while (true)
            {
                var key = SysConsole.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.UpArrow && current > 0) { current--; RenderMulti(opts, selected, current); }
                else if (key.Key == ConsoleKey.DownArrow && current < opts.Count - 1) { current++; RenderMulti(opts, selected, current); }
                else if (key.Key == ConsoleKey.Spacebar)
                {
                    if (!selected.Add(current)) selected.Remove(current);
                    RenderMulti(opts, selected, current);
                }
                else if (key.Key == ConsoleKey.A && (key.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (selected.Count == opts.Count) selected.Clear();
                    else for (var i = 0; i < opts.Count; i++) selected.Add(i);
                    RenderMulti(opts, selected, current);
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    MoveCursorDown(opts.Count);
                    var picks = selected.OrderBy(i => i).Select(i => (object?)opts[i]).ToList();
                    SysConsole.WriteLine($"  {Dim}→ [{string.Join(", ", picks)}]{Reset}");
                    return picks;
                }
            }
        }
        finally
        {
            SysConsole.CursorVisible = true;
        }
    }

    /// <summary>
    /// Starts an animated spinner and returns a handle. Call <c>:stop(success, message)</c>
    /// to finalize. When the terminal is non-interactive, the spinner prints its
    /// message once and the returned handle is a no-op.
    /// </summary>
    [NebraExport("spinner")]
    public static Spinner StartSpinner(string message) => new(message, IsInteractive);

    /// <summary>
    /// Starts a progress bar and returns a handle. Call <c>:update(current)</c>,
    /// <c>:increment()</c>, or <c>:finish()</c> to advance.
    /// </summary>
    [NebraExport("progress")]
    public static ProgressBar StartProgress(long total, string? message = null)
        => new(total, message ?? "", IsInteractive);

    private static void RenderSelect(List<string> opts, int current, int? _)
    {
        var buf = new StringBuilder();
        for (var i = 0; i < opts.Count; i++)
        {
            buf.Append("\x1b[2K\r");
            if (i == current) buf.Append($"{Cyan}❯ {opts[i]}{Reset}");
            else buf.Append($"  {Dim}{opts[i]}{Reset}");
            buf.Append('\n');
        }
        buf.Append($"\x1b[{opts.Count}A");
        SysConsole.Write(buf);
    }

    private static void RenderMulti(List<string> opts, HashSet<int> selected, int current)
    {
        var buf = new StringBuilder();
        for (var i = 0; i < opts.Count; i++)
        {
            buf.Append("\x1b[2K\r");
            var pointer = i == current ? $"{Cyan}❯{Reset}" : " ";
            var mark = selected.Contains(i) ? $"{Green}◉{Reset}" : $"{Dim}○{Reset}";
            var label = i == current ? $"{Cyan}{opts[i]}{Reset}" : opts[i];
            buf.Append($"{pointer} {mark} {label}\n");
        }
        buf.Append($"\x1b[{opts.Count}A");
        SysConsole.Write(buf);
    }

    private static void MoveCursorDown(int lines)
    {
        SysConsole.Write($"\x1b[{lines}B\r");
    }
}

/// <summary>
/// Animated spinner handle returned by <see cref="ConsoleLib.StartSpinner"/>.
/// Drives a background animation thread until <see cref="Stop"/> or
/// <see cref="Finish"/> finalizes the line.
/// </summary>
public sealed class Spinner
{
    private static readonly string[] Frames =
    [
        "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"
    ];

    private readonly Thread? _thread;
    private readonly CancellationTokenSource _cts = new();
    private volatile string _message;
    private readonly bool _interactive;
    private int _stopped;

    internal Spinner(string message, bool interactive)
    {
        _message = message;
        _interactive = interactive;

        if (!interactive)
        {
            SysConsole.WriteLine($"\x1b[36m…\x1b[0m {message}");
            return;
        }

        SysConsole.CursorVisible = false;
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    [NebraExport("update")]
    public void Update(string message) => _message = message;

    [NebraExport("stop")]
    public void Stop(bool success = true, string? message = null) => Finalize(success, message);

    [NebraExport("succeed")]
    public void Succeed(string? message = null) => Finalize(true, message);

    [NebraExport("fail")]
    public void Fail(string? message = null) => Finalize(false, message);

    private void Finalize(bool success, string? message)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        var finalMessage = message ?? _message;
        if (!_interactive)
        {
            var tag = success ? "\x1b[32m✓\x1b[0m" : "\x1b[31m✗\x1b[0m";
            SysConsole.WriteLine($"{tag} {finalMessage}");
            return;
        }

        _cts.Cancel();
        _thread?.Join();
        var marker = success ? "\x1b[32m✓\x1b[0m" : "\x1b[31m✗\x1b[0m";
        SysConsole.Write($"\r\x1b[2K{marker} {finalMessage}\n");
        SysConsole.CursorVisible = true;
    }

    private void Run()
    {
        var i = 0;
        while (!_cts.IsCancellationRequested)
        {
            var frame = Frames[i++ % Frames.Length];
            SysConsole.Write($"\r\x1b[2K\x1b[36m{frame}\x1b[0m {_message}");
            try { Thread.Sleep(80); } catch { break; }
        }
    }
}

/// <summary>
/// Progress bar handle returned by <see cref="ConsoleLib.StartProgress"/>.
/// Renders a bar of width 30 with percentage and optional message.
/// </summary>
public sealed class ProgressBar
{
    private readonly long _total;
    private long _current;
    private string _message;
    private readonly bool _interactive;
    private int _finished;

    internal ProgressBar(long total, string message, bool interactive)
    {
        _total = total <= 0 ? 1 : total;
        _message = message;
        _interactive = interactive;
        Render();
    }

    [NebraExport("update")]
    public void Update(long current, string? message = null)
    {
        _current = current;
        if (message != null) _message = message;
        Render();
    }

    [NebraExport("increment")]
    public void Increment(long step = 1)
    {
        _current += step;
        Render();
    }

    [NebraExport("setMessage")]
    public void SetMessage(string message)
    {
        _message = message;
        Render();
    }

    [NebraExport("finish")]
    public void Finish(string? message = null)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _current = _total;
        if (message != null) _message = message;
        Render(final: true);
    }

    private void Render(bool final = false)
    {
        const int width = 30;
        var ratio = (double)Math.Clamp(_current, 0, _total) / _total;
        var filled = (int)Math.Round(ratio * width);
        var bar = new string('█', filled) + new string('░', width - filled);
        var pct = (int)Math.Round(ratio * 100);
        var line = $"\x1b[36m{bar}\x1b[0m {pct,3}% {_message}";
        if (_interactive)
        {
            SysConsole.Write($"\r\x1b[2K{line}");
            if (final) SysConsole.WriteLine();
        }
        else if (final)
        {
            SysConsole.WriteLine(line);
        }
    }
}
