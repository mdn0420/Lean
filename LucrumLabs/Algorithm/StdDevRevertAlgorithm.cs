using System;
using System.Collections.Generic;
using System.Linq;
using LucrumLabs.Data;
using LucrumLabs.Trades;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Forex;

namespace LucrumLabs.Algorithm
{
    /// <summary>
    /// Fade when instrument closes outside a certain Std deviation
    /// </summary>
    public class StdDevRevertAlgorithm : QCAlgorithm
    {
        protected TimeSpan TradingTimeFrame = TimeSpan.FromMinutes(15);

        protected readonly string[] PAIRS = new[] {"AUDCAD"};
        
        protected Dictionary<Symbol, RollingWindow<QuoteBar>> _setupWindow = new Dictionary<Symbol, RollingWindow<QuoteBar>>();
        protected Dictionary<Symbol, BollingerBands> _bollingerBands = new Dictionary<Symbol, BollingerBands>();
        protected Dictionary<Symbol, AverageTrueRange> _atrs = new Dictionary<Symbol, AverageTrueRange>();
        
        protected List<Symbol> _activeSymbols = new List<Symbol>();
        protected Dictionary<Symbol, ManagedTrade> _activeTrades = new Dictionary<Symbol, ManagedTrade>();
        
        public override void Initialize()
        {
            SetupDates();
            SetCash(100000);
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);


            foreach (var symbol in PAIRS)
            {
                SetupPair(symbol);
            }

            //_tradeSettings = GetTradeSettings();
        }
        
        protected virtual void SetupDates()
        {
            SetStartDate(2019, 1, 1);
            SetEndDate(2019, 12, 31);
        }
        
        private void SetupPair(string ticker)
        {
            var consolidator = new SmoothQuoteBarConsolidator(AlgoUtils.NewYorkClosePeriod(TimeZone, TradingTimeFrame));
            var fx = AddForex(ticker, Resolution.Minute, Market.Oanda);
            var symbol = fx.Symbol;
            Securities[symbol].SetLeverage(50m);
            
            SubscriptionManager.AddConsolidator(fx.Symbol, consolidator);
            
            //var stoch = _stochastics[symbol] = new Stochastic(14, 3, 3);
            var bb = _bollingerBands[symbol] = new BollingerBands(20, 2.5m);
            var atr = _atrs[symbol] = new AverageTrueRange(14, MovingAverageType.Simple);
            //RegisterIndicator(symbol, stoch, consolidator);
            RegisterIndicator(symbol, bb, consolidator);
            RegisterIndicator(symbol, atr, consolidator);
            
            
            _setupWindow[symbol] = new RollingWindow<QuoteBar>(2);
            /*
            _bbMidWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);
            _bbUpperWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);
            _bbLowerWindow[symbol] = new RollingWindow<IndicatorDataPoint>(2);*/
            
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
            //debugStr += string.Format("BBMid:{0:F5} Up: {1:F5} Low: {2:F5}", _bb.MiddleBand, _bb.UpperBand, _bb.LowerBand);
            Console.WriteLine(debugStr);
#endif

            var symbol = bar.Symbol;
            var bb = _bollingerBands[symbol];
            //var stoch = _stochastics[symbol];
            var atr = _atrs[symbol];
            _setupWindow[symbol].Add(bar);
            
            if (!bb.IsReady || !atr.IsReady)
            {
                return;
            }

            var data = new ResultBarData(Securities[symbol] as Forex, bar, TimeZone)
            {
                BBMid = bb.MiddleBand,
                BBUpper = bb.UpperBand,
                BBLower = bb.LowerBand,
                atrPips = Math.Round(atr / ForexUtils.GetPipSize(Securities[symbol] as Forex), 1)
            };
            //_results.BarData.Add(data);
            
            
            
            /*
            _bbUpperWindow[symbol].Add(bb.UpperBand.Current);
            _bbLowerWindow[symbol].Add(bb.LowerBand.Current);
            _bbMidWindow[symbol].Add(bb.MiddleBand.Current);*/

            if (_setupWindow[symbol].IsReady)
            {
                CheckSetup(symbol);
            }
        }

        private void CheckSetup(Symbol symbol)
        {
            var bb = _bollingerBands[symbol];
            var atr = _atrs[symbol];
            var window = _setupWindow[symbol];

            var thisBar = window[0];
            if (thisBar.Close < bb.LowerBand)
            {
                // long setup
                //Console.Write("LONG SIGNAL: ");
                PrintBarInfo(thisBar);
                TryOpenTrade(thisBar, OrderDirection.Buy, atr);
            } 
            else if (thisBar.Close > bb.UpperBand)
            {
                //Console.Write("SHORT SIGNAL: ");
                PrintBarInfo(thisBar);
                TryOpenTrade(thisBar, OrderDirection.Sell, atr);
            }
        }

        private void TryOpenTrade(QuoteBar bar, OrderDirection direction, decimal atr)
        {
            var symbol = bar.Symbol;
            if (!_activeTrades.ContainsKey(symbol))
            {
                decimal entryPrice = bar.Close; // todo: use ask/bid price instead?
                decimal slPrice;
                decimal tpPrice;
                if (direction == OrderDirection.Buy)
                {
                    slPrice = bar.Low - atr;
                    tpPrice = entryPrice + ((entryPrice - slPrice) * 1.2m);
                }
                else
                {
                    slPrice = bar.High + atr;
                    tpPrice = entryPrice - ((slPrice - entryPrice) * 1.2m);
                }
                
                Forex pair = this.Securities[symbol] as Forex;
                decimal pipSize = ForexUtils.GetPipSize(pair);
                var riskPips = Math.Abs(entryPrice - slPrice) / pipSize;
                int quantity = ForexUtils.CalculatePositionSize(pair, riskPips, this.Portfolio.MarginRemaining, 0.0025m);
                if (direction == OrderDirection.Sell)
                {
                    quantity = -quantity;
                }
                var trade = new CalculatedTrade(this, symbol, direction, OrderType.Market, entryPrice, slPrice, tpPrice, quantity);
                
                //Console.WriteLine("{0} - Setting up trade - Entry:{1}, SL: {2}, TP: {3}, Qty: {4}", this.Time, entryPrice, slPrice, tpPrice, quantity);
                _activeTrades[symbol] = trade;
                _activeSymbols.Add(symbol);
                trade.Execute();
                
            }
            else
            {
                Console.WriteLine("{0} - We currently have a pending trade.. bailing on {1} idea", bar.Time, symbol);
            }
        }
        
        public override void OnData(Slice slice)
        {
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
                // for market orders, this line hits before the entry order is created so the trade doesn't receive the event
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
        
        private void CleanupTrades()
        {
            List<Symbol> remove = new List<Symbol>();
            foreach (var kvp in _activeTrades)
            {
                if (kvp.Value.State == ManagedTrade.TradeState.CLOSED)
                {
                    remove.Add(kvp.Key);
                }
            }

            foreach (var symbol in remove)
            {
                //var trade = _activeTrades[symbol];
                //var stats = trade.GetStats();
                //_results.TradeSetups.AddRange(stats);
                _activeTrades.Remove(symbol);
                _activeSymbols.Remove(symbol);
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
            debugStr += string.Format("ATR:{0:F5}", _atrs[bar.Symbol]);
            //debugStr += string.Format("BBMid:{0:F5} Up: {1:F5} Low: {2:F5}", _bb.MiddleBand, _bb.UpperBand, _bb.LowerBand);
            Console.WriteLine(debugStr);
        }
        
        public void PrintBalance()
        {
            var portfolio = Portfolio;
            Console.WriteLine("Equity: {0:C}, Margin Used/Remaining: {1:C}/{2:C}", portfolio.TotalPortfolioValue, portfolio.TotalMarginUsed, portfolio.MarginRemaining);
            /*
            foreach (var symbol in PAIRS)
            {
                Console.WriteLine(" {0} - {1}", symbol, portfolio.GetHoldingsQuantity(symbol));
            }*/
        }
    }
}