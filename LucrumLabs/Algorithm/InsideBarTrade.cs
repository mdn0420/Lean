using System;
using System.Diagnostics;
using System.Linq;
using LucrumLabs.Trades;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Algorithm
{
    public class InsideBarTrade : ManagedTrade
    {
        private QuoteBar _bar1;
        private QuoteBar _bar2;

        private decimal EntryRange = 0.1m;
        private decimal StopLossRange = 0.4m;
        private decimal TakeProfitRange = 0.8m;

        
        public InsideBarTrade(InsideBarAlgorithm algorithm, QuoteBar bar1, QuoteBar bar2, OrderDirection direction) : base(algorithm, bar1.Symbol, direction, OrderType.StopMarket)
        {
            _bar1 = bar1;
            _bar2 = bar2;
        }

        protected override void CalculatePrices()
        {
            Forex pair = _algorithm.Securities[_symbol] as Forex;
            
            var range = _bar1.High - _bar1.Low;
            if (_direction == OrderDirection.Buy)
            {
                _entryPrice = _bar1.High + (range * EntryRange);
                _slPrice = _bar1.Low - (range * StopLossRange);
                _tpPrice = _bar1.High + (range * TakeProfitRange);
            }
            else
            {
                _entryPrice = _bar1.Low - (range * EntryRange);
                _slPrice = _bar1.High + (range * StopLossRange);
                _tpPrice = _bar1.Low - (range * TakeProfitRange);
            }
            
            RoundPrice(ref _entryPrice);
            RoundPrice(ref _slPrice);
            RoundPrice(ref _tpPrice);
            
            decimal pipSize = ForexUtils.GetPipSize(pair);
            var riskPips = Math.Abs(_entryPrice - _slPrice) / pipSize;
            
            _quantity = ForexUtils.CalculatePositionSize(pair, riskPips, _algorithm.Portfolio.MarginRemaining, 0.01m);
            if (_direction == OrderDirection.Sell)
            {
                _quantity = -_quantity;
            }
        }

        private decimal _profitLossPips;
        private void UpdateProfitLoss()
        {
            var trade = _algorithm.TradeBuilder.ClosedTrades.Last();
            if (trade != null)
            {
                Forex pair = _algorithm.Securities[_symbol] as Forex;
                var pipSize = ForexUtils.GetPipSize(pair);
                if (_direction == OrderDirection.Buy)
                {
                    _profitLossPips = (trade.ExitPrice - trade.EntryPrice) / pipSize;
                }
                else
                {
                    _profitLossPips = (trade.EntryPrice - trade.ExitPrice) / pipSize;
                }
            }
        }
    }
}