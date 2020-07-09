using System;
using QuantConnect.Data.Market;

namespace LucrumLabs
{
    public struct BarRatios
    {
        public float Top;
        public float Body;
        public float Bottom;
    }
    
    public static class BarUtils
    {
        public static BarRatios GetBarRatios(this QuoteBar bar)
        {
            var high = bar.High;
            var low = bar.Low;
            var open = bar.Open;
            var close = bar.Close;
            
            decimal length = high - low;

            if (length <= 0m)
            {
                return new BarRatios();
            }
            
            decimal bodyTop = Math.Max(open, close);
            decimal bodyBottom = Math.Min(open, close);

            var ratios = new BarRatios()
            {
                Top = (float)((high - bodyTop) / length),
                Body = (float)((bodyTop - bodyBottom) / length),
                Bottom = (float)((bodyBottom - low) / length)
            };
            return ratios;
        }

        public static decimal GetBodyTop(this QuoteBar bar)
        {
            return Math.Max(bar.Open, bar.Close);
        }
        
        public static decimal GetBodyBottom(this QuoteBar bar)
        {
            return Math.Min(bar.Open, bar.Close);
        }
    }
}