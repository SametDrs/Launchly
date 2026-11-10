using Launchly.Core.Querying;

namespace Launchly.Tests;

public class StringMatcherTests
{
    [Fact]
    public void Acronym_MatchesWordInitials()
    {
        var m = StringMatcher.Match("vsc", "Visual Studio Code");

        Assert.True(m.IsMatch);
        Assert.Equal(new[] { 0, 7, 14 }, m.Indices);
        Assert.InRange(m.Score, 500, 599);
    }

    [Fact]
    public void Acronym_IsCaseInsensitive_AndSupportsCamelCase()
    {
        Assert.True(StringMatcher.Match("VSC", "visual studio code").IsMatch);
        Assert.True(StringMatcher.Match("gc", "GoogleChrome").IsMatch);
    }

    [Fact]
    public void SequentialFuzzy_MatchesInOrder()
    {
        var m = StringMatcher.Match("chrm", "Google Chrome");

        Assert.True(m.IsMatch);
        Assert.Equal(4, m.Indices.Count);
        Assert.True(m.Indices.SequenceEqual(m.Indices.OrderBy(i => i)));
        Assert.InRange(m.Score, 1, 299);
    }

    [Fact]
    public void WrongOrder_DoesNotMatch()
    {
        Assert.False(StringMatcher.Match("rhc", "Chrome").IsMatch);
    }

    [Theory]
    [InlineData("xyz", "Visual Studio Code")]
    [InlineData("codee", "Code")]
    [InlineData("q", "Notepad")]
    public void NoMatch_IsReportedWithZeroScore(string query, string target)
    {
        var m = StringMatcher.Match(query, target);

        Assert.False(m.IsMatch);
        Assert.Equal(0, m.Score);
        Assert.Empty(m.Indices);
    }

    [Fact]
    public void NullOrEmptyTarget_DoesNotMatch()
    {
        Assert.False(StringMatcher.Match("a", null).IsMatch);
        Assert.False(StringMatcher.Match("a", "").IsMatch);
    }

    [Fact]
    public void EmptyQuery_MatchesEverythingWithZeroScore()
    {
        var m = StringMatcher.Match("  ", "Anything");

        Assert.True(m.IsMatch);
        Assert.Equal(0, m.Score);
    }

    [Fact]
    public void Ranking_ExactThenPrefixThenWordStartThenAcronymThenSubstringThenFuzzy()
    {
        var exact = StringMatcher.Match("code", "Code").Score;
        var prefix = StringMatcher.Match("code", "Code Runner").Score;
        var wordStart = StringMatcher.Match("code", "Visual Studio Code").Score;
        var acronym = StringMatcher.Match("vsc", "Visual Studio Code").Score;
        var substring = StringMatcher.Match("ode", "Visual Studio Code").Score;
        var fuzzy = StringMatcher.Match("vstdc", "Visual Studio Code").Score;

        Assert.Equal(1000, exact);
        Assert.True(exact > prefix, "exact > prefix");
        Assert.True(prefix > wordStart, "prefix > word-start substring");
        Assert.True(wordStart > acronym, "word-start substring > acronym");
        Assert.True(acronym > substring, "acronym > mid substring");
        Assert.True(substring > fuzzy, "mid substring > fuzzy");
        Assert.True(fuzzy > 0);
    }

    [Fact]
    public void ShorterTarget_ScoresHigherForSamePrefix()
    {
        var short1 = StringMatcher.Match("note", "Notepad").Score;
        var long1 = StringMatcher.Match("note", "Notepad++ Portable Edition").Score;

        Assert.True(short1 > long1);
    }

    [Fact]
    public void ConsecutiveFuzzy_BeatsScatteredFuzzy()
    {
        var tight = StringMatcher.Match("wrd", "Word Pad").Score;     // w-or-d: yakın
        var scattered = StringMatcher.Match("wrd", "Windows Recorder").Score;

        Assert.True(tight > scattered);
    }

    [Fact]
    public void MultipleTerms_AllMustMatch_AnyOrder()
    {
        Assert.True(StringMatcher.Match("code visual", "Visual Studio Code").IsMatch);
        Assert.False(StringMatcher.Match("code zzz", "Visual Studio Code").IsMatch);
    }

    [Theory]
    [InlineData("istanbul", "İstanbul")]
    [InlineData("ISTANBUL", "İstanbul")]
    [InlineData("sifre", "Şifre Yöneticisi")]
    [InlineData("ıslak", "Islak Zemin")]
    public void TurkishLetters_AreFolded(string query, string target)
    {
        Assert.True(StringMatcher.Match(query, target).IsMatch);
    }
}
