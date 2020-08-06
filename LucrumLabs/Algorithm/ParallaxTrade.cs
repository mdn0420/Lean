using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using LucrumLabs.Trades;
using NodaTime;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Forex;
using QuantConnect.Statistics;

namespace LucrumLabs.Algorithm
{
    public class ParallaxTrade : ManagedTrade
    {
        /// <summary>
        /// If price hits this extension level before entry is filled, cancel the trade
        /// </summary>
        private const decimal ExpireFibLevel = -0.382m;

        private bool _activeTradeManagementEnabled;


        private int _slLlevel = -1;

        private QuoteBar _setupBar;

        private decimal _entryFib;
        private decimal _slFib;
        private decimal _tpFib;
        
        private decimal _riskPercent;
        private decimal _riskPips;
        private decimal _tpPips;

        private decimal _extensionPrice;

        // For internal tracking purposes
        protected override string TradeName => _tradeName;
        private string _tradeName;

        // True if price hit extension before entering
        private bool _expired;

        public ParallaxTrade(ParallaxAlgorithm algorithm, 
            QuoteBar setupBar, 
            OrderDirection direction, 
            decimal entryFib, 
            decimal slFib, 
            decimal tpFib, 
            decimal riskPercent,
            bool activeManagement,
            string tag="") : base(algorithm, setupBar.Symbol, direction, OrderType.Limit)
        {
            _setupBar = setupBar;
            _riskPercent = riskPercent;
            _entryFib = entryFib;
            _slFib = slFib;
            _tpFib = tpFib;
            _activeTradeManagementEnabled = activeManagement;
            
            if (string.IsNullOrEmpty(tag))
            {
                _tradeName = _symbol;
            }
            else
            {
                _tradeName = string.Format("{0}:{1}", _symbol, tag);
            }
        }

        protected override void CalculatePrices()
        {
            Forex pair = _algorithm.Securities[_symbol] as Forex;

            
            
            decimal pipSize = ForexUtils.GetPipSize(pair);

            _entryPrice = GetFibPrice(_entryFib);
            
            // adjust entry price by 1 pip
            if (_direction == OrderDirection.Buy)
            {
                _entryPrice -= pipSize;
            }
            else
            {
                _entryPrice += pipSize;
            }
            
            _slPrice = GetFibPrice(_slFib);
            _tpPrice = GetFibPrice(_tpFib);
            _extensionPrice = GetFibPrice(ExpireFibLevel);
            
            RoundPrice(ref _entryPrice);
            RoundPrice(ref _slPrice);
            RoundPrice(ref _tpPrice);

            _riskPips = Math.Abs(_entryPrice - _slPrice) / pipSize;
            _tpPips = Math.Abs(_entryPrice - _tpPrice) / pipSize;
            
            _quantity = ForexUtils.CalculatePositionSize(pair, _riskPips, _algorithm.Portfolio.MarginRemaining, _riskPercent);
            if (_direction == OrderDirection.Sell)
            {
                _quantity = -_quantity;
            }
            
            Console.WriteLine("{0} - Setting up trade, entry: {1}, sl: {2} ({3:F1}), tp: {4} ({5:F1}), units: {6:N0} {7}", _algorithm.Time, _entryPrice, _slPrice, _riskPips, _tpPrice, _tpPips, _quantity, _tradeName);
        }

        /// <summary>
        /// Returns true if we're interested in a particular order id
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public bool HasOrderId(int orderId)
        {
            if (_entryOrder != null && _entryOrder.OrderId == orderId) return true;
            if (_slOrder != null && _slOrder.OrderId == orderId) return true;
            if (_tpOrder != null && _tpOrder.OrderId == orderId) return true;

            return false;
        }

        private void UpdateProfitLoss()
        {
            _trade = _algorithm.TradeBuilder.ClosedTrades.Last();
            if (_trade != null)
            {
                Forex pair = _algorithm.Securities[_symbol] as Forex;
                var pipSize = ForexUtils.GetPipSize(pair);
                if (_direction == OrderDirection.Buy)
                {
                    _profitLossPips = (_trade.ExitPrice - _trade.EntryPrice) / pipSize;
                }
                else
                {
                    _profitLossPips = (_trade.EntryPrice - _trade.ExitPrice) / pipSize;
                }
            }
        }

        /// <summary>
        /// Helper method to calculate fib price based on direction
        /// </summary>
        /// <param name="fibValue"></param>
        /// <returns></returns>
        private decimal GetFibPrice(decimal fibValue)
        {
            if (_direction == OrderDirection.Buy)
            {
                return MathUtils.GetFibPrice(_setupBar.Low, _setupBar.High, fibValue);
            }
            
            return MathUtils.GetFibPrice(_setupBar.High, _setupBar.Low, fibValue);
        }

        public override void OnDataUpdate(QuoteBar bar)
        {
            if (bar.Symbol != _symbol)
            {
                _algorithm.Error(string.Format("Received {0} bar data for {1} trade", bar.Symbol, _tradeName));
                return;
            }
            
            if (_state == TradeState.PENDING)
            {
                // Cancel order if we haven't filled and we already hit the first extension level
                var price = bar.Price;
                bool cancelFromExtension = (_direction == OrderDirection.Buy && price > _extensionPrice) || 
                                           (_direction == OrderDirection.Sell && price < _extensionPrice);
                if (cancelFromExtension)
                {
                    _expired = true;
                    Console.WriteLine("{0} - Price ran to extension before entry order filled.. cancelling {1} trade.", _algorithm.Time, _tradeName);
                    Close();
                }
            }
            else if (_state == TradeState.OPEN)
            {
                if (_activeTradeManagementEnabled)
                {
                    var price = bar.Price;
                    if (_slLlevel < 0)
                    {
                        // -38.2
                        var fibPrice = GetFibPrice(ParallaxAlgorithm.FibExtensionLevels[1]);
                        if (_direction == OrderDirection.Buy && price > fibPrice ||
                            _direction == OrderDirection.Sell && price < fibPrice)
                        {
                            MoveUpStopLoss();
                        }
                    }
                    else if (_slLlevel == 0)
                    {
                        // -61.8
                        var fibPrice = GetFibPrice(ParallaxAlgorithm.FibExtensionLevels[2]);
                        if (_direction == OrderDirection.Buy && price > fibPrice ||
                            _direction == OrderDirection.Sell && price < fibPrice)
                        {
                            MoveUpStopLoss();
                        }
                    }
                    else if (_slLlevel == 1)
                    {
                        // -1
                        var fibPrice = GetFibPrice(ParallaxAlgorithm.FibExtensionLevels[3]);
                        if (_direction == OrderDirection.Buy && price > fibPrice ||
                            _direction == OrderDirection.Sell && price < fibPrice)
                        {
                            MoveUpStopLoss();
                        }
                    }
                    else if (_slLlevel == 2)
                    {
                        // -1
                        var fibPrice = GetFibPrice(ParallaxAlgorithm.FibExtensionLevels[4]);
                        if (_direction == OrderDirection.Buy && price > fibPrice ||
                            _direction == OrderDirection.Sell && price < fibPrice)
                        {
                            MoveUpStopLoss();
                        }
                    }
                }
            }
        }

        private void MoveUpStopLoss()
        {
            _slLlevel++;
            decimal newStopPrice = -1m;
            if (_slLlevel == 0)
            {
                // move to break even
                // todo: may need to adjust price since LEAN is counting these as losses
                newStopPrice = _entryOrder.AverageFillPrice;
            }
            else if (_slLlevel > 0 && _slLlevel < ParallaxAlgorithm.FibExtensionLevels.Length)
            {
                newStopPrice = GetFibPrice(ParallaxAlgorithm.FibExtensionLevels[_slLlevel]);
            }
            else
            {
                _algorithm.Error("Unexpected level to move stop loss to");
            }

            if (newStopPrice > 0m)
            {
                RoundPrice(ref newStopPrice);
                Console.WriteLine("{0} - Moving stop loss on {1} to {2}", _algorithm.Time, _tradeName, newStopPrice);
                _slOrder.UpdateStopPrice(newStopPrice);
            }
        }

        public TradeSetupData GetStats()
        {
            var fillPrice = 0m;
            if (_entryOrder.QuantityFilled != 0)
            {
                fillPrice = _entryOrder.AverageFillPrice;
            }
            TradeSetupData result = new TradeSetupData()
            {
                BarTime = _setupBar.Time.ConvertToUtc(_algorithm.TimeZone),
                direction = _direction.ToString(),
                entryPrice = _entryPrice,
                entryTime = _entryTimeUtc,
                closeTime = _closeTimeUtc,
                fillPrice = fillPrice,
                slPrice = _slPrice,
                tpPrice = _tpPrice,
                slPips = _riskPips,
                tpPips = _tpPips,
                plPips = _profitLossPips,
                symbol = _symbol,
                canceled = _expired,
                tradeIndex = _algorithm.TradeBuilder.ClosedTrades.IndexOf(_trade)
            };
            return result;
        }
    }
}