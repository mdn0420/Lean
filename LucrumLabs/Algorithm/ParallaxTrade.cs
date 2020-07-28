using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Forex;
using QuantConnect.Statistics;

namespace LucrumLabs.Algorithm
{
    public class ParallaxTrade
    {
        public enum TradeState
        {
            PENDING,
            OPEN,
            CLOSED
        }

        /// <summary>
        /// If price hits this extension level before entry is filled, cancel the trade
        /// </summary>
        private const decimal ExpireFibLevel = -0.382m;

        private bool activeTradeManagementEnabled = true;

        public TradeState State => _state;
        private TradeState _state = TradeState.PENDING;
        
        private ParallaxAlgorithm _algorithm;

        private decimal _entryPrice;
        private decimal _slPrice;
        private decimal _tpPrice;
        
        private OrderTicket _entryOrder;
        private OrderTicket _tpOrder;
        private OrderTicket _slOrder;

        private int _slLlevel = -1;

        private QuoteBar _setupBar;

        private OrderDirection _direction;

        private decimal _riskPercent;
        private decimal _riskPips;
        private decimal _tpPips;
        private decimal _profitLossPips;
        
        public bool TradeEntered => _entryOrder != null && _entryOrder.QuantityFilled != 0;

        private decimal _extensionPrice;

        private Symbol _symbol;
        private int _lotSize;

        // LEAN Trade object associated with this
        private Trade _trade;

        // For internal tracking purposes
        private string _tradeName;

        public ParallaxTrade(ParallaxAlgorithm algorithm, 
            QuoteBar setupBar, 
            OrderDirection direction, 
            decimal entryFib, 
            decimal slFib, 
            decimal tpFib, 
            decimal riskPercent,
            string tag="")
        {
            _algorithm = algorithm;
            _setupBar = setupBar;
            _symbol = setupBar.Symbol;
            _direction = direction;
            _riskPercent = riskPercent;
            if (string.IsNullOrEmpty(tag))
            {
                _tradeName = _symbol;
            }
            else
            {
                _tradeName = string.Format("{0}:{1}", _symbol, tag);
            }

            CalculatePrices(entryFib, slFib, tpFib);
        }

        /// <summary>
        /// Calculate the order prices
        /// </summary>
        private void CalculatePrices(decimal entryFib, decimal slFib, decimal tpFib)
        {
            Forex pair = _algorithm.Securities[_symbol] as Forex;

            var spread = _setupBar.GetSpread();
            decimal pipSize = ForexUtils.GetPipSize(pair);

            _entryPrice = GetFibPrice(entryFib);
            
            if (_direction == OrderDirection.Buy)
            {
                _entryPrice -= spread * 2m;
            }
            else
            {
                _entryPrice += spread * 2m;
            }
            
            _slPrice = GetFibPrice(slFib);
            _tpPrice = GetFibPrice(tpFib);
            _extensionPrice = GetFibPrice(ExpireFibLevel);
            
            RoundPrice(ref _entryPrice);
            RoundPrice(ref _slPrice);
            RoundPrice(ref _tpPrice);

            _riskPips = Math.Abs(_entryPrice - _slPrice) / pipSize;
            _tpPips = Math.Abs(_entryPrice - _tpPrice) / pipSize;
            
            _lotSize = _algorithm.CalculatePositionSize(pair, _riskPips, _algorithm.Portfolio.MarginRemaining, _riskPercent);
            if (_direction == OrderDirection.Sell)
            {
                _lotSize = -_lotSize;
            }
            
            Console.WriteLine("{0} - Setting up trade, entry: {1}, sl: {2} ({3:F1}), tp: {4} ({5:F1}), units: {6:N0} {7}:{8}", _algorithm.Time, _entryPrice, _slPrice, _riskPips, _tpPrice, _tpPips, _lotSize, _symbol, _tradeName);
        }

        public void PlaceOrders()
        {
            if (_entryOrder != null)
            {
                _algorithm.Error("Tried to place duplicate trade");
                return;
            }

            if (_lotSize == 0)
            {
                _algorithm.Error("Calculated lot size of 0.. cancelling trade");
                CloseTrade();
                return;
            }
            
            //_algorithm.Debug(string.Format("{0} - Placing limit entry order for {1:N0} {2} at {3}", _algorithm.Time, _lotSize, _symbol, _entryPrice));
            _entryOrder = _algorithm.LimitOrder(_symbol, _lotSize, _entryPrice);
            if (_entryOrder.Status == OrderStatus.Invalid)
            {
                // something wrong
                _algorithm.Error("Something was wrong with entry order.. cancelling trade");
                CloseTrade();
            }
        }

        private void PlaceManagementOrders()
        {
            //_algorithm.Debug(string.Format("Opening orders - SL: {0}, TP: {1}", slPrice, tpPrice));
            _tpOrder = _algorithm.LimitOrder(_symbol, -_lotSize, _tpPrice, string.Format("tp:{0}", _entryOrder.OrderId));
            _slOrder = _algorithm.StopMarketOrder(_symbol, -_lotSize, _slPrice, string.Format("sl:{0}", _entryOrder.OrderId));
        }

        private void RoundPrice(ref decimal value)
        {
            var security = _algorithm.Securities[_symbol];
            
            var increment = security.PriceVariationModel.GetMinimumPriceVariation(
                new GetMinimumPriceVariationParameters(security, value));
            if (increment > 0)
            {
                value = Math.Round(value / increment) * increment;
            }
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

        public void OnOrderEvent(OrderEvent orderEvent)
        {
            if (_entryOrder != null && _entryOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    // Setup TP/SL orders
                    Console.WriteLine("{0} - Entered {1} trade for {2:N0} {3} @ {4}", _algorithm.Time, _direction, orderEvent.Quantity, _tradeName, orderEvent.FillPrice);
                    _state = TradeState.OPEN;
                    //_algorithm.PrintBalance();
                    PlaceManagementOrders();
                } 
                else if (orderEvent.Status == OrderStatus.Canceled)
                {
                    Console.WriteLine("{0} - Trade for {1} cancelled", _algorithm.Time, _tradeName);
                    CloseTrade();
                }
                else if (orderEvent.Status == OrderStatus.Invalid)
                {
                    Console.WriteLine("Entry order invalid");
                    CloseTrade();
                }
            }
            else if (_slOrder != null && _slOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    UpdateProfitLoss();
                    Console.WriteLine(
                        "{0} - Stop loss hit for {1} at {2} - P/L: {3}",
                        _algorithm.Time,
                        _tradeName,
                        orderEvent.FillPrice,
                        _profitLossPips
                    );
                    CloseTrade();
                }
            }
            else if (_tpOrder != null && _tpOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    UpdateProfitLoss();
                    Console.WriteLine(
                            "{0} - Take profit hit for {1} at {2} - P/L: {3}",
                            _algorithm.Time,
                            _tradeName,
                            orderEvent.FillPrice,
                            _profitLossPips
                    );
                    CloseTrade();
                }
            }
            else
            {
                _algorithm.Error(string.Format("ParallaxTrade received unexpected order event {0}", orderEvent));
            }
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

        public void OnDataUpdate(QuoteBar bar)
        {
            if (bar.Symbol != _symbol)
            {
                _algorithm.Error(string.Format("Received {0} bar data for {1} trade", bar.Symbol, _tradeName));
                return;
            }
            
            if (_state != TradeState.OPEN)
            {
                // Cancel order if we haven't filled and we already hit the first extension level
                var price = bar.Price;
                bool cancelFromExtension = (_direction == OrderDirection.Buy && price > _extensionPrice) || 
                                           (_direction == OrderDirection.Sell && price < _extensionPrice);
                if (cancelFromExtension)
                {
                    Console.WriteLine("{0} - Price ran to extension before entry order filled.. cancelling {1} trade.", _algorithm.Time, _tradeName);
                    CloseTrade();
                }
            }
            else if (_state == TradeState.OPEN)
            {
                if (activeTradeManagementEnabled)
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

        private void CloseTrade()
        {
            if (_entryOrder != null && !_entryOrder.Status.IsClosed())
            {
                _entryOrder.Cancel();
            }

            if (_slOrder != null && !_slOrder.Status.IsClosed())
            {
                _slOrder.Cancel();
            }
            
            if (_tpOrder != null && !_tpOrder.Status.IsClosed())
            {
                _tpOrder.Cancel();
            }

            if (_state == TradeState.OPEN)
            {
                _algorithm.PrintBalance();
            }
            _state = TradeState.CLOSED;
        }

        public TradeSetupData GetStats()
        {
            TradeSetupData result = new TradeSetupData()
            {
                BarTime = _setupBar.Time.ConvertToUtc(_algorithm.TimeZone),
                direction = _direction.ToString(),
                slPips = _riskPips,
                tpPips = _tpPips,
                plPips = _profitLossPips,
                symbol = _symbol,
                tradeIndex = _algorithm.TradeBuilder.ClosedTrades.IndexOf(_trade)
            };
            return result;
        }
    }
}