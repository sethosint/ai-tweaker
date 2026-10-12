using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AITweaker.App.Platform;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Console tools (netsh, bcdedit, sc...) print in the OEM code page (866 on Russian Windows).</summary>
    private static Encoding OemEncoding
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static async Task<ProcessResult> RunAsync(string file, string args, TimeSpan? timeout = null, Action<string>? onLine = null, Encoding? encoding = null, CancellationToken ct = default)
    {
        if (!Path.IsPathRooted(file) && File.Exists(System32(file))) file = System32(file);

        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? OemEncoding,
            StandardErrorEncoding = encoding ?? OemEncoding,
            WorkingDirectory = Environment.SystemDirectory,
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.AppendLine(e.Data);
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(2));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new ProcessResult(-1, stdout.ToString(), "Timed out");
        }
        p.WaitForExit(); // flush async readers
        return new ProcessResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Runs Windows PowerShell 5.1 with a script passed as -EncodedCommand (no quoting issues), UTF-8 output.</summary>
    public static Task<ProcessResult> PowerShellAsync(string script, TimeSpan? timeout = null, Action<string>? onLine = null, CancellationToken ct = default)
    {
        var full = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return RunAsync(exe, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeout, onLine, Encoding.UTF8, ct);
    }

    /// <summary>Quotes a value for embedding in a PowerShell single-quoted string.</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    public static void OpenUrl(string url)
    {
        if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only web and ms-settings links can be opened");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    /// <summary>Opens Explorer with the file selected (only for files that exist).</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
