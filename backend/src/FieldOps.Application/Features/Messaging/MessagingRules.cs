using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FieldOps.Application.Features.Messaging;

public static class MessagingFilters
{
    public const string All = "all";

    public const string Unread = "unread";

    public const string Jobs = "jobs";

    public const string Team = "team";

    public static bool IsValid(string filter) => filter is All or Unread or Jobs or Team;
}

public static partial class MessagingRules
{
    public const int PreviewLength = 140;

    public const int PageSize = 30;

    public const int MessagePageSize = 50;

    public const int SearchMin = 2;

    public const int SearchMax = 80;

    /// <summary>BR-04: the body with whitespace collapsed, cut to the first 140 characters.</summary>
    public static string Preview(string body)
    {
        var collapsed = Whitespace().Replace(body, " ").Trim();

        return collapsed.Length <= PreviewLength ? collapsed : collapsed[..PreviewLength];
    }

    /// <summary>BR-05: the trimmed search (null when absent) and whether its length is valid.</summary>
    public static (string? Search, bool Valid) NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, true);
        }

        return (trimmed, trimmed.Length is >= SearchMin and <= SearchMax);
    }

    /// <summary>The list cursor binds the filter and the normalized search; a cursor of another query is invalid.</summary>
    public static string QueryHash(string filter, string? search)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{filter}\n{search?.ToLowerInvariant()}"));

        return Base64Url.EncodeToString(bytes.AsSpan(0, 9));
    }

    public static string EncodeCursor(string queryHash, ConversationListCursor cursor) =>
        Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(
                new CursorPayload(1, queryHash, cursor.LastMessageAt?.UtcTicks, cursor.SortTitle, cursor.Key)));

    /// <summary>Decodes an opaque cursor; null when malformed or bound to another filter or search.</summary>
    public static ConversationListCursor? DecodeCursor(string cursor, string queryHash)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(Base64Url.DecodeFromChars(cursor));

            if (payload is not { Version: 1, SortTitle: not null }
                || payload.QueryHash != queryHash
                || payload.Key == Guid.Empty
                || payload.LastMessageTicks is < 0 or > 3_155_378_975_999_999_999L)
            {
                return null;
            }

            return new ConversationListCursor(
                payload.LastMessageTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
                payload.SortTitle,
                payload.Key);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("fh")] string QueryHash,
        [property: JsonPropertyName("at")] long? LastMessageTicks,
        [property: JsonPropertyName("t")] string SortTitle,
        [property: JsonPropertyName("k")] Guid Key);
}
