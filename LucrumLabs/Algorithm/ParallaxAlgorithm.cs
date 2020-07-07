using System;
using System.Collections.Generic;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;

namespace LucrumLabs.Algorithm
{
    public class ParallaxAlgorithm : QCAlgorithm
    {
        /// <summary>
        /// Body of indecision bar should be less than this
        /// </summary>
        private const float IndecisionBodyRatioMax = 0.6f;

        /// <summary>
        /// Minimum size of wick of indecision bar in the opposite direction
        /// </summary>
        private const float IndecisionWickRatioMin = 0.1f;

        /// <summary>
        /// Minimum distance from the edge of the bank for the indecision bar body
        /// </summary>
        private const float IndecisionMinBBDistance = 0.8f;

        private const float SetupBodyRatioMin = 0.4f;
        
        private const string SYMBOL = "EURUSD";

        private RollingWindow<QuoteBar> _setupWindow = new RollingWindow<QuoteBar>(2);
        private RollingWindow<IndicatorDataPoint> _bbUpperWindow = new RollingWindow<IndicatorDataPoint>(2);
        private RollingWindow<IndicatorDataPoint> _bbLowerWindow = new RollingWindow<IndicatorDataPoint>(2);
        private RollingWindow<IndicatorDataPoint> _bbMidWindow = new RollingWindow<IndicatorDataPoint>(2);

        private Dictionary<Symbol, ParallaxTrade> _activeTrades = new Dictionary<Symbol, ParallaxTrade>();
        
        private Stochastic _stochastic;
        private BollingerBands _bb;
        
        public override void Initialize()
        {
            SetStartDate(2020, 04, 01);
            SetEndDate(2020, 06, 30);
            SetCash(100000);
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);

            AddForex(SYMBOL, Resolution.Minute, Market.Oanda);
            
            _stochastic = STO(SYMBOL, 14, 3, 3, Resolution.Hour);
            _bb = BB(SYMBOL, 20, 2, MovingAverageType.Simple, Resolution.Hour);
            
            // This needs to get created last so the bar gets processed after indicators are updated
            var consolidator = new QuoteBarConsolidator(TimeSpan.FromMinutes(60));
            SubscriptionManager.AddConsolidator(SYMBOL, consolidator);
            consolidator.DataConsolidated += OnDataConsolidated;
        }

        private void OnDataConsolidated(object sender, QuoteBar bar)
        {
            /*
            Log(string.Format("{0} - Stoch: {1}/{2}, BB: {3}/{4}", 
                bar.Time.ToString(), 
                _stochastic.StochK.Current.Value,
                _stochastic.StochD.Current.Value,
                _bb.UpperBand.Current.Value,
                _bb.LowerBand.Current.Value));*/

            if (!_bb.IsReady || !_stochastic.IsReady)
            {
                return;
            }

            //Log(string.Format("{0} - AskO: {1}, BidO: {2}, O: {3}", bar.Time, bar.Ask.Open, bar.Bid.Open, bar.Open));
            
            _setupWindow.Add(bar);
            _bbUpperWindow.Add(_bb.UpperBand.Current);
            _bbLowerWindow.Add(_bb.LowerBand.Current);
            _bbMidWindow.Add(_bb.MiddleBand.Current);
            CheckSetup(_setupWindow);
        }

        private void CheckSetup(RollingWindow<QuoteBar> window)
        {
            if (!window.IsReady)
            {
                return;
            }

            var prevBar = window[1];
            var thisBar = window[0];

            int ibar = IsIndecisionBar(prevBar, 
                _bbUpperWindow[1].Value, 
                _bbLowerWindow[1].Value, 
                _bbMidWindow[1].Value);
            
            /*
            if (ibar != 0)
            {
                string direction = ibar == 1 ? "long" : "short";
                Log(string.Format("{0} - {1} indecision bar", prevBar.Time, direction));
            }*/
            
            var setupRatios = thisBar.GetBarRatios();
            if (ibar != 0 && setupRatios.Body > SetupBodyRatioMin)
            {
                var stochK = _stochastic.StochK.Current.Value;
                var stochD = _stochastic.StochD.Current.Value;
                
                // long setup
                if (ibar == 1 && 
                    thisBar.Close > prevBar.Close && 
                    thisBar.High > prevBar.High &&
                    stochK <= 25 &&
                    stochD <= 25)
                {
                    Debug(string.Format("{0} LONG setup: C: {1}, K/D: {2:N3}/{3:N3}", 
                        thisBar.Time, thisBar.Close, stochK, stochD));
                    TryOpenTrade(thisBar, OrderDirection.Buy);
                }
                else if (ibar == -1 && 
                         thisBar.Close < prevBar.Close &&
                         thisBar.Low < prevBar.Low &&
                         stochK >= 75 &&
                         stochD >= 75)
                {
                    Debug(string.Format("{0} SHORT setup: C: {1}, K/D: {2:N3}/{3:N3}", thisBar.Time, thisBar.Close, stochK, stochD));
                    TryOpenTrade(thisBar, OrderDirection.Sell);
                }
            }
        }

        private void TryOpenTrade(QuoteBar setupBar, OrderDirection direction)
        {
            var symbol = setupBar.Symbol;
            if (_activeTrades.ContainsKey(symbol))
            {
                return;
            }
            
            ParallaxTrade trade = _activeTrades[symbol] = new ParallaxTrade(this, setupBar, symbol.Value, direction);
            trade.PlaceOrders();
        }

        private void CleanupTrades()
        {
            List<Symbol> remove = new List<Symbol>();
            foreach (var kvp in _activeTrades)
            {
                if (kvp.Value.State == ParallaxTrade.TradeState.CLOSED)
                {
                    remove.Add(kvp.Key);
                }
            }

            foreach (var symbol in remove)
            {
                _activeTrades.Remove(symbol);
            }
        }

        private int IsIndecisionBar(QuoteBar bar, decimal bbUpper, decimal bbLower, decimal bbMid)
        {
            if (!_bb.IsReady)
            {
                return 0;
            }
            
            int result = 0;
            var ratios = bar.GetBarRatios();

            if (ratios.Body < IndecisionBodyRatioMax)
            {
                var bodyTop = bar.GetBodyTop();
                var bodyBottom = bar.GetBodyBottom();

                var bbTopLerp = (float)MathUtils.InvLerp(bbMid, bbUpper, bodyTop);
                var bbBottomLerp = (float)MathUtils.InvLerp(bbMid, bbLower, bodyBottom);

                // Long indecision bar
                if (ratios.Bottom > IndecisionWickRatioMin && bbBottomLerp >= IndecisionMinBBDistance)
                {
                    result = 1;
                } 
                else if (ratios.Top > IndecisionWickRatioMin && bbTopLerp >= IndecisionMinBBDistance)
                {
                    result = -1;
                }
            }
            
            return result;
        }

        public override void OnData(Slice slice)
        {
            //Log(string.Format("{0} OnData", slice.Time.ToString()));
            QuoteBar bar;
            foreach (var kvp in _activeTrades)
            {
                if (slice.QuoteBars.TryGetValue(kvp.Key, out bar))
                {
                    kvp.Value.OnDataUpdate(bar);
                } 
            }
            
            CleanupTrades();
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            foreach (var trade in _activeTrades.Values)
            {
                if (trade.HasOrderId(orderEvent.OrderId))
                {
                    trade.OnOrderEvent(orderEvent);
                }
            }
            
            CleanupTrades();
        }
    }
}