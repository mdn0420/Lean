using System;
using System.Linq;
using LucrumLabs.Algorithm;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Forex;
using QuantConnect.Statistics;

namespace LucrumLabs.Trades
{
    /// <summary>
    /// Class that sets up entry orders and corresponding stop loss and take profit orders
    /// </summary>
    public abstract class ManagedTrade
    {
        public enum TradeState
        {
            PENDING,
            OPEN,
            CLOSED
        }

        protected QCAlgorithm _algorithm;
        protected Symbol _symbol;
        protected OrderDirection _direction;
        
        protected decimal _entryPrice;
        protected decimal _slPrice;
        protected decimal _tpPrice;
        protected int _quantity;

        protected decimal _riskPips;
        protected decimal _tpPips;

        protected OrderType _entryOrderType;

        protected DateTime _openTimeUtc = DateTime.MinValue;
        protected DateTime _entryTimeUtc = DateTime.MinValue;
        protected DateTime _closeTimeUtc = DateTime.MinValue;

        public TradeState State => _state;
        protected TradeState _state;
        protected OrderTicket _entryOrder;
        protected OrderTicket _slOrder;
        protected OrderTicket _tpOrder;
        protected OrderTicket _closeOrder; // Manual exit order

        protected decimal _profitLossPips;
        
        private ManualTradeBuilder _tradeBuilder;
        private int _tradeId;

        protected Trade _trade;

        protected virtual string TradeName
        {
            get;
            private set;
        }

        public virtual bool WasCancelled => false; 
        
        public ManagedTrade(QCAlgorithm algorithm, Symbol symbol, OrderDirection direction, OrderType entryOrderType)
        {
            // todo: it would be nice to register for order events directly with transaction handler
            _algorithm = algorithm;
            _tradeBuilder = algorithm.TradeBuilder as ManualTradeBuilder;
            _symbol = symbol;
            TradeName = _symbol;
            _direction = direction;
            _entryOrderType = entryOrderType;
        }

        abstract protected void CalculatePrices();

        public void Execute()
        {
            CalculatePrices();
            RoundPrice(ref _entryPrice);
            RoundPrice(ref _slPrice);
            RoundPrice(ref _tpPrice);
            
            Forex pair = _algorithm.Securities[_symbol] as Forex;
            decimal pipSize = ForexUtils.GetPipSize(pair);
            _riskPips = Math.Abs(_entryPrice - _slPrice) / pipSize;
            _tpPips = Math.Abs(_entryPrice - _tpPrice) / pipSize;

            if (_entryOrder != null)
            {
                _algorithm.Error("Tried to place duplicate trade");
                return;
            }

            if (_quantity == 0)
            {
                _algorithm.Error("Calculated a quantiy of 0 for trade.");
                Close();
                return;
            }

            Console.WriteLine("{0} Setup order for {1} {2}, entry:{3}, sl:{4}, tp:{5}", _algorithm.UtcTime, _quantity, _symbol, _entryPrice, _slPrice, _tpPrice);


            _openTimeUtc = _algorithm.UtcTime;
            _tradeId = _tradeBuilder.OpenTrade(_openTimeUtc, _slPrice, _tpPrice);
            
            switch (_entryOrderType)
            {
                case OrderType.Limit:
                    _entryOrder = _algorithm.LimitOrder(_symbol, _quantity, _entryPrice);
                    break;
                case OrderType.StopMarket:
                    _entryOrder = _algorithm.StopMarketOrder(_symbol, _quantity, _entryPrice);
                    break;
                case OrderType.Market:
                    _entryOrder = _algorithm.MarketOrder(_symbol, _quantity, true);
                    OnTradeEntered();
                    break;
                default:
                    _algorithm.Error(string.Format("Unsupported entry order type {0}", _entryOrderType));
                    break;
            }

            if (_entryOrder == null)
            {
                Close();
                return;
            }
            
            if (_entryOrder.Status == OrderStatus.Invalid)
            {
                // something wrong
                _algorithm.Error("Something was wrong with entry order.. cancelling trade");
                Close();
            }
        }

        /// <summary>
        /// Close out the position and/or cancel any pending orders
        /// </summary>
        public void Close()
        {
            _closeTimeUtc = _algorithm.UtcTime;
            bool slOrTpHit = (_slOrder != null && _slOrder.QuantityFilled != 0) ||
                             (_tpOrder != null && _tpOrder.QuantityFilled != 0);
            if (_entryOrder != null && !_entryOrder.Status.IsClosed())
            {
                _tradeBuilder.CancelTrade(_tradeId);
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

            if (!slOrTpHit && _state == TradeState.OPEN && _entryOrder != null && _entryOrder.QuantityFilled != 0)
            {
                Console.WriteLine("Liquidating {0} of {1}",-_entryOrder.QuantityFilled, _symbol);
                _closeOrder = _algorithm.MarketOrder(_symbol, -_entryOrder.QuantityFilled);
            }

            //_algorithm.PrintBalance();
            _state = TradeState.CLOSED;
        }

        public bool HasOrderId(int orderId)
        {
            if (_entryOrder != null && _entryOrder.OrderId == orderId) return true;
            if (_slOrder != null && _slOrder.OrderId == orderId) return true;
            if (_tpOrder != null && _tpOrder.OrderId == orderId) return true;

            return false;
        }

        private void OnTradeEntered()
        {
            TradeName = string.Format("{0}:{1}", _symbol, _entryOrder.OrderId);
            _entryTimeUtc = _entryOrder.Time;
            // Setup TP/SL orders
            Console.WriteLine("{0} - {3} Entered {1} trade for {2:N0} @ {4}", _algorithm.UtcTime, _direction, _entryOrder.QuantityFilled, TradeName, _entryOrder.AverageFillPrice);
            _state = TradeState.OPEN;
            
            _tradeBuilder.RegisterTradeEntry(_tradeId, _entryOrder.OrderId);
            //_algorithm.PrintBalance();
            PlaceManagementOrders();
        }
        
        public void OnOrderEvent(OrderEvent orderEvent)
        {
            if (_entryOrder != null && _entryOrder.OrderId == orderEvent.OrderId)
            {
                if (_state == TradeState.PENDING && orderEvent.Status == OrderStatus.Filled)
                {
                    OnTradeEntered();
                } 
                else if (orderEvent.Status == OrderStatus.Canceled)
                {
                    Console.WriteLine("{0} - {1} Trade cancelled", _algorithm.UtcTime, TradeName);
                    Close();
                }
                else if (orderEvent.Status == OrderStatus.Invalid)
                {
                    Console.WriteLine("Entry order invalid");
                    Close();
                }
            }
            else if (_slOrder != null && _slOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    _tradeBuilder.RegisterTradeExit(_tradeId, _slOrder.OrderId);
                    UpdateProfitLoss();
                    Console.WriteLine(
                        "{0} - {1} Stop loss hit at {2}, Qty: {3}",
                        _algorithm.UtcTime,
                        TradeName,
                        orderEvent.FillPrice,
                        orderEvent.FillQuantity
                    );
                    Close();
                }
            }
            else if (_tpOrder != null && _tpOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    _tradeBuilder.RegisterTradeExit(_tradeId, _tpOrder.OrderId);
                    UpdateProfitLoss();
                    Console.WriteLine(
                            "{0} - {1} Take profit hit at {2}, Qty: {3}",
                            _algorithm.UtcTime,
                            TradeName,
                            orderEvent.FillPrice,
                            orderEvent.FillQuantity
                    );
                    Close();
                }
            }
            else
            {
                _algorithm.Error(string.Format("{0} received unexpected order event {1}", GetType().Name));
            }
        }

        /// <summary>
        /// Inform the trade of relevant price updates
        /// </summary>
        /// <param name="bar"></param>
        public virtual void OnDataUpdate(QuoteBar bar)
        {
            
        }
        
        private void UpdateProfitLoss()
        {
            
            _trade = _algorithm.TradeBuilder.ClosedTrades.LastOrDefault();
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
        
        protected void RoundPrice(ref decimal value)
        {
            var security = _algorithm.Securities[_symbol];
            
            var increment = security.PriceVariationModel.GetMinimumPriceVariation(
                new GetMinimumPriceVariationParameters(security, value));
            if (increment > 0)
            {
                value = Math.Round(value / increment) * increment;
            }
        }
        
        private void PlaceManagementOrders()
        {
            //_algorithm.Debug(string.Format("Opening orders - SL: {0}, TP: {1}", slPrice, tpPrice));
            _tpOrder = _algorithm.LimitOrder(_symbol, -_quantity, _tpPrice, string.Format("tp:{0}", _entryOrder.OrderId));
            _slOrder = _algorithm.StopMarketOrder(_symbol, -_quantity, _slPrice, string.Format("sl:{0}", _entryOrder.OrderId));
        }
        
        public virtual TradeSetupData GetStats()
        {
            var fillPrice = 0m;
            if (_entryOrder.QuantityFilled != 0)
            {
                fillPrice = _entryOrder.AverageFillPrice;
            }
            TradeSetupData result = new TradeSetupData()
            {
                BarTime = _openTimeUtc,
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
                canceled = WasCancelled,
                tradeIndex = _algorithm.TradeBuilder.ClosedTrades.IndexOf(_trade)
            };
            return result;
        }
    }
}