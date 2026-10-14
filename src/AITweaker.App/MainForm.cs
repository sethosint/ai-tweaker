using System.Reflection;
using AITweaker.App.Bridge;
using AITweaker.App.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AITweaker.App;

public sealed class MainForm : Form
{
    private const string Host = "https://app.aitweaker";
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0xEC, 0xE8, 0xDE) };
    private readonly Dictionary<string, string> _resources;
    /// <summary>Changes with every build: index.html asks for js/css with it, so a cached old file is never used.</summary>
    private readonly string _stamp = $"{Updater.Current.ToString(3)}-{File.GetLastWriteTimeUtc(Environment.ProcessPath ?? typeof(MainForm).Assembly.Location).Ticks:x}";
    private BridgeHost? _bridge;
    private Telemetry? _telemetry;

    public MainForm()
    {
        Text = "AI Tweaker";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1480, 920);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0xEC, 0xE8, 0xDE);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* default icon */ }
        Controls.Add(_web);

        // Map "web/guides/discord-overlay.webp" style logical names regardless of the build OS path separator.
        _resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("web/", StringComparison.Ordinal))
            .ToDictionary(n => n.Replace('\\', '/')[4..], n => n, StringComparer.OrdinalIgnoreCase);

        Load += async (_, _) => await InitAsync();
        FormClosing += OnFormClosing;
        // minimized: the page stops drawing (WebView2 reports it hidden) and the live sampling slows down
        Resize += (_, _) =>
        {
            bool minimized = WindowState == FormWindowState.Minimized;
            if (_telemetry is not null) _telemetry.Background = minimized;
            if (_web.CoreWebView2 is not null && _web.Visible == minimized) _web.Visible = !minimized;
        };
        FormClosed += (_, _) =>
        {
            _telemetry?.Dispose();
            // Game Mode only lasts while AI Tweaker is open: closing it puts the paused services and closed apps back.
            if (GameSession.Active) Task.Run(GameSession.StopAsync).Wait(TimeSpan.FromSeconds(30));
        };
    }

    private bool _closeAllowed;

    /// <summary>Closing in the middle of an image build or a USB write would leave a mounted image and loaded hives behind: stop the job cleanly first.</summary>
    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeAllowed || _bridge is null || !_bridge.Api.LongTaskRunning || e.CloseReason == CloseReason.WindowsShutDown) return;
        e.Cancel = true;
        var answer = MessageBox.Show(this,
            "AI Tweaker ещё работает (сборка образа, запись флешки или установка). Остановить и закрыть?\n\nAI Tweaker is still working (image build, USB or install). Stop it and close?",
            "AI Tweaker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        Text = "AI Tweaker — остановка… / stopping…";
        await _bridge.Api.StopAsync(TimeSpan.FromMinutes(3));
        _closeAllowed = true;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme(SettingsStore.Current.Theme);
    }

    public void ApplyTheme(string theme)
    {
        bool night = theme == "night" || (theme == "auto" && SystemThemeReader.Read().Dark);
        Native.StyleTitleBar(Handle, night, night ? 0x0E0F0C : 0xECE8DE, night ? 0xF2A93B : 0x1D1D1B);
        BackColor = night ? Color.FromArgb(0x0E, 0x0F, 0x0C) : Color.FromArgb(0xEC, 0xE8, 0xDE);
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Для работы AI Tweaker нужен Microsoft Edge WebView2 Runtime (он встроен в Windows 11).\n\nСейчас откроется страница загрузки.\n\nAI Tweaker needs the Microsoft Edge WebView2 Runtime.",
                "AI Tweaker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ProcessRunner.OpenUrl("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        // UI files are cached for good (they live inside the exe); a new or rebuilt exe starts from an empty cache
        if (SettingsStore.Current.WebCacheStamp != _stamp)
        {
            try { await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); }
            catch (Exception ex) { AppLog.Warn("app", "web cache not cleared: " + ex.Message); }
            SettingsStore.Current.WebCacheStamp = _stamp;
            SettingsStore.Save();
        }
        var s = core.Settings;
#if !DEBUG
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
#endif
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;

        core.AddWebResourceRequestedFilter(Host + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = Serve(core.Environment, e.Request.Uri);

        // The UI never navigates away: external links open in the default browser.
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Host, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            TryOpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            TryOpenExternal(e.Uri);
        };

        _bridge = new BridgeHost(this, core);
        _bridge.Api.ThemeChanged = theme => BeginInvoke(() => ApplyTheme(theme));

        _telemetry = new Telemetry();
        _telemetry.Sample += sample => _bridge.Emit("telemetry", sample);
        _telemetry.SensorsRead += sample => _bridge.Emit("sensors", sample);
        _telemetry.Start();

        // Accent color / light-dark switches in Windows Settings are pushed to the UI live.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        FormClosed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        core.Navigate(Host + "/index.html");
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
            _bridge?.Emit("system.theme", SystemThemeReader.Read());
    }

    private static void TryOpenExternal(string uri)
    {
        try { ProcessRunner.OpenUrl(uri); }
        catch (Exception ex) { AppLog.Warn("ui", $"blocked navigation to {uri}: {ex.Message}"); }
    }

    private CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, string uri)
    {
        var path = Uri.UnescapeDataString(new Uri(uri).AbsolutePath.TrimStart('/'));
        if (path.Length == 0) path = "index.html";
        if (!_resources.TryGetValue(path, out var resource))
            return env.CreateWebResourceResponse(null, 404, "Not Found", "");

        var ms = new MemoryStream();
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!) s.CopyTo(ms);
        if (path == "index.html")
        {
            var html = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            html = System.Text.RegularExpressions.Regex.Replace(html, @"(src|href)=""((?:js|css|vendor)/[^""?]+\.(?:js|css))""", m => $"{m.Groups[1].Value}=\"{m.Groups[2].Value}?v={_stamp}\"");
            ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
        }
        ms.Position = 0;
        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
        var cache = path == "index.html" ? "no-cache" : "max-age=31536000, immutable";
        return env.CreateWebResourceResponse(ms, 200, "OK", $"Content-Type: {type}\nCache-Control: {cache}");
    }
}
