using System;

namespace Filee.App.Services;

/// <summary>
/// NOAA Solar Calculator for sunrise, solar noon, and sunset.
/// Operates purely mathematically with zero network requests or location permissions.
/// </summary>
public static class SunCalc
{
    public static (TimeOnly Sunrise, TimeOnly SolarNoon, TimeOnly Sunset) Calculate(DateTime date, double latitude, double longitude, TimeSpan? utcOffset = null)
    {
        int dayOfYear = date.DayOfYear;
        double rad = Math.PI / 180.0;
        double deg = 180.0 / Math.PI;

        // Fractional year in radians
        double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1 + (date.Hour - 12) / 24.0);

        // Equation of time in minutes
        double eqtime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));

        // Solar declination in radians
        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        // Hour angle for sunrise/sunset (solar zenith = 90.833° for atmospheric refraction)
        double latRad = latitude * rad;
        double zenithRad = 90.833 * rad;
        double cosHa = (Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(decl))) - (Math.Tan(latRad) * Math.Tan(decl));

        // Clamped in case of polar day / polar night
        cosHa = Math.Clamp(cosHa, -1.0, 1.0);
        double ha = Math.Acos(cosHa) * deg;

        // UTC offset
        var offset = (utcOffset ?? TimeZoneInfo.Local.GetUtcOffset(date)).TotalMinutes;

        // Solar noon, sunrise, sunset in local minutes from midnight
        double noonMinutes = 720 - 4 * longitude - eqtime + offset;
        double riseMinutes = noonMinutes - ha * 4;
        double setMinutes = noonMinutes + ha * 4;

        return (ToTimeOnly(riseMinutes), ToTimeOnly(noonMinutes), ToTimeOnly(setMinutes));
    }

    private static TimeOnly ToTimeOnly(double minutes)
    {
        double normalized = (minutes % 1440 + 1440) % 1440;
        int h = (int)(normalized / 60);
        int m = (int)(normalized % 60);
        int s = (int)((normalized - Math.Floor(normalized)) * 60);
        return new TimeOnly(h, m, s);
    }
}

