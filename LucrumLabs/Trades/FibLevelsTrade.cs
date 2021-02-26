using System;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Orders;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Trades
{
    public class FibLevelsTradeSettings
    {
        // Start and end prices to setup to calculate the fib levels off of
        public decimal StartPrice;
        public decimal EndPrice;

        // Fib levels used to calculate prices
        public decimal EntryFib;
        public decimal SlFib;
        public decimal TpFib;
        public decimal ExpireFib;

        public decimal RiskPercent;
    }
    /// <summary>
    /// Managed trade that calculates prices based on Fibonacci levels
    /// </summary>
    public class FibLevelsTrade : ManagedTrade
    {
        protected FibLevelsTradeSettings _settings;

        protected decimal _expirePrice;
        protected decimal _riskPips;
        protected decimal _tpPips;

        public FibLevelsTrade(QCAlgorithm algorithm, ManualTradeBuilder tradeBuilder, Symbol symbol, OrderDirection direction, OrderType entryOrderType, FibLevelsTradeSettings settings) : base(algorithm, symbol, direction, entryOrderType)
        {
            _settings = settings;
        }

        protected override void CalculatePrices()
        {
            Forex pair = _algorithm.Securities[_symbol] as Forex;
            
            decimal pipSize = ForexUtils.GetPipSize(pair);

            _entryPrice = MathUtils.GetRetracementPrice(_settings.StartPrice, _settings.EndPrice, _settings.EntryFib);
            
            // adjust entry price by 1 pip
            if (_direction == OrderDirection.Buy)
            {
                _entryPrice -= pipSize;
            }
            else
            {
                _entryPrice += pipSize;
            }
            
            _slPrice = MathUtils.GetRetracementPrice(_settings.StartPrice, _settings.EndPrice, _settings.SlFib);
            _tpPrice = MathUtils.GetRetracementPrice(_settings.StartPrice, _settings.EndPrice, _settings.TpFib);
            _expirePrice = MathUtils.GetRetracementPrice(_settings.StartPrice, _settings.EndPrice, _settings.ExpireFib);
            
            RoundPrice(ref _entryPrice);
            RoundPrice(ref _slPrice);
            RoundPrice(ref _tpPrice);

            _riskPips = Math.Abs(_entryPrice - _slPrice) / pipSize;
            _tpPips = Math.Abs(_entryPrice - _tpPrice) / pipSize;
            
            _quantity = ForexUtils.CalculatePositionSize(pair, _riskPips, _algorithm.Portfolio.MarginRemaining, _settings.RiskPercent);
            if (_direction == OrderDirection.Sell)
            {
                _quantity = -_quantity;
            }
            
            Console.WriteLine("{0} - Setting up trade, entry: {1}, sl: {2} ({3:F1}), tp: {4} ({5:F1}), units: {6:N0} {7}", _algorithm.Time, _entryPrice, _slPrice, _riskPips, _tpPrice, _tpPips, _quantity, TradeName);
        }
    }
}