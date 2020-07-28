//#define DEBUG_PRINT_BARS

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const decimal IndecisionWickRatioMin = 0.05m;

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

        private TimeSpan TradingTimeFrame = TimeSpan.FromHours(24);

        private readonly string[] PAIRS = ForexPairs.MAJORS_28;

        private Dictionary<Symbol, RollingWindow<QuoteBar>> _setupWindow = new Dictionary<Symbol, RollingWindow<QuoteBar>>();
        private Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbUpperWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();
        private Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbLowerWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();
        private Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbMidWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();

        private Dictionary<Symbol, ParallaxTradeSetup> _activeTrades = new Dictionary<Symbol, ParallaxTradeSetup>();
        private List<Symbol> _activeSymbols = new List<Symbol>();

        private AlgorithmResults _results = new AlgorithmResults();
        
        private Dictionary<Symbol, Stochastic> _stochastics = new Dictionary<Symbol, Stochastic>();
        private Dictionary<Symbol, BollingerBands> _bollingerBands = new Dictionary<Symbol, BollingerBands>();
        private Dictionary<Symbol, AverageTrueRange> _atrs = new Dictionary<Symbol, AverageTrueRange>();

        public override void Initialize()
        {
            SetStartDate(2016, 1, 1);
            SetEndDate(2019, 12, 31);
            SetCash(100000);
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);


            foreach (var symbol in PAIRS)
            {
                SetupPair(symbol);
            }
        }

        private void SetupPair(string ticker)
        {
            var consolidator = new SmoothQuoteBarConsolidator(AlgoUtils.NewYorkClosePeriod(TimeZone, TradingTimeFrame));
            var fx = AddForex(ticker, Resolution.Minute, Market.Oanda);
            var symbol = fx.Symbol;
            Securities[symbol].SetLeverage(50m);
            
            SubscriptionManager.AddConsolidator(fx.Symbol, consolidator);
            
            var stoch = _stochastics[symbol] = new Stochastic(14, 3, 3);
            var bb = _bollingerBands[symbol] = new BollingerBands(20, 2);
            var atr = _atrs[symbol] = new AverageTrueRange(20, MovingAverageType.Simple);
            RegisterIndicator(symbol, stoch, consolidator);
            RegisterIndicator(symbol, bb, consolidator);
            RegisterIndicator(symbol, atr, consolidator);
            
            _setupWindow[symbol] = new RollingWindow<QuoteBar>(2);
            _bbMidWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);
            _bbUpperWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);
            _bbLowerWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);
            
            // This needs to get added last so the bar gets processed after indicators are updated
            consolidator.DataConsolidated += OnDataConsolidated;
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

            var symbol = bar.Symbol;
            var bb = _bollingerBands[symbol];
            var stoch = _stochastics[symbol];
            var atr = _atrs[symbol];
            if (!bb.IsReady || !stoch.IsReady || !atr.IsReady)
            {
                return;
            }

            var data = new ResultBarData(Securities[symbol] as Forex, bar, TimeZone)
            {
                BBMid = bb.MiddleBand,
                BBUpper = bb.UpperBand,
                BBLower = bb.LowerBand,
                StochD = stoch.StochD,
                StochK = stoch.StochK,
                atrPips = Math.Round(atr / ForexUtils.GetPipSize(Securities[symbol] as Forex), 1)
            };
            _results.BarData.Add(data);
            
            _setupWindow[symbol].Add(bar);
            _bbUpperWindow[symbol].Add(bb.UpperBand.Current);
            _bbLowerWindow[symbol].Add(bb.LowerBand.Current);
            _bbMidWindow[symbol].Add(bb.MiddleBand.Current);
            CheckSetup(symbol);
        }

        private void CheckSetup(Symbol symbol)
        {
            var window = _setupWindow[symbol];
            if (!window.IsReady)
            {
                return;
            }

            var prevBar = window[1];
            var thisBar = window[0];

            int ibar = IsIndecisionBar(prevBar, 
                _bbUpperWindow[symbol][1].Value, 
                _bbLowerWindow[symbol][1].Value, 
                _bbMidWindow[symbol][1].Value);

            /*
            if (ibar != 0)
            {
                Console.WriteLine("Ibar found {0}", prevBar.EndTime);
            }*/

            var ibarBodyLength = prevBar.GetBodyTop() - prevBar.GetBodyBottom();
            var setupBodyLength = thisBar.GetBodyTop() - thisBar.GetBodyBottom();

            var atr = _atrs[symbol];
            if (setupBodyLength < _atrs[symbol] * SetupBarLengthATRScale)
            {
                return;
            }

            var setupRatios = thisBar.GetBarRatios();
            if (ibar != 0 && setupRatios.Body > SetupBodyRatioMin && setupBodyLength > ibarBodyLength)
            {
                var stochK = _stochastics[symbol].StochK.Current.Value;
                var stochD = _stochastics[symbol].StochD.Current.Value;

                var maxSetupRetrace = FibRetraceLevels[MaxSetupFibRetracement];
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
                        "{0} {1} LONG setup: H/L: {2}/{3}",
                        thisBar.Time,
                        symbol,
                        thisBar.High,
                        thisBar.Low
                    );
                    TryOpenTrade(prevBar, thisBar,OrderDirection.Buy);
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
                        "{0} {1} SHORT setup: H/L: {2}/{3}",
                        thisBar.Time,
                        symbol,
                        thisBar.High,
                        thisBar.Low
                    );
                    TryOpenTrade(prevBar, thisBar, OrderDirection.Sell);
                }
            }
        }

        private void TryOpenTrade(QuoteBar ibar, QuoteBar setupBar, OrderDirection direction)
        {
            bool canTrade = true;
            var ticker = setupBar.Symbol;

            if (_activeTrades.ContainsKey(ticker))
            {
                // todo: check if trade has been entered yet
                Console.WriteLine("{0} - We currently have a pending trade.. bailing on {1} idea", setupBar.Time, ticker);
                canTrade = false;
                
                // Record for analysis purposes
                TradeSetupData setupData = new TradeSetupData() {
                    BarTime = setupBar.Time.ConvertToUtc(TimeZone),
                    direction = direction.ToString(),
                    slPips = 0,
                    tpPips = 0,
                    plPips = 0,
                    symbol = ticker,
                    tradeIndex = -1
                };
                _results.TradeSetups.Add(setupData);
            }

            if (canTrade)
            {
                ParallaxTradeSetup trade = _activeTrades[ticker] = new ParallaxTradeSetup(
                    this,
                    ibar,
                    setupBar,
                    direction
                );
                _activeSymbols.Add(ticker);
                trade.PlaceOrders();
            }
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
                var stats = trade.GetStats();
                _results.TradeSetups.AddRange(stats);
                _activeTrades.Remove(symbol);
                _activeSymbols.Remove(symbol);
            }
        }

        private int IsIndecisionBar(QuoteBar bar, decimal bbUpper, decimal bbLower, decimal bbMid)
        {
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
            for (int i = _activeSymbols.Count - 1; i >= 0; i--)
            {
                var symbol = _activeSymbols[i];
                if (slice.QuoteBars.TryGetValue(symbol, out bar))
                {
                    _activeTrades[symbol].OnDataUpdate(bar);
                } 
            }

            CleanupTrades();
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            var trades = _activeTrades.Values.ToList();
            foreach (var trade in trades)
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
            decimal pipSize = ForexUtils.GetPipSize(pair);

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