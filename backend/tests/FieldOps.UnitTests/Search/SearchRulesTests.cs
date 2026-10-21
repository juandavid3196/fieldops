using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Search;

namespace FieldOps.UnitTests.Search;

/// <summary>Query normalization, number parsing and LIKE escaping (global-search BR-05, BR-06; AC-03, AC-11, AC-12).</summary>
public class SearchRulesTests
{
    // BR-05: trimmed length 2 to 100 and an integer limit from 1 to 20 (default 20); the error messages never echo the query.
    [Theory]
    [InlineData("  ab  ", null, true, "ab", 20, null)]
    [InlineData("a b", "1", true, "a b", 1, null)]
    [InlineData("ab", "20", true, "ab", 20, null)]
    [InlineData(" a ", null, false, null, 0, "q")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", null, false, null, 0, "q")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", null, true, "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", 20, null)]
    [InlineData(null, null, false, null, 0, "q")]
    [InlineData("ab", "0", false, null, 0, "limit")]
    [InlineData("ab", "21", false, null, 0, "limit")]
    [InlineData("ab", "five", false, null, 0, "limit")]
    [InlineData("ab", "-1", false, null, 0, "limit")]
    public void Parse_QueryAndLimit_ValidatesAndNormalizes(
        string? query, string? limit, bool valid, string? expectedQuery, int expectedLimit, string? errorKey)
    {
        var outcome = SearchRules.Parse(query, limit);

        if (valid)
        {
            var request = Assert.IsType<BillingOutcome<SearchRequest>.Succeeded>(outcome).Value;
            Assert.Equal((expectedQuery, expectedLimit), (request.Query, request.Limit));
        }
        else
        {
            var errors = Assert.IsType<BillingOutcome<SearchRequest>.Invalid>(outcome).Errors;
            Assert.Equal([errorKey], errors.Keys);
        }
    }

    // BR-06: digits, #digits, prefixdigits and prefix-digits (any case) are numbers; anything else is text only.
    [Theory]
    [InlineData("1042", "INV", "1042")]
    [InlineData("#1042", "INV", "1042")]
    [InlineData("INV-1042", "INV", "1042")]
    [InlineData("inv1042", "INV", "1042")]
    [InlineData("007", "INV", "007")]
    [InlineData("WO-1042", "INV", null)]
    [InlineData("INV-", "INV", null)]
    [InlineData("INV-10x", "INV", null)]
    [InlineData("#", "INV", null)]
    [InlineData("10 42", "INV", null)]
    [InlineData("1234567890123456789", "INV", null)]
    public void ParseNumber_OrganizationPrefixHashAndDigits_ReturnsTheDigitsOrNull(string query, string prefix, string? expected) =>
        Assert.Equal(expected, SearchRules.ParseNumber(query, prefix));

    // BR-06: percent, underscore and the escape character match literally.
    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("plain", "plain")]
    public void EscapeLike_WildcardsAndEscapeCharacter_AreEscaped(string text, string expected) =>
        Assert.Equal(expected, SearchRules.EscapeLike(text));
}
