using AITweaker.Core.Files;

namespace AITweaker.Core.Tests;

public class SettingsFileTests
{
    [Fact]
    public void JsonEditAndRollbackRestoreTheFile()
    {
        const string original = """{ "OPEN_ON_STARTUP": true, "BACKGROUND_COLOR": "#202225" }""";
        var before = new Dictionary<string, string?>();
        var edited = SettingsFile.Edit(original, "json", new Dictionary<string, string?> { ["OPEN_ON_STARTUP"] = "false", ["MINIMIZE_TO_TRAY"] = "true" }, before);

        Assert.True(SettingsFile.Holds(edited, "json", new Dictionary<string, string> { ["OPEN_ON_STARTUP"] = "false", ["MINIMIZE_TO_TRAY"] = "true" }));
        Assert.Equal("true", before["OPEN_ON_STARTUP"]);
        Assert.Null(before["MINIMIZE_TO_TRAY"]);

        var restored = SettingsFile.Edit(edited, "json", before, []);
        var r = SettingsFile.Read(restored, "json", ["OPEN_ON_STARTUP", "MINIMIZE_TO_TRAY", "BACKGROUND_COLOR"]);
        Assert.Equal("true", r["OPEN_ON_STARTUP"]);
        Assert.Null(r["MINIMIZE_TO_TRAY"]);
        Assert.Equal("\"#202225\"", r["BACKGROUND_COLOR"]);
    }

    [Fact]
    public void PrefsKeepOtherLinesAndQuotes()
    {
        const string original = "storage.size=2048\r\napp.autostart-mode=\"normal\"\r\nui.zoom_level=0\r\n";
        var before = new Dictionary<string, string?>();
        var edited = SettingsFile.Edit(original, "prefs", new Dictionary<string, string?> { ["app.autostart-mode"] = "\"off\"", ["app.autostart-configured"] = "true" }, before);

        Assert.Equal("storage.size=2048\napp.autostart-mode=\"off\"\nui.zoom_level=0\napp.autostart-configured=true\n", edited);
        Assert.True(SettingsFile.Holds(edited, "prefs", new Dictionary<string, string> { ["app.autostart-mode"] = "\"off\"" }));
        Assert.False(SettingsFile.Holds(original, "prefs", new Dictionary<string, string> { ["app.autostart-mode"] = "\"off\"" }));

        var restored = SettingsFile.Edit(edited, "prefs", before, []);
        Assert.Equal("storage.size=2048\napp.autostart-mode=\"normal\"\nui.zoom_level=0\n", restored);
    }

    [Fact]
    public void IniEditsKeysInTheirSectionAndRollsBack()
    {
        // OBS basic.ini: CRLF, keys spread over sections, one section missing
        const string original = "[General]\r\nName=Untitled\r\n\r\n[Output]\r\nMode=Simple\r\n\r\n[AdvOut]\r\nEncoder=obs_x264\r\nRecRB=false\r\n";
        var values = new Dictionary<string, string?>
        {
            ["Output/Mode"] = "\"Advanced\"",
            ["AdvOut/RecRB"] = "true",
            ["AdvOut/RecRBTime"] = "30",
            ["Video/OutputCX"] = "1664",
        };
        var before = new Dictionary<string, string?>();
        var edited = SettingsFile.Edit(original, "ini", values, before);

        Assert.Equal("[General]\r\nName=Untitled\r\n\r\n[Output]\r\nMode=Advanced\r\n\r\n[AdvOut]\r\nEncoder=obs_x264\r\nRecRB=true\r\nRecRBTime=30\r\n\r\n[Video]\r\nOutputCX=1664\r\n", edited);
        Assert.True(SettingsFile.Holds(edited, "ini", values.ToDictionary(kv => kv.Key, kv => kv.Value!)));
        Assert.True(SettingsFile.Holds("[AdvOut]\nRecRB=True\n", "ini", new Dictionary<string, string> { ["AdvOut/RecRB"] = "true" }));
        Assert.False(SettingsFile.Holds(original, "ini", new Dictionary<string, string> { ["Output/Mode"] = "\"Advanced\"" }));
        Assert.Equal("\"Simple\"", before["Output/Mode"]);
        Assert.Null(before["Video/OutputCX"]);

        var restored = SettingsFile.Edit(edited, "ini", before, []);
        Assert.Equal(SettingsFile.Read(original, "ini", values.Keys), SettingsFile.Read(restored, "ini", values.Keys));
        Assert.Contains("Mode=Simple", restored);
        Assert.DoesNotContain("RecRBTime", restored);
    }

    [Fact]
    public void IniDoesNotConfuseSameKeyInAnotherSection()
    {
        const string original = "[SimpleOutput]\nRecRB=false\n[AdvOut]\nRecRB=false\n";
        var edited = SettingsFile.Edit(original, "ini", new Dictionary<string, string?> { ["AdvOut/RecRB"] = "true" }, []);
        Assert.Equal("[SimpleOutput]\nRecRB=false\n[AdvOut]\nRecRB=true\n", edited);
    }

    [Fact]
    public void EmptyFileIsAnEmptyObject()
    {
        var edited = SettingsFile.Edit("", "json", new Dictionary<string, string?> { ["A"] = "1" }, []);
        Assert.True(SettingsFile.Holds(edited, "json", new Dictionary<string, string> { ["A"] = "1" }));
    }
}
