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
    }
}