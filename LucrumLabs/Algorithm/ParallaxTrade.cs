using System;
using System.Diagnostics;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;

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

        public TradeState State => _state;
        private TradeState _state = TradeState.PENDING;
        
        private ParallaxAlgorithm _algorithm;
        
        private OrderTicket _entryOrder;
        private OrderTicket _tpOrder;
        private OrderTicket _slOrder;

        private QuoteBar _setupBar;
        private OrderDirection _direction;

        private decimal _extensionPrice;

        private Symbol _symbol;
        private int _lotSize;

        public ParallaxTrade(ParallaxAlgorithm algorithm, QuoteBar setupBar, Symbol symbol, OrderDirection direction)
        {
            _algorithm = algorithm;
            _setupBar = setupBar;
            _symbol = symbol;
            _direction = direction;
            
            // todo: calculate lot size
            _lotSize = 100000;

            const decimal extensionFibLevel = -0.382m;
            if (direction == OrderDirection.Buy)
            {
                _extensionPrice = MathUtils.GetFibPrice(setupBar.Low, setupBar.High, extensionFibLevel);
            }
            else
            {
                _extensionPrice = MathUtils.GetFibPrice(setupBar.High, setupBar.Low, extensionFibLevel);
            }
        }

        public void PlaceOrders()
        {
            if (_entryOrder != null)
            {
                _algorithm.Error("Tried to place duplicate trade");
                return;
            }
            
            decimal entryFibLevel = 0.236m;
            decimal entryPrice;
            decimal spread = _setupBar.Ask.Close - _setupBar.Bid.Close;
            int lotSize;
            if (_direction == OrderDirection.Buy)
            {
                entryPrice = MathUtils.GetFibPrice(_setupBar.Low, _setupBar.High, entryFibLevel) - spread;
                lotSize = _lotSize;
            }
            else
            {
                entryPrice = MathUtils.GetFibPrice(_setupBar.High, _setupBar.Low, entryFibLevel) + spread;
                lotSize = -_lotSize;
            }
            
            RoundPrice(ref entryPrice);
            
            //_algorithm.Debug(string.Format("Placing limit order for {0} at {1}", _symbol, entryPrice));
            
            _entryOrder = _algorithm.LimitOrder(_symbol, lotSize, entryPrice);
        }

        private void PlaceManagementOrders()
        {
            decimal slFiblevel = 0.618m;
            decimal tpFibLevel = -0.618m;
            decimal slPrice, tpPrice;
            int lotSize;
            if (_direction == OrderDirection.Buy)
            {
                slPrice = MathUtils.GetFibPrice(_setupBar.Low, _setupBar.High, slFiblevel);
                tpPrice = MathUtils.GetFibPrice(_setupBar.Low, _setupBar.High, tpFibLevel);
                lotSize = _lotSize;
            }
            else
            {
                slPrice = MathUtils.GetFibPrice(_setupBar.High, _setupBar.Low, slFiblevel);
                tpPrice = MathUtils.GetFibPrice(_setupBar.High, _setupBar.Low, tpFibLevel);
                lotSize = -_lotSize;
            }
            
            RoundPrice(ref slPrice);
            RoundPrice(ref tpPrice);
            
            //_algorithm.Debug(string.Format("Opening orders - SL: {0}, TP: {1}", slPrice, tpPrice));
            _tpOrder = _algorithm.LimitOrder(_symbol, -lotSize, tpPrice, string.Format("tp:{0}", _entryOrder.OrderId));
            _slOrder = _algorithm.StopMarketOrder(_symbol, -lotSize, slPrice, string.Format("sl:{0}", _entryOrder.OrderId));
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
                    _algorithm.Log(string.Format("{0} - Entered {1} trade for {2} at {3}", _algorithm.Time, _direction, _symbol, orderEvent.FillPrice));
                    _state = TradeState.OPEN;
                    PlaceManagementOrders();
                } 
                else if (orderEvent.Status == OrderStatus.Canceled)
                {
                    _algorithm.Log(string.Format("{0} - Trade for {1} cancelled", _algorithm.Time, _symbol));
                    CloseTrade();
                }
            }
            else if (_slOrder != null && _slOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    _algorithm.Log(string.Format("{0} - Stop loss hit for {1} at {2}", _algorithm.Time, _symbol, orderEvent.FillPrice));
                    CloseTrade();
                }
            }
            else if (_tpOrder != null && _tpOrder.OrderId == orderEvent.OrderId)
            {
                if (orderEvent.Status == OrderStatus.Filled)
                {
                    _algorithm.Log(
                        string.Format(
                            "{0} - Take profit hit for {1} at {2}",
                            _algorithm.Time,
                            _symbol,
                            orderEvent.FillPrice
                        )
                    );
                    CloseTrade();
                }
            }
            else
            {
                _algorithm.Error(string.Format("ParallaxTrade received unexpected order event {0}", orderEvent));
            }
        }

        public void OnDataUpdate(QuoteBar bar)
        {
            if (_state != TradeState.OPEN)
            {
                // Cancel order if we haven't filled and we already hit the first extension level
                var price = bar.Price;
                if (_direction == OrderDirection.Buy && price > _extensionPrice)
                {
                    CloseTrade();
                } 
                else if (_direction == OrderDirection.Sell && price < _extensionPrice)
                {
                    CloseTrade();
                }
            }
        }

        private void CloseTrade()
        {
            if (_entryOrder != null && _entryOrder.QuantityFilled == 0)
            {
                _entryOrder.Cancel();
            }

            if (_slOrder != null && _slOrder.QuantityFilled == 0)
            {
                _slOrder.Cancel();
            }
            
            if (_tpOrder != null && _tpOrder.QuantityFilled == 0)
            {
                _tpOrder.Cancel();
            }
            _state = TradeState.CLOSED;
        }
    }
}