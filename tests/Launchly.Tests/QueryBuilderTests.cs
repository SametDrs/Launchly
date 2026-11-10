using Launchly.Core.Querying;

namespace Launchly.Tests;

public class QueryBuilderTests
{
    private static readonly string[] Keywords = ["g", "yt", "calc"];

    [Fact]
    public void Keyword_IsSeparated_RestIsSearch()
    {
        var q = QueryBuilder.Build("yt  the beatles ", Keywords);

        Assert.Equal("yt", q.ActionKeyword);
        Assert.Equal("the beatles", q.Search);
        Assert.Equal(new[] { "the", "beatles" }, q.Terms);
        Assert.Equal("yt  the beatles ", q.Raw);
    }

    [Fact]
    public void Keyword_IsCaseInsensitive_AndKeepsTypedSpelling()
    {
        var q = QueryBuilder.Build("YT beatles", Keywords);

        Assert.Equal("YT", q.ActionKeyword);
        Assert.Equal("beatles", q.Search);
    }

    [Fact]
    public void KeywordFollowedBySpace_HasEmptySearch()
    {
        var q = QueryBuilder.Build("calc ", Keywords);

        Assert.Equal("calc", q.ActionKeyword);
        Assert.Equal(string.Empty, q.Search);
        Assert.Empty(q.Terms);
    }

    [Theory]
    [InlineData("g")]
    [InlineData("calc")]
    [InlineData("  yt")]
    public void KeywordAloneWithoutSpace_IsAGlobalQuery(string raw)
    {
        var q = QueryBuilder.Build(raw, Keywords);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal(raw.Trim(), q.Search);
    }

    [Fact]
    public void KeywordWithSpaceThenText_IsFilesStyleQuery()
    {
        var q = QueryBuilder.Build("g rapor", Keywords);

        Assert.Equal("g", q.ActionKeyword);
        Assert.Equal("rapor", q.Search);
    }

    [Fact]
    public void NoKeyword_WholeTextIsSearch_ForGlobalPlugins()
    {
        var q = QueryBuilder.Build("chrome browser", Keywords);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal("chrome browser", q.Search);
        Assert.Equal(new[] { "chrome", "browser" }, q.Terms);
    }

    [Fact]
    public void UnknownKeyword_IsNotSeparated()
    {
        var q = QueryBuilder.Build("zz something", Keywords);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal("zz something", q.Search);
    }

    [Fact]
    public void KeywordPrefixOfFirstWord_IsNotAKeyword()
    {
        // "gmail" 'g' ile başlar ama ilk kelime "gmail"; "g" anahtar kelimesi değil.
        var q = QueryBuilder.Build("gmail inbox", Keywords);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal("gmail inbox", q.Search);
    }

    [Fact]
    public void GlobalMarker_IsNeverTreatedAsKeyword()
    {
        var q = QueryBuilder.Build("* foo", ["*", "g"]);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal("* foo", q.Search);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_GivesEmptyQuery(string? raw)
    {
        var q = QueryBuilder.Build(raw, Keywords);

        Assert.Equal(string.Empty, q.ActionKeyword);
        Assert.Equal(string.Empty, q.Search);
        Assert.Empty(q.Terms);
    }
}
