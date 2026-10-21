using Clans.Settings;
using System;

namespace Clans.Core {
    // The weekly reset that clan activities are locked to: every week at the configured weekday and UTC hour,
    // like an MMO lockout. All values are Unix seconds, UTC.
    //
    // An admin can shift the clock for testing (clans-admin week); the shift moves where the boundaries fall
    // but the values returned are still real times, so countdowns stay correct.
    internal static class WeeklyClock {
        internal const long Week = 7 * 24 * 3600;

        // Set on the server from the save file, and on clients from the server's Welcome.
        internal static long DebugOffsetSeconds;

        internal static long PeriodStart(long nowUtc) {
            long shifted = nowUtc + DebugOffsetSeconds;
            DateTime now = DateTimeOffset.FromUnixTimeSeconds(shifted).UtcDateTime;
            DayOfWeek resetDay = ClansConfig.WeeklyResetDay?.Value ?? DayOfWeek.Monday;
            int resetHour = ClansConfig.WeeklyResetHourUtc?.Value ?? 0;
            int daysSince = ((int)now.DayOfWeek - (int)resetDay + 7) % 7;
            DateTime start = now.Date.AddDays(-daysSince).AddHours(resetHour);
            if (start > now) { start = start.AddDays(-7); }
            return new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeSeconds() - DebugOffsetSeconds;
        }

        internal static long PeriodEnd(long nowUtc) => PeriodStart(nowUtc) + Week;
    }
}
