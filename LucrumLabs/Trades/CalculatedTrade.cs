using System;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data.Market;
using QuantConnect.Orders;

namespace LucrumLabs.Trades
{
    /// <summary>
    /// Trade where the prices are passed in
    /// </summary>
    public class CalculatedTrade : ManagedTrade
    {
        public CalculatedTrade(QCAlgorithm algorithm, Symbol symbol, OrderDirection direction, OrderType entryOrderType, decimal entryPrice, decimal slPrice, decimal tpPrice, int quantity) : base(algorithm, symbol, direction, entryOrderType)
        {
            _entryPrice = entryPrice;
            _slPrice = slPrice;
            _tpPrice = tpPrice;
            _quantity = quantity;
        }

        protected override void CalculatePrices()
        {
            
        }

        public override void OnDataUpdate(QuoteBar bar)
        {
            /*
            if (bar.EndTime - _entryTimeUtc >= bar.Period && _state == TradeState.PENDING)
            {
                Console.WriteLine("Expiring trade");
                Close();
            }*/
        }
    }
}