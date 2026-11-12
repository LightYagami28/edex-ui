// SPDX-License-Identifier: GPL-3.0-only
using System.Net.Http.Headers;
using System.Text.Json;

namespace Edex.Core.Updates;

public sealed record UpdateInfo(string Version, string Url);

public static class UpdateChecker
{
    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/>. Unparsable versions are never newer.</summary>
    public static bool IsNewer(string current, string latest) =>
        TryParse(current, out var c) && TryParse(latest, out var l) && l > c;

    private static bool TryParse(string value, out Version version)
    {
        var text = value.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return Version.TryParse(text, out version!);
    }

    /// <summary>Reads the latest GitHub release of <paramref name="repository"/> ("owner/name"). Null when up to date or on any network or format error.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient client, string repository, string currentVersion, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("edex-ui", currentVersion));
            using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            return IsNewer(currentVersion, tag) ? new UpdateInfo(tag.TrimStart('v', 'V'), url) : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
