//#define DEBUG_PRINT_BARS

using System;
using System.Collections.Generic;
using System.Linq;
using LucrumLabs.Data;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Algorithm
{
    public class BBRevertAlgorithm : QCAlgorithm
    {
        private const decimal SetupRangeMinAtrRatio = 1m;

        private const decimal SetupWickRatioMax = 0.25m;
        
        protected TimeSpan TradingTimeFrame = TimeSpan.FromHours(4);

        protected readonly string[] PAIRS = ForexPairs.MAJORS_28;
        
        protected Dictionary<Symbol, RollingWindow<QuoteBar>> _setupWindow = new Dictionary<Symbol, RollingWindow<QuoteBar>>();
        protected Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbUpperWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();
        protected Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbLowerWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();
        protected Dictionary<Symbol, RollingWindow<IndicatorDataPoint>> _bbMidWindow = new Dictionary<Symbol, RollingWindow<IndicatorDataPoint>>();
        
        protected Dictionary<Symbol, ParallaxTradeSetup> _activeTrades = new Dictionary<Symbol, ParallaxTradeSetup>();
        protected List<Symbol> _activeSymbols = new List<Symbol>();
        
        protected Dictionary<Symbol, Stochastic> _stochastics = new Dictionary<Symbol, Stochastic>();
        protected Dictionary<Symbol, BollingerBands> _bollingerBands = new Dictionary<Symbol, BollingerBands>();
        protected Dictionary<Symbol, AverageTrueRange> _atrs = new Dictionary<Symbol, AverageTrueRange>();
        
        protected AlgorithmResults _results = new AlgorithmResults();
        
        protected ParallaxTradeSettings _tradeSettings;
        
        public override void Initialize()
        {
            SetupDates();
            SetCash(100000);
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);


            foreach (var symbol in PAIRS)
            {
                SetupPair(symbol);
            }

            _tradeSettings = GetTradeSettings();
        }
        
        protected virtual ParallaxTradeSettings GetTradeSettings()
        {
            var result = new ParallaxTradeSettings()
            {
                ScaleIn = false,
                Entry1Fib = 0.33m,
                Sl1Fib = 1m,
                Tp1Fib = -1m
            };
            return result;
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
        
        protected virtual void SetupDates()
        {
            SetStartDate(2019, 1, 1);
            SetEndDate(2019, 12, 31);
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
            //debugStr += string.Format("BBMid:{0:F5} Up: {1:F5} Low: {2:F5}", _bb.MiddleBand, _bb.UpperBand, _bb.LowerBand);
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

            var data = new ParallaxResultBarData(Securities[symbol] as Forex, bar, TimeZone)
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

        protected virtual void CheckSetup(Symbol symbol)
        {
            var window = _setupWindow[symbol];
            if (!window.IsReady)
            {
                return;
            }
            
            var prevBar = window[1];
            var thisBar = window[0];

            // Check bar size relative to ATR
            var range = AverageTrueRange.ComputeTrueRange(prevBar, thisBar);
            var atr = _atrs[symbol];
            if (range < atr * SetupRangeMinAtrRatio)
            {
                return;
            }
            
            // todo: check wick ratio

            var bbUpper = _bbUpperWindow[symbol][0].Value;
            var bbLower = _bbLowerWindow[symbol][0].Value;
            var stochK = _stochastics[symbol].StochK.Current.Value;
            var stochD = _stochastics[symbol].StochD.Current.Value;

            const decimal stochOverbought = 80m;
            const decimal stochOversold = 20m;

            var barRatios = thisBar.GetBarRatios();

            bool isLongSetup = thisBar.Open < bbLower && thisBar.Close > bbLower &&
                               stochK <= stochOversold && stochD <= stochOversold &&
                               barRatios.Top < SetupWickRatioMax && 
                               thisBar.High > prevBar.High;
            bool isShortSetup = thisBar.Open > bbUpper && thisBar.Close < bbUpper &&
                                stochK >= stochOverbought && stochD >= stochOverbought && 
                                barRatios.Bottom < SetupWickRatioMax && 
                                thisBar.Low < prevBar.Low;

            if (isLongSetup)
            {
                Console.WriteLine("{0} {1} - Found long setup, Range: {2}, ATR: {3}", thisBar.Time, symbol, range, atr);
                PrintBarInfo(thisBar);
                TryOpenTrade(prevBar, thisBar, OrderDirection.Buy);
            } 
            else if (isShortSetup)
            {
                Console.WriteLine("{0} {1} - Found short setup Range: {2}, ATR: {3}", thisBar.Time, symbol, range, atr);
                PrintBarInfo(thisBar);
                TryOpenTrade(prevBar, thisBar, OrderDirection.Sell);
            }
        }

        private void PrintBarInfo(QuoteBar bar)
        {
            string debugStr = string.Format(
                "Bar info: {0} - O:{1:F5} H:{2:F5} L:{3:F5} C:{4:F5} ",
                bar.Time.ToString(),
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close
            );
            //debugStr += string.Format("BBMid:{0:F5} Up: {1:F5} Low: {2:F5}", _bb.MiddleBand, _bb.UpperBand, _bb.LowerBand);
            Console.WriteLine(debugStr);
        }
        
        protected void TryOpenTrade(QuoteBar ibar, QuoteBar setupBar, OrderDirection direction)
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
                    direction,
                    _tradeSettings
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

            if (orderEvent.Status == OrderStatus.Filled)
            {
                PrintBalance();
            }
            
            CleanupTrades();
        }
        
        public void PrintBalance()
        {
            var portfolio = Portfolio;
            Console.WriteLine("Equity: {0:C}, Margin Used/Remaining: {1:C}/{2:C}", portfolio.TotalPortfolioValue, portfolio.TotalMarginUsed, portfolio.MarginRemaining);
        }
    }
}