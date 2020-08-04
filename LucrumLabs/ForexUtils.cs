using System;
using QuantConnect;
using QuantConnect.Securities.Forex;

namespace LucrumLabs
{
    public static class ForexUtils
    {
        public static decimal GetPipSize(Forex pair)
        {
            var quoteSymbol = pair.QuoteCurrency.Symbol;
            if (quoteSymbol.LazyToUpper() == "JPY")
            {
                return 0.01m;
            }

            return 0.0001m;
        }
        
        public static int CalculatePositionSize(Forex pair, decimal pips, decimal balance, decimal riskPercent)
        {
            var quoteCurrency = pair.QuoteCurrency;

            int lotSize = 0;
            decimal pipSize = ForexUtils.GetPipSize(pair);

            if (quoteCurrency.ConversionRate <= 0m)
            {
                Console.WriteLine("Could not find account conversion rate when calculating position size");
                return 0;
            }
            
            // risk amount in the quote currency
            decimal riskAmount = balance * riskPercent / quoteCurrency.ConversionRate;

            decimal unitsPerCurrency = 1m / pipSize;
            var pipValue = riskAmount / pips;
            lotSize = (int)(pipValue * unitsPerCurrency);

            //Debug(string.Format("Lot size: {0}, Risk: {1:P1}, Balance: {2:C}, Price: {3}, ConversionRate: {4}", lotSize, riskPercent, balance, pair.Price, quoteCurrency.ConversionRate));
            return lotSize;
        }
    }
}