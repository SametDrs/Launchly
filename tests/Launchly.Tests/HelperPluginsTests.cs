using Microsoft.Extensions.Logging.Abstractions;
using Launchly.Plugin;
using Launchly.Plugins.Calculator;
using Launchly.Plugins.Files;
using Launchly.Plugins.SystemCommands;
using Launchly.Plugins.Url;
using Launchly.Plugins.WebSearch;

namespace Launchly.Tests;

public sealed class HelperPluginsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchly-files-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Task<IReadOnlyList<Result>> Ask(IPlugin p, string raw, string keyword = "", string? search = null) =>
        p.QueryAsync(new Query(raw, keyword, search ?? raw), CancellationToken.None);

    // ---- Calculator ----
    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public void SetText(string text) => Texts.Add(text);
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public void SetText(string text) => throw new InvalidOperationException("boom");
    }

    [Theory]
    [InlineData("2+2*3", "8")]
    [InlineData("(2+2)*3", "12")]
    [InlineData("10/4", "2.5")]
    [InlineData("-2^2", "-4")]
    [InlineData("2^3^2", "512")]
    [InlineData("7 % 4", "3")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("-(3+4)", "-7")]
    public async Task Calculator_ValidExpression_ShowsResult_AndEnterCopies(string expr, string expected)
    {
        var clip = new FakeClipboard();
        var plugin = new CalculatorPlugin(clip, NullLogger.Instance);

        var results = await Ask(plugin, expr);

        var r = Assert.Single(results);
        Assert.Equal(expected, r.Title);
        Assert.True(await r.Action!());
        Assert.Equal([expected], clip.Texts);
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("5%0")]
    [InlineData("2+")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("2**3")]
    [InlineData("abc")]
    [InlineData("5")]
    [InlineData("2+a")]
    [InlineData("")]
    [InlineData("1e5+1")]
    public async Task Calculator_InvalidOrNonMath_ReturnsNothing(string expr)
    {
        var plugin = new CalculatorPlugin(new FakeClipboard(), NullLogger.Instance);
        Assert.Empty(await Ask(plugin, expr));
    }

    [Fact]
    public async Task Calculator_ClipboardFailure_ReturnsFalse()
    {
        var plugin = new CalculatorPlugin(new ThrowingClipboard(), NullLogger.Instance);
        var r = Assert.Single(await Ask(plugin, "1+1"));
        Assert.False(await r.Action!());
    }

    [Fact]
    public void Calculator_DeepNesting_FailsWithoutOverflow()
    {
        var expr = new string('(', 150) + "1" + new string(')', 150);
        Assert.False(ExpressionEvaluator.TryEvaluate(expr, out _));
    }

    // ---- Url ----
    [Theory]
    [InlineData("github.com", "https://github.com/")]
    [InlineData("www.github.com/foo?x=1", "https://www.github.com/foo?x=1")]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("HTTP://example.com", "http://example.com/")]
    [InlineData("localhost.dev:8080/x", "https://localhost.dev:8080/x")]
    [InlineData("  github.com  ", "https://github.com/")]
    public void Url_Detects_AndAddsScheme(string input, string expected)
    {
        Assert.True(UrlDetector.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///c:/windows")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.com")]
    [InlineData("hello")]
    [InlineData("hello world.com")]
    [InlineData("2.5")]
    [InlineData("a.b")]
    [InlineData("https://")]
    [InlineData("")]
    public void Url_RejectsNonHttpAndNonUrls(string input)
    {
        Assert.False(UrlDetector.TryNormalize(input, out _));
    }

    private sealed class FakeUrlOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    [Fact]
    public async Task Url_Plugin_ActionOpensNormalizedUrl()
    {
        var opener = new FakeUrlOpener();
        var plugin = new UrlPlugin(opener, NullLogger.Instance);

        var r = Assert.Single(await Ask(plugin, "github.com"));
        Assert.Equal("Open URL", r.Title);
        Assert.True(await r.Action!());
        Assert.Equal(["https://github.com/"], opener.Opened);
        Assert.Empty(await Ask(plugin, "ftp://x.com"));
    }

    // ---- System ----
    private sealed class FakeSystem : ISystemCommands
    {
        public List<string> Calls { get; } = [];

        public void Lock() => Calls.Add("lock");

        public void Sleep() => Calls.Add("sleep");

        public void Shutdown() => Calls.Add("shutdown");

        public void Restart() => Calls.Add("restart");

        public void EmptyRecycleBin() => Calls.Add("bin");
    }

    private sealed class FakeConfirm(bool answer) : IConfirmation
    {
        public int Asked { get; private set; }

        public bool Confirm(string title, string message)
        {
            Asked++;
            return answer;
        }
    }

    [Theory]
    [InlineData("lock", "Lock", "lock")]
    [InlineData("sleep", "Sleep", "sleep")]
    public async Task System_SafeCommands_RunWithoutConfirmation(string search, string title, string call)
    {
        var sys = new FakeSystem();
        var confirm = new FakeConfirm(false);
        var plugin = new SystemPlugin(sys, confirm, NullLogger.Instance);

        var r = (await Ask(plugin, search)).First(x => x.Title == title);
        Assert.True(await r.Action!());

        Assert.Equal([call], sys.Calls);
        Assert.Equal(0, confirm.Asked);
    }

    [Theory]
    [InlineData("shutdown", "shutdown")]
    [InlineData("restart", "restart")]
    [InlineData("recycle", "bin")]
    public async Task System_IrreversibleCommands_NeedConfirmation(string search, string call)
    {
        var sys = new FakeSystem();
        var no = new FakeConfirm(false);
        var r = (await Ask(new SystemPlugin(sys, no, NullLogger.Instance), search)).First();
        Assert.False(await r.Action!());
        Assert.Empty(sys.Calls);
        Assert.Equal(1, no.Asked);

        var yes = new FakeConfirm(true);
        var r2 = (await Ask(new SystemPlugin(sys, yes, NullLogger.Instance), search)).First();
        Assert.True(await r2.Action!());
        Assert.Equal([call], sys.Calls);
    }

    [Fact]
    public async Task System_NoMatch_AndEmpty_ReturnNothing()
    {
        var plugin = new SystemPlugin(new FakeSystem(), new FakeConfirm(true), NullLogger.Instance);
        Assert.Empty(await Ask(plugin, "zzzqq"));
        Assert.Empty(await Ask(plugin, ""));
    }

    // ---- Files ----
    private sealed class FakeFileOpener : IFileOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string path) => Opened.Add(path);
    }

    private FilesPlugin CreateFiles(FakeFileOpener opener)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub", "deep"));
        File.WriteAllText(Path.Combine(_dir, "rapor-2026.docx"), "x");
        File.WriteAllText(Path.Combine(_dir, "sub", "Rapor taslak.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "sub", "deep", "gizli rapor.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "notlar.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "hidden-rapor.txt"), "x");
        File.SetAttributes(Path.Combine(_dir, "hidden-rapor.txt"), FileAttributes.Hidden);
        return new FilesPlugin([_dir], opener, NullLogger.Instance);
    }

    [Fact]
    public async Task Files_FindsByNameRecursively_SkipsHidden_AndOpens()
    {
        var opener = new FakeFileOpener();
        var plugin = CreateFiles(opener);

        var results = await Ask(plugin, "f rapor", "f", "rapor");

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Contains("rapor", r.Title, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, r => r.Title.StartsWith("hidden", StringComparison.Ordinal));

        var first = results[0];
        Assert.True(await first.Action!());
        Assert.Equal([first.SubTitle], opener.Opened);
        Assert.StartsWith(_dir, first.SubTitle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Files_EmptySearch_NoMatch_AndCancellation()
    {
        var plugin = CreateFiles(new FakeFileOpener());

        Assert.Empty(await Ask(plugin, "f", "f", ""));
        Assert.Empty(await Ask(plugin, "f zzzqqq", "f", "zzzqqq"));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            plugin.QueryAsync(new Query("f rapor", "f", "rapor"), cts.Token));
    }

    [Fact]
    public async Task Files_MissingRoot_IsIgnored()
    {
        var plugin = new FilesPlugin([Path.Combine(_dir, "yok")], new FakeFileOpener(), NullLogger.Instance);
        Assert.Empty(await Ask(plugin, "f a", "f", "a"));
    }

    [Fact]
    public void Plugins_HaveExpectedMetadata()
    {
        Assert.Equal(["*"], new CalculatorPlugin(new FakeClipboard(), NullLogger.Instance).Metadata.ActionKeywords);
        Assert.Equal(["*"], new UrlPlugin(new FakeUrlOpener(), NullLogger.Instance).Metadata.ActionKeywords);
        Assert.Equal(["*"], new SystemPlugin(new FakeSystem(), new FakeConfirm(true), NullLogger.Instance).Metadata.ActionKeywords);
        Assert.Equal(["f"], new FilesPlugin([], new FakeFileOpener(), NullLogger.Instance).Metadata.ActionKeywords);
    }
}
