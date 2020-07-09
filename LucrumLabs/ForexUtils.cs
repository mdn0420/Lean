using QuantConnect;
using QuantConnect.Securities.Forex;

namespace LucrumLabs
{
    public static class ForexUtils
    {
        public static decimal GetPipSize(string symbol)
        {
            if (symbol.LazyToUpper() == "JPY")
            {
                return 0.01m;
            }

            return 0.0001m;
        }
    }
}