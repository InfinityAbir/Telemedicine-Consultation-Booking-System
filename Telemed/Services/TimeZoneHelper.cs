using System;

namespace Telemed.Services
{
    public static class TimeZoneHelper
    {
        // Convert a DateTime to target zone (Asia/Dhaka). 
        // If the incoming DateTime.Kind is Unspecified, we treat it as UTC (adjust if your app stores local times).
        public static DateTime ConvertToDhaka(DateTime dt)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

                if (dt.Kind == DateTimeKind.Utc)
                {
                    return TimeZoneInfo.ConvertTimeFromUtc(dt, tz);
                }

                if (dt.Kind == DateTimeKind.Local)
                {
                    return TimeZoneInfo.ConvertTime(dt, TimeZoneInfo.Local, tz);
                }

                // Unspecified: assume stored as UTC (safe if you store UTC). 
                // If your app stores local times instead, change SpecifyKind to Local.
                var assumedUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return TimeZoneInfo.ConvertTimeFromUtc(assumedUtc, tz);
            }
            catch
            {
                // If the server doesn't know "Asia/Dhaka" (rare), fall back to original value.
                return dt;
            }
        }
    }
}
