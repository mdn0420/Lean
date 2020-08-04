using System;
using NodaTime;
using QuantConnect;
using QuantConnect.Data.Consolidators;

namespace LucrumLabs
{
    public static class AlgoUtils
    {
        /// <summary>
        /// Calculates period aligned with NY session close time
        /// </summary>
        /// <param name="period"></param>
        /// <returns></returns>
        public static Func<DateTime, CalendarInfo> NewYorkClosePeriod(DateTimeZone fromTz, TimeSpan period)
        {
            return dt =>
            {
                // dt is start time of the data slice
                var nyc = dt.ConvertTo(fromTz, TimeZones.NewYork).RoundUp(TimeSpan.FromHours(1));
                int closeHour = 0; // 5pm)
                if (nyc.Hour <= closeHour)
                {
                    nyc = nyc.AddHours(closeHour - nyc.Hour);
                }
                else
                {
                    nyc = nyc.AddHours(closeHour + (24 - nyc.Hour));
                }

                // walk backwards until we find the period this time is in
                DateTime periodStart = nyc.ConvertTo(TimeZones.NewYork, fromTz);
                while (dt < periodStart)
                {
                    periodStart = periodStart.Subtract(period);
                }

                var result = new CalendarInfo(periodStart, period);
                return result;
            };
        }
    }
}