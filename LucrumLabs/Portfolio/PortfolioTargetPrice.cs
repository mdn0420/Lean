using QuantConnect;
using QuantConnect.Algorithm.Framework.Portfolio;

namespace LucrumLabs.Portfolio
{
    public class PortfolioTargetPrice : IPortfolioTargetPrice
    {
        public Symbol Symbol { get; }
        public decimal Quantity { get; }
        public decimal EntryPrice { get; }
        public decimal StopLossPrice { get; }
        public decimal TakeProfitPrice { get; }

        public PortfolioTargetPrice(Symbol symbol, decimal qty, decimal entryPrice, decimal slPrice, decimal tpPrice)
        {
            Symbol = symbol;
            Quantity = qty;
            EntryPrice = entryPrice;
            StopLossPrice = slPrice;
            TakeProfitPrice = tpPrice;
        }
    }
}