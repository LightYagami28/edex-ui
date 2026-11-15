// SPDX-License-Identifier: GPL-3.0-only
using System.Net.Http.Json;
using System.Text.Json;

namespace Edex.Core.Geo;

public sealed class LandMask
{
    private const int Columns = 360;
    private const int Rows = 180;
    private const double MaxLatitude = 90;
    private const double MaxLongitude = 180;
    private const double FullTurn = 360;
    private const int BitsPerByte = 8;
    private const int ByteIndexShift = 3;
    private const int BitIndexMask = 7;
    private const double MinLongitudeScale = 0.2;
    private const double DegreesToRadians = Math.PI / 180;

    private readonly byte[] _bits;

    public static int Width => Columns;
    public static int Height => Rows;

    private LandMask(byte[] bits) => _bits = bits;

    private static readonly Lazy<LandMask> s_default = new(() =>
    {
        using var stream = typeof(LandMask).Assembly.GetManifestResourceStream("Edex.Core.Geo.land-1deg.bin")
            ?? throw new InvalidOperationException("land mask resource missing");
        var bits = new byte[Columns * Rows / BitsPerByte];
        stream.ReadExactly(bits);
        return new LandMask(bits);
    });

    public static LandMask Default => s_default.Value;

    public bool IsLand(double latitude, double longitude)
    {
        int row = Math.Clamp((int)Math.Floor(MaxLatitude - latitude), 0, Rows - 1);
        double lon = ((longitude + MaxLongitude) % FullTurn + FullTurn) % FullTurn;
        int col = Math.Clamp((int)Math.Floor(lon), 0, Columns - 1);
        int i = row * Columns + col;
        int bit = _bits[i >> ByteIndexShift] >> (i & BitIndexMask);
        return (bit & 1) == 1;
    }

    public IReadOnlyList<(double Latitude, double Longitude)> LandPoints(double step)
    {
        var points = new List<(double, double)>();
        double half = step / 2;
        for (double lat = MaxLatitude - half; lat > -MaxLatitude; lat -= step)
        {
            AddRow(points, lat, step / Math.Max(MinLongitudeScale, Math.Cos(lat * DegreesToRadians)));
        }
        return points;
    }

    private void AddRow(List<(double, double)> points, double lat, double lonStep)
    {
        double start = lonStep / 2 - MaxLongitude;
        for (double lon = start; lon < MaxLongitude; lon += lonStep)
        {
            if (IsLand(lat, lon))
            {
                points.Add((lat, lon));
            }
        }
    }
}

public readonly record struct Orthographic(double CenterLatitude, double CenterLongitude)
{
    public (double X, double Y, bool Visible) Project(double latitude, double longitude)
    {
        double phi = latitude * Math.PI / 180;
        double lambda = (longitude - CenterLongitude) * Math.PI / 180;
        double phi0 = CenterLatitude * Math.PI / 180;
        double cosc = Math.Sin(phi0) * Math.Sin(phi) + Math.Cos(phi0) * Math.Cos(phi) * Math.Cos(lambda);
        double x = Math.Cos(phi) * Math.Sin(lambda);
        double y = Math.Cos(phi0) * Math.Sin(phi) - Math.Sin(phi0) * Math.Cos(phi) * Math.Cos(lambda);
        return (x, -y, cosc >= 0);
    }
}

public sealed record GeoLocation(double Latitude, double Longitude, string? City, string? Country, string? Ip, string Source);

public static class GeoLocator
{
    private const double MaxLatitude = 90;
    private const double MaxLongitude = 180;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private static readonly (string Url, Func<JsonElement, GeoLocation?> Parse)[] Providers =
    [
        ("https://ipwho.is/", ParseIpWhoIs),
        ("https://ipapi.co/json/", ParseIpApiCo),
    ];

    public static async Task<GeoLocation?> LocateAsync(HttpClient http, CancellationToken token)
    {
        foreach (var (url, parse) in Providers)
        {
            var location = await TryLocateAsync(http, url, parse, token).ConfigureAwait(false);
            if (location is not null || token.IsCancellationRequested)
            {
                return location;
            }
        }
        return null;
    }

    private static async Task<GeoLocation?> TryLocateAsync(HttpClient http, string url, Func<JsonElement, GeoLocation?> parse, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(RequestTimeout);
            return parse(await http.GetFromJsonAsync<JsonElement>(url, cts.Token).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static GeoLocation? ParseIpWhoIs(JsonElement json)
    {
        if (json.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        return Build(json, "country", "ipwho.is");
    }

    public static GeoLocation? ParseIpApiCo(JsonElement json)
    {
        if (json.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return Build(json, "country_name", "ipapi.co");
    }

    private static GeoLocation? Build(JsonElement json, string country, string source)
    {
        if (Number(json, "latitude") is not { } latitude || Number(json, "longitude") is not { } longitude || !IsOnEarth(latitude, longitude))
        {
            return null;
        }

        return new GeoLocation(latitude, longitude, Text(json, "city"), Text(json, country), Text(json, "ip"), source);
    }

    private static bool IsOnEarth(double latitude, double longitude) =>
        Math.Abs(latitude) <= MaxLatitude && Math.Abs(longitude) <= MaxLongitude;

    private static double? Number(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
