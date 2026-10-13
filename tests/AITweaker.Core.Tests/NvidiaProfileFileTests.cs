using System.Xml.Linq;
using AITweaker.Core.Catalog;
using AITweaker.Core.Model;
using AITweaker.Core.Nvidia;

namespace AITweaker.Core.Tests;

public class NvidiaProfileFileTests
{
    [Fact]
    public void BuildsWhatProfileInspectorDeserializes()
    {
        var xml = NvidiaProfileFile.Build([new(0x1057EB71, "Power management mode", 1), new(0x007BA09E, "Maximum pre-rendered frames", 1)]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-16\"?>", xml);
        var root = XDocument.Parse(xml).Root!;
        Assert.Equal("ArrayOfProfile", root.Name.LocalName);
        var p = Assert.Single(root.Elements("Profile"));
        Assert.Equal("Base Profile", (string?)p.Element("ProfileName"));
        Assert.NotNull(p.Element("Executeables")); // sic: the serializer's field name
        var s = p.Element("Settings")!.Elements("ProfileSetting").First();
        Assert.Equal(new[] { "SettingNameInfo", "SettingID", "SettingValue", "ValueType" }, s.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("274197361", (string?)s.Element("SettingID")); // decimal, not hex
        Assert.Equal("1", (string?)s.Element("SettingValue"));
        Assert.Equal("Dword", (string?)s.Element("ValueType"));
    }

    [Fact]
    public void ReadsBaseProfileDwordsFromAnExport()
    {
        const string export = """
            <?xml version="1.0" encoding="utf-16"?>
            <ArrayOfProfile>
              <Profile><ProfileName>Counter-strike 2</ProfileName><Executeables><string>cs2.exe</string></Executeables>
                <Settings><ProfileSetting><SettingNameInfo /><SettingID>274197361</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting></Settings></Profile>
              <Profile><ProfileName>Base Profile</ProfileName><Executeables />
                <Settings>
                  <ProfileSetting><SettingNameInfo>Power management mode</SettingNameInfo><SettingID>274197361</SettingID><SettingValue>3</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
                  <ProfileSetting><SettingNameInfo /><SettingID>1234</SettingID><SettingValue>abc</SettingValue><ValueType>String</ValueType></ProfileSetting>
                  <ProfileSetting><SettingNameInfo /><SettingID>11306135</SettingID><SettingValue>4294967295</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
                </Settings></Profile>
            </ArrayOfProfile>
            """;
        var d = NvidiaProfileFile.ReadDwords(export.Trim());
        Assert.Equal(2, d.Count);
        Assert.Equal(3u, d[0x1057EB71]);
        Assert.Equal(uint.MaxValue, d[0x00AC8497]);
        Assert.Empty(NvidiaProfileFile.ReadDwords(export.Trim(), "Missing"));
    }

    [Fact]
    public void RoundTrips()
    {
        var xml = NvidiaProfileFile.Build([new(0x00CE2691, "Texture filtering - Quality", 20)]);
        Assert.Equal(20u, NvidiaProfileFile.ReadDwords(xml)[0x00CE2691]);
    }

    [Fact]
    public void CatalogNvidiaSettingsUseKnownDriverIds()
    {
        // ids from NvApiDriverSettings.h and Profile Inspector's CustomSettingNames.xml
        var known = new Dictionary<uint, string>
        {
            [0x1057EB71] = "PREFERRED_PSTATE", [0x007BA09E] = "PRERENDERLIMIT", [0x0005F543] = "ULL CPL state",
            [0x10835000] = "ULL enabled", [0x0064B541] = "REFRESH_RATE_OVERRIDE", [0x00CE2691] = "QUALITY_ENHANCEMENTS",
        };
        var actions = TweakCatalog.LoadEmbedded().Tweaks.SelectMany(t => t.Actions).OfType<NvidiaProfileAction>().ToList();
        Assert.NotEmpty(actions);
        foreach (var s in actions.SelectMany(a => a.Settings))
        {
            Assert.True(known.ContainsKey(s.SettingId), s.Id);
            Assert.NotEqual(s.Value, s.Default);
        }
    }
}

public class NvidiaProfileMergeTests
{
    private const string Export = """
        <?xml version="1.0" encoding="utf-16"?>
        <ArrayOfProfile>
          <Profile><ProfileName>Base Profile</ProfileName><Executeables />
            <Settings>
              <ProfileSetting><SettingNameInfo>Power management mode</SettingNameInfo><SettingID>274197361</SettingID><SettingValue>3</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
              <ProfileSetting><SettingNameInfo>Some string</SettingNameInfo><SettingID>1234</SettingID><SettingValue>keep me</SettingValue><ValueType>String</ValueType></ProfileSetting>
            </Settings></Profile>
          <Profile><ProfileName>Some Game</ProfileName><Executeables /><Settings /></Profile>
        </ArrayOfProfile>
        """;

    [Fact]
    public void KeepsEverythingElseInTheProfile()
    {
        var xml = NvidiaProfileFile.WithSettings(Export.Trim(), [new(0x1057EB71, "Power management mode", 1), new(0x007BA09E, "Maximum pre-rendered frames", 1)]);
        var root = XDocument.Parse(xml).Root!;
        var p = Assert.Single(root.Elements("Profile")); // other profiles are not re-imported
        Assert.Equal("Base Profile", (string?)p.Element("ProfileName"));
        var rows = p.Element("Settings")!.Elements("ProfileSetting").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => (string?)r.Element("SettingID") == "1234" && (string?)r.Element("SettingValue") == "keep me" && (string?)r.Element("ValueType") == "String");
        var d = NvidiaProfileFile.ReadDwords(xml);
        Assert.Equal(1u, d[0x1057EB71]);
        Assert.Equal(1u, d[0x007BA09E]);
    }

    [Fact]
    public void StartsFreshWhenNothingWasCustomized()
    {
        var empty = """<?xml version="1.0" encoding="utf-16"?><ArrayOfProfile />""";
        Assert.Equal(20u, NvidiaProfileFile.ReadDwords(NvidiaProfileFile.WithSettings(empty, [new(0x00CE2691, "Texture filtering - Quality", 20)]))[0x00CE2691]);
        Assert.Equal(20u, NvidiaProfileFile.ReadDwords(NvidiaProfileFile.WithSettings(null, [new(0x00CE2691, "Texture filtering - Quality", 20)]))[0x00CE2691]);
    }
}
