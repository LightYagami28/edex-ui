// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Edex.Core.Updates;

public sealed record UpdateInfo(string Version, Uri Url);

public static class UpdateChecker
{
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

    public static async Task<UpdateInfo?> CheckAsync(HttpClient client, string repository, string currentVersion, CancellationToken cancel)
    {
        try
        {
            using var request = LatestReleaseRequest(repository, currentVersion);
            using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false), currentVersion)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Trace.WriteLine($"UpdateChecker: controllo aggiornamenti fallito: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static HttpRequestMessage LatestReleaseRequest(string repository, string currentVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("edex-ui", currentVersion));
        return request;
    }

    private static UpdateInfo? Parse(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString();
        return IsNewer(currentVersion, tag) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new UpdateInfo(tag.TrimStart('v', 'V'), uri)
            : null;
    }
}
