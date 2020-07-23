//#define DEBUG_PRINT_BARS

using System;
using System.Collections.Generic;
using System.IO;
using LucrumLabs.Data;
using Newtonsoft.Json;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Algorithm
{
    public class ParallaxAlgorithm : QCAlgorithm
    {
        public static readonly decimal[] FibRetraceLevels = new[] {0.236m, 0.382m, 0.5m, 0.618m, 0.786m, 1m};
        public static readonly decimal[] FibExtensionLevels = new[] {0m, -0.382m, -0.618m, -1m, -1.618m};
        /// <summary>
        /// Body of indecision bar should be less than this
        /// </summary>
        private const decimal IndecisionBodyRatioMax = 0.6m;

        /// <summary>
        /// Minimum size of wick of indecision bar in the opposite direction
        /// </summary>
        private const decimal IndecisionWickRatioMin = 0.1m;

        /// <summary>
        /// Minimum distance from the edge of the bank for the indecision bar body
        /// </summary>
        private const decimal IndecisionMinBBDistance = 0.95m;

        /// <summary>
        /// Minimum ratio of body of setup candle
        /// </summary>
        private const decimal SetupBodyRatioMin = 0.2m;

        /// <summary>
        /// Maximum size of wick for setup bar in the trade direction, 1.0 disables this check
        /// </summary>
        private const decimal SetupWickRatioMax = 1m;

        /// <summary>
        /// Maximum normalized distance of setup bar to mid BB, 1.0 disables this check
        /// </summary>
        private const decimal SetupCloseMidBBMax = 1m;

        /// <summary>
        /// How large the setup bar needs to be relative to the ATR, 0 disables this
        /// </summary>
        private const decimal SetupBarLengthATRScale = 0m;

        /// <summary>
        /// Index to the Fib retracement array.
        /// </summary>
        private const int MaxSetupFibRetracement = 1;

        private TimeSpan TradingTimeFrame = TimeSpan.FromHours(4);

        private const string SYMBOL = "USDCAD";

        private RollingWindow<QuoteBar> _setupWindow = new RollingWindow<QuoteBar>(2);
        private RollingWindow<IndicatorDataPoint> _bbUpperWindow = new RollingWindow<IndicatorDataPoint>(2);
        private RollingWindow<IndicatorDataPoint> _bbLowerWindow = new RollingWindow<IndicatorDataPoint>(2);
        private RollingWindow<IndicatorDataPoint> _bbMidWindow = new RollingWindow<IndicatorDataPoint>(2);

        private Dictionary<Symbol, ParallaxTrade> _activeTrades = new Dictionary<Symbol, ParallaxTrade>();

        private AlgorithmResults _results = new AlgorithmResults();
        
        private Stochastic _stochastic;
        private BollingerBands _bb;
        private AverageTrueRange _atr;
        
        public override void Initialize()
        {
            var tfName = TradingTimeFrame.Hours == 24 ? "D1" : $"H{TradingTimeFrame.Hours}";
            SetAlgorithmId($"{AlgorithmId}-{SYMBOL}-{tfName}");
            SetStartDate(2017, 1, 1);
            SetEndDate(2019, 12, 31);
            SetCash(100000);
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);

            AddForex(SYMBOL, Resolution.Minute, Market.Oanda);
            Securities[SYMBOL].SetLeverage(50m);

            var consolidator = new SmoothQuoteBarConsolidator(NewYorkClosePeriod(TradingTimeFrame));
            SubscriptionManager.AddConsolidator(SYMBOL, consolidator);

            _stochastic = new Stochastic(14, 3, 3);
            _bb = new BollingerBands(20, 2);
            _atr = new AverageTrueRange(14, MovingAverageType.Simple);
            
            RegisterIndicator(SYMBOL, _stochastic, consolidator);
            RegisterIndicator(SYMBOL, _bb, consolidator);
            RegisterIndicator(SYMBOL, _atr, consolidator);

            // This needs to get added last so the bar gets processed after indicators are updated
            consolidator.DataConsolidated += OnDataConsolidated;
        }

        /// <summary>
        /// Calculates period aligned with NY session close time
        /// </summary>
        /// <param name="period"></param>
        /// <returns></returns>
        private Func<DateTime, CalendarInfo> NewYorkClosePeriod(TimeSpan period)
        {
            return dt =>
            {
                // dt is start time of the data slice
                var nyc = dt.ConvertTo(TimeZone, TimeZones.NewYork).RoundUp(TimeSpan.FromHours(1));
                int closeHour = 17; // 5pm)
                if (nyc.Hour <= closeHour)
                {
                    nyc = nyc.AddHours(closeHour - nyc.Hour);
                }
                else
                {
                    nyc = nyc.AddHours(closeHour + (24 - nyc.Hour));
                }

                // walk backwards until we find the period this time is in
                DateTime periodStart = nyc.ConvertTo(TimeZones.NewYork, TimeZone);
                while (dt < periodStart)
                {
                    periodStart = periodStart.Subtract(period);
                }

                var result = new CalendarInfo(periodStart, period);
                return result;
            };
        }

        private void OnDataConsolidated(object sender, QuoteBar bar)
        {
            #if DEBUG_PRINT_BARS   
            string debugStr = string.Format(
                "Consolidated: {0} - O:{1:F5} H:{2:F5} L:{3:F5} C:{4:F5} ",
                bar.Time.ToString(),
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close
            );
            debugStr += string.Format("BBMid:{0:F5} Up: {1:F5} Low: {2:F5}", _bb.MiddleBand, _bb.UpperBand, _bb.LowerBand);
            Console.WriteLine(debugStr);
            #endif
                

            if (!_bb.IsReady || !_stochastic.IsReady)
            {
                return;
            }

            var data = new ResultBarData(bar, TimeZone)
            {
                BBMid = _bb.MiddleBand,
                BBUpper = _bb.UpperBand,
                BBLower = _bb.LowerBand,
                StochD = _stochastic.StochD,
                StochK = _stochastic.StochK
            };
            _results.BarData.Add(data);
            
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
                Console.WriteLine("Ibar found {0}", prevBar.EndTime);
            }*/

            var ibarBodyLength = prevBar.GetBodyTop() - prevBar.GetBodyBottom();
            var setupBodyLength = thisBar.GetBodyTop() - thisBar.GetBodyBottom();

            if (setupBodyLength < _atr * SetupBarLengthATRScale)
            {
                return;
            }

            var setupRatios = thisBar.GetBarRatios();
            if (ibar != 0 && setupRatios.Body > SetupBodyRatioMin && setupBodyLength > ibarBodyLength)
            {
                var stochK = _stochastic.StochK.Current.Value;
                var stochD = _stochastic.StochD.Current.Value;

                var maxSetupRetrace = FibRetraceLevels[MaxSetupFibRetracement];
                var spread = thisBar.GetSpread();
                // long setup
                if (ibar == 1 && 
                    thisBar.Close > prevBar.Close &&  // higher close
                    thisBar.High > prevBar.High && // higher high
                    stochK <= 25 && // Stoch showing oversold
                    stochD <= 25 &&
                    // Didn't retrace too far on close
                    thisBar.Close > MathUtils.GetFibPrice(thisBar.Low, thisBar.High, maxSetupRetrace) &&
                    setupRatios.Top < SetupWickRatioMax) // Small wick in direction of trade
                {
                    Console.WriteLine(
                        "{0} LONG setup: H/L: {1}/{2}, spread: {3}",
                        thisBar.Time,
                        thisBar.High,
                        thisBar.Low,
                        spread
                    );
                    TryOpenTrade(thisBar, OrderDirection.Buy);
                }
                else if (ibar == -1 && 
                         thisBar.Close < prevBar.Close &&
                         thisBar.Low < prevBar.Low &&
                         stochK >= 75 &&
                         stochD >= 75 &&
                         thisBar.Close < MathUtils.GetFibPrice(thisBar.High, thisBar.Low, maxSetupRetrace) &&
                         setupRatios.Bottom < SetupWickRatioMax)
                {
                    Console.WriteLine(
                        "{0} SHORT setup: H/L: {1}/{2}, spread: {3}",
                        thisBar.Time,
                        thisBar.High,
                        thisBar.Low,
                        spread
                    );
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
                var trade = _activeTrades[symbol];
                TradeSetupData setupData = trade.GetStats();
                _results.TradeSetups.Add(setupData);
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
                var bbTopLerp = MathUtils.InvLerp(bbMid, bbUpper, bar.High);
                var bbBottomLerp = MathUtils.InvLerp(bbMid, bbLower, bar.Low);

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
            /*
            QuoteBar b = slice.QuoteBars[SYMBOL];
            if (b.Time.Day == 13 && (b.Time.Hour == 18 || b.Time.Hour == 17))
            {
                Log(
                    string.Format(
                        "OnData - {0} - O:{1} H:{2} L:{3} C:{4}",
                        b.Time.ToString(),
                        b.Open,
                        b.High,
                        b.Low,
                        b.Close
                    )
                );
            }*/

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

        public int CalculatePositionSize(Forex pair, decimal pips, decimal balance, decimal riskPercent)
        {
            var quoteCurrency = pair.QuoteCurrency;

            int lotSize = 0;
            decimal pipSize = ForexUtils.GetPipSize(quoteCurrency.Symbol);

            if (quoteCurrency.ConversionRate <= 0m)
            {
                Error("Could not find account conversion rate when calculating position size");
                return 0;
            }
            
            // risk amount in the quote currency
            decimal riskAmount = balance * riskPercent / quoteCurrency.ConversionRate;

            decimal unitsPerCurrency = 1m / pipSize;
            var pipValue = riskAmount / pips;
            lotSize = (int)(pipValue * unitsPerCurrency);

            //Debug(string.Format("Lot size: {0}, Risk: {1:P1}, Balance: {2:C}, Price: {3}, ConversionRate: {4}", lotSize, riskPercent, balance, pair.Price, quoteCurrency.ConversionRate));
            return lotSize;
        }

        public override void OnEndOfAlgorithm()
        {
            string resultsFolder = Config.Get("results-destination-folder", Directory.GetCurrentDirectory());;
            var filePath = Path.Combine(resultsFolder, $"{AlgorithmId}-analysis_data.json");
            File.WriteAllText(filePath, JsonConvert.SerializeObject(_results, Formatting.Indented));
        }

        public void PrintBalance()
        {
            var portfolio = Portfolio;
            Console.WriteLine("Equity: {0:C}, Margin Used/Remaining: {1:C}/{2:C}", portfolio.TotalPortfolioValue, portfolio.TotalMarginUsed, portfolio.MarginRemaining);
        }
    }
}