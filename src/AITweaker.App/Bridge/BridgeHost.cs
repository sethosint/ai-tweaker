using System.Text.Json;
using System.Text.Json.Serialization;
using AITweaker.App.Platform;
using Microsoft.Web.WebView2.Core;

namespace AITweaker.App.Bridge;

/// <summary>JSON-RPC over WebView2 messages: {id, method, params} → {id, ok, result | error}; events: {event, data}.</summary>
public sealed class BridgeHost
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly Control _ui;
    private readonly CoreWebView2 _web;
    public ApiService Api { get; }

    public BridgeHost(Control ui, CoreWebView2 web)
    {
        _ui = ui;
        _web = web;
        Api = new ApiService(OnUiAsync, Emit);
        _web.WebMessageReceived += OnMessage;
        AppLog.Written += e => Emit("log", e);
    }

    private Task<object?> OnUiAsync(Func<object?> work)
    {
        var tcs = new TaskCompletionSource<object?>();
        _ui.BeginInvoke(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public void Emit(string name, object? data) => Post(new { @event = name, data });

    private void Post(object message)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(message, Json);
        }
        catch (Exception ex)
        {
            json = JsonSerializer.Serialize(new { @event = "log", data = new LogEntry(DateTime.Now, "error", "bridge", "serialization failed: " + ex.Message) }, Json);
        }
        if (_ui.IsDisposed) return;
        try { _ui.BeginInvoke(() => { try { _web.PostWebMessageAsJson(json); } catch (InvalidOperationException) { } }); }
        catch (InvalidOperationException) { /* window closing */ }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        string method = "";
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt64();
            method = root.GetProperty("method").GetString() ?? "";
            var p = root.TryGetProperty("params", out var pe) ? pe.Clone() : default;
            var result = await Task.Run(() => Api.InvokeAsync(method, p));
            Post(new { id, ok = true, result });
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException { InnerException: { } ie } ? ie : ex;
            AppLog.Error("api", $"{method}: {inner.Message}");
            Post(new { id, ok = false, error = inner.Message });
        }
    }
}
