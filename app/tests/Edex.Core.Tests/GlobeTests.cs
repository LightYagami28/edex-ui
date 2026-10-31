// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using Edex.Core.Geo;

namespace Edex.Core.Tests;

public class GlobeTests
{
    [Theory]
    [InlineData(41.9, 12.5, true)]     // Rome
    [InlineData(40.7, -74.5, true)]    // New Jersey
    [InlineData(-25, 134, true)]       // Australia
    [InlineData(-82, 0, true)]         // Antarctica
    [InlineData(0, -140, false)]       // Pacific
    [InlineData(30, -40, false)]       // Atlantic
    [InlineData(-30, 80, false)]       // Indian Ocean
    public void LandMaskMatchesGeography(double lat, double lon, bool land) =>
        Assert.Equal(land, LandMask.Default.IsLand(lat, lon));

    [Fact]
    public void LandPointsCoverAboutAThirdOfTheGlobe()
    {
        var points = LandMask.Default.LandPoints(3);
        Assert.InRange(points.Count, 800, 3000);
        Assert.Equal(LandMask.Default.IsLand(0, 190), LandMask.Default.IsLand(0, -170)); // longitude wraps
    }

    [Fact]
    public void OrthographicProjection()
    {
        var view = new Orthographic(0, 0);
        var (x, y, visible) = view.Project(0, 0);
        Assert.True(visible);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
        (x, _, visible) = view.Project(0, 90);
        Assert.True(visible);
        Assert.Equal(1, x, 6);
        (_, y, _) = view.Project(90, 0);
        Assert.Equal(-1, y, 6); // north is up
        Assert.False(view.Project(0, 180).Visible);
        Assert.True(new Orthographic(45, 10).Project(45, 10) is { Visible: true, X: < 1e-9 and > -1e-9 });
    }

    [Fact]
    public void ParsesKeylessGeolocationResponses()
    {
        var who = GeoLocator.ParseIpWhoIs(JsonDocument.Parse("""{"ip":"203.0.113.9","success":true,"latitude":45.07,"longitude":7.69,"city":"Turin","country":"Italy"}""").RootElement);
        Assert.Equal(new GeoLocation(45.07, 7.69, "Turin", "Italy", "203.0.113.9", "ipwho.is"), who);
        Assert.Null(GeoLocator.ParseIpWhoIs(JsonDocument.Parse("""{"success":false,"message":"reserved range"}""").RootElement));
        var co = GeoLocator.ParseIpApiCo(JsonDocument.Parse("""{"ip":"203.0.113.9","city":"Milan","country_name":"Italy","latitude":45.46,"longitude":9.19}""").RootElement);
        Assert.Equal("Milan", co?.City);
        Assert.Null(GeoLocator.ParseIpApiCo(JsonDocument.Parse("""{"error":true,"reason":"RateLimited"}""").RootElement));
        Assert.Null(GeoLocator.ParseIpApiCo(JsonDocument.Parse("""{"latitude":"x","longitude":1}""").RootElement));
    }
}
