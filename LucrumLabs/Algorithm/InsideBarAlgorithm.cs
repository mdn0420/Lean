using System;
using System.Collections.Generic;
using System.Linq;
using LucrumLabs.Data;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Brokerages;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities;

namespace LucrumLabs.Algorithm
{
    public class InsideBarAlgorithm : QCAlgorithm
    {
        private TimeSpan TradingTimeFrame = TimeSpan.FromHours(4);

        private readonly string[] PAIRS = { "GBPJPY"};

        private Dictionary<Symbol, RollingWindow<QuoteBar>> _setupWindow = new Dictionary<Symbol, RollingWindow<QuoteBar>>();
        
        private Dictionary<Symbol, InsideBarTrade> _activeTrades = new Dictionary<Symbol, InsideBarTrade>();

        public override void Initialize()
        {
            SetStartDate(2020, 1, 1);
            SetEndDate(2020, 3, 31);
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

            
            _setupWindow[symbol] = new RollingWindow<QuoteBar>(2);
            
            // This needs to get added last so the bar gets processed after indicators are updated
            consolidator.DataConsolidated += OnDataConsolidated;
        }

        private void OnDataConsolidated(object sender, QuoteBar bar)
        {
            var symbol = bar.Symbol;
            _setupWindow[symbol].Add(bar);
            
            CheckSetup(symbol);
        }

        private void CheckSetup(Symbol symbol)
        {
            var barWindow = _setupWindow[symbol];
            if (!barWindow.IsReady)
            {
                return;
            }

            var bar1 = barWindow[1];
            var bar2 = barWindow[0];

            bool foundInsideBar = bar2.High < bar1.High && bar2.Low > bar1.Low;
            if (foundInsideBar)
            {
                OrderDirection direction = bar1.Close > bar1.Open ? OrderDirection.Buy : OrderDirection.Sell;
                //Console.WriteLine("{0} - Found {1} {2} setup", bar2.Time, direction, symbol);
                InsideBarTrade trade = null;
                if (_activeTrades.TryGetValue(symbol, out trade))
                {
                    _activeTrades.Remove(symbol);
                    trade.Close();
                }
                
                trade = _activeTrades[symbol] = new InsideBarTrade(this, bar1, bar2, direction);
                trade.Execute();
            }
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
        
        private void CleanupTrades()
        {
            List<Symbol> remove = new List<Symbol>();
            foreach (var kvp in _activeTrades)
            {
                if (kvp.Value.State == InsideBarTrade.TradeState.CLOSED)
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
                //_activeSymbols.Remove(symbol);
            }
        }
        
        public void PrintBalance()
        {
            var portfolio = Portfolio;
            Console.WriteLine("Equity: {0:C}, Margin Used/Remaining: {1:C}/{2:C}", portfolio.TotalPortfolioValue, portfolio.TotalMarginUsed, portfolio.MarginRemaining);
            foreach (var kvp in _activeTrades)
            {
                Console.WriteLine(" {0} - {1}", kvp.Key, portfolio.Securities[kvp.Key].Holdings.Quantity);
            }
        }
    }
}