using System;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;

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

        protected OrderType _entryOrderType;

        public TradeState State => _state;
        protected TradeState _state;
        protected OrderTicket _entryOrder;
        protected OrderTicket _slOrder;
        protected OrderTicket _tpOrder;
        protected OrderTicket _closeOrder; // Manual exit order
        
        public ManagedTrade(QCAlgorithm algorithm, Symbol symbol, OrderDirection direction, OrderType entryOrderType)
        {
            // todo: it would be nice to register for order events directly with transaction handler
            _algorithm = algorithm;
            _symbol = symbol;
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

            //Console.WriteLine("{0} Setup order for {1} {2}, entry:{3}, sl:{4}, tp:{5}", _bar2.Time, _units, _symbol, _entryPrice, _slPrice, _tpPrice);
            

            switch (_entryOrderType)
            {
                case OrderType.Limit:
                    _entryOrder = _algorithm.LimitOrder(_symbol, _quantity, _entryPrice);
                    break;
                case OrderType.StopMarket:
                    _entryOrder = _algorithm.StopMarketOrder(_symbol, _quantity, _entryPrice);
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
            bool slOrTpHit = (_slOrder != null && _slOrder.QuantityFilled != 0) ||
                             (_tpOrder != null && _tpOrder.QuantityFilled != 0);
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
        
        public void OnOrderEvent(OrderEvent orderEvent)
        {
            if (_entryOrder != null && _entryOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    // Setup TP/SL orders
                    Console.WriteLine("{0} - Entered {1} trade for {2:N0} {3} @ {4}", _algorithm.Time, _direction, orderEvent.Quantity, _symbol, orderEvent.FillPrice);
                    _state = TradeState.OPEN;
                    //_algorithm.PrintBalance();
                    PlaceManagementOrders();
                } 
                else if (orderEvent.Status == OrderStatus.Canceled)
                {
                    Console.WriteLine("{0} - Trade for {1} cancelled", _algorithm.Time, _symbol);
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
                    /*
                    UpdateProfitLoss();
                    Console.WriteLine(
                        "{0} - Stop loss hit for {1} at {2} - P/L: {3}",
                        _algorithm.Time,
                        _symbol,
                        orderEvent.FillPrice,
                        _profitLossPips
                    );*/
                    Close();
                }
            }
            else if (_tpOrder != null && _tpOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    /*
                    UpdateProfitLoss();
                    Console.WriteLine(
                            "{0} - Take profit hit for {1} at {2} - P/L: {3}",
                            _algorithm.Time,
                            _symbol,
                            orderEvent.FillPrice,
                            _profitLossPips
                    );*/
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
    }
}