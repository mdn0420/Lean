using System.Collections.Generic;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data.Market;
using QuantConnect.Orders;

namespace LucrumLabs.Algorithm
{
    public class ParallaxTradeSettings
    {
        public bool ScaleIn = false;

        public bool ActiveTradeManagement = false;

        public decimal Entry1Fib = 0.236m;
        public decimal Sl1Fib = 0.786m;
        public decimal Tp1Fib = -1.618m;
        
        // Used if we're scaling in
        public decimal Entry2Fib = 0.382m;
        public decimal Sl2Fib = 0.786m;
        public decimal Tp2Fib = -1.618m;

        public decimal Risk = 0.01m;
    }
    /// <summary>
    /// Handles executing on a trade setup. Multiple trades can be associated with a single setup (scale in)
    /// </summary>
    public class ParallaxTradeSetup
    {
        private QCAlgorithm _algorithm;
        private QuoteBar _ibar;
        private QuoteBar _setupBar;
        private OrderDirection _direction;
        private Symbol _symbol;

        private ParallaxTrade _trade1;
        private ParallaxTrade _trade2;

        private ParallaxTradeSettings _settings;

        public ParallaxTrade.TradeState State {
            get
            {
                var trade1State = _trade1 != null ? _trade1.State : ParallaxTrade.TradeState.CLOSED;
                var trade2State = _trade2 != null ? _trade2.State : ParallaxTrade.TradeState.CLOSED;

                ParallaxTrade.TradeState result;
                if (trade1State != ParallaxTrade.TradeState.CLOSED)
                {
                    // prioritize trade1?
                    result = trade1State;
                }
                else if(trade2State != ParallaxTrade.TradeState.CLOSED)
                {
                    // prioritize trade1?
                    result = trade2State;
                }
                else
                {
                    // both trades are closed
                    result = ParallaxTrade.TradeState.CLOSED;
                }

                return result;
            }
        }
        private ParallaxTrade.TradeState _state = ParallaxTrade.TradeState.PENDING;
        
        public ParallaxTradeSetup(QCAlgorithm algorithm, QuoteBar ibar, QuoteBar setupBar, OrderDirection direction, ParallaxTradeSettings settings)
        {
            _algorithm = algorithm;
            _ibar = ibar;
            _setupBar = setupBar;
            _direction = direction;
            _symbol = setupBar.Symbol;
            _settings = settings;
        }

        public void PlaceOrders()
        {
            if (_settings.ScaleIn)
            {
                var risk = _settings.Risk * 0.5m;
                _trade1 = new ParallaxTrade(_algorithm, _setupBar, _direction, _settings.Entry1Fib, _settings.Sl1Fib, _settings.Tp1Fib, risk, _settings.ActiveTradeManagement, "1");
                _trade2 = new ParallaxTrade(_algorithm, _setupBar, _direction, _settings.Entry2Fib, _settings.Sl2Fib, _settings.Tp2Fib, risk, _settings.ActiveTradeManagement, "2");
                _trade1.Execute();
                _trade2.Execute();
            }
            else
            {
                _trade1 = new ParallaxTrade(_algorithm, _setupBar, _direction, _settings.Entry1Fib, _settings.Sl1Fib, _settings.Tp1Fib, _settings.Risk, _settings.ActiveTradeManagement);
                _trade1.Execute();
            }
        }

        public bool HasOrderId(int orderId)
        {
            bool result = (_trade1 != null && _trade1.HasOrderId(orderId)) ||
                (_trade2 != null && _trade2.HasOrderId(orderId));
            return result;
        }

        public void OnOrderEvent(OrderEvent orderEvent)
        {
            var orderId = orderEvent.OrderId;
            if (_trade1.HasOrderId(orderId))
            {
                _trade1.OnOrderEvent(orderEvent);
            } 
            else if (_trade2 != null && _trade2.HasOrderId(orderId))
            {
                _trade2.OnOrderEvent(orderEvent);
            }
        }

        public void OnDataUpdate(QuoteBar bar)
        {
            if (bar.Symbol != _symbol)
            {
                _algorithm.Error(string.Format("Received {0} bar data for {1} trade", bar.Symbol, _symbol));
                return;
            }

            _trade1?.OnDataUpdate(bar);
            _trade2?.OnDataUpdate(bar);
        }

        public List<TradeSetupData> GetStats()
        {
            var result = new List<TradeSetupData>();
            if (_trade1 != null)
            {
                result.Add(_trade1.GetStats());
            }

            if (_trade2 != null)
            {
                result.Add(_trade2.GetStats());
            }
            return result;
        }
    }
}