using System;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Forex;

namespace LucrumLabs
{
    public struct BarRatios
    {
        public decimal Top;
        public decimal Body;
        public decimal Bottom;
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
                Top = (high - bodyTop) / length,
                Body = (bodyTop - bodyBottom) / length,
                Bottom = (bodyBottom - low) / length
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

        public static decimal GetSpread(this QuoteBar bar)
        {
            return bar.Ask.Close - bar.Bid.Close;
        }

        public static decimal GetSpreadPips(this QuoteBar bar, Forex pair)
        {
            return bar.GetSpread() / ForexUtils.GetPipSize(pair);
        }
    }
}