using System.Text.Json;

using BBDown.Core.Util;

namespace BBDown.Core.Fetcher;

/// <summary>Shared validation for Bilibili JSON API envelopes.</summary>
internal static class FetcherJson
{
    internal static void ThrowIfApiError(JsonElement root, string failureMessage)
    {
        long code = root.GetInt64Safe("code");
        if (code == 0) return;

        string message = JsonElementExtensions.SanitizeServerText(root.GetValueAsStringSafe("message"));
        throw new InvalidOperationException($"{failureMessage} (code={code}): {message}");
    }
}
