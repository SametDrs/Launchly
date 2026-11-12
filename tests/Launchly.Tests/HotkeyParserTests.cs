using Launchly.Core;
using Launchly.Core.Storage;

namespace Launchly.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void AltSpace_Parses()
    {
        Assert.True(HotkeyParser.TryParse("Alt+Space", out var h));
        Assert.Equal(HotkeyParser.ModAlt, h.Modifiers);
        Assert.Equal(0x20u, h.VirtualKey);
    }

    [Fact]
    public void MultipleModifiers_AreCombined_CaseInsensitive()
    {
        Assert.True(HotkeyParser.TryParse(" ctrl + SHIFT + k ", out var h));
        Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModShift, h.Modifiers);
        Assert.Equal((uint)'K', h.VirtualKey);
    }

    [Fact]
    public void FunctionKey_Parses()
    {
        Assert.True(HotkeyParser.TryParse("Win+F12", out var h));
        Assert.Equal(0x7Bu, h.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Alt")]
    [InlineData("Space")]
    [InlineData("Alt+")]
    [InlineData("Alt+Foo")]
    [InlineData("Alt+A+B")]
    [InlineData("Shift+A")]
    [InlineData("Shift+Space")]
    public void Invalid_ReturnsFalse(string? text) => Assert.False(HotkeyParser.TryParse(text, out _));
}

public class HotkeyStrongModifierTests
{
    [Fact]
    public void TryFormat_RejectsShiftOnlyAndNoModifier()
    {
        Assert.False(HotkeyParser.TryFormat(HotkeyParser.ModShift, 0x41, out _));
        Assert.False(HotkeyParser.TryFormat(0, 0x41, out _));
        Assert.True(HotkeyParser.TryFormat(HotkeyParser.ModShift | HotkeyParser.ModWin, 0x41, out var text));
        Assert.Equal("Shift+Win+A", text);
    }
}

public class SettingsBackdropTests
{
    [Fact]
    public void Backdrop_DefaultsToAcrylic_AndSerializesAsString()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launchly-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var storage = new JsonStorage<Settings>(path);
            var s = storage.Load();
            Assert.Equal(BackdropType.Acrylic, s.Backdrop);
            s.Backdrop = BackdropType.Mica;
            storage.Save(s);
            Assert.Contains("\"Mica\"", File.ReadAllText(path));
            Assert.Equal(BackdropType.Mica, storage.Load().Normalize().Backdrop);   // v2 dosyasında açık Mica seçimi korunur
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
