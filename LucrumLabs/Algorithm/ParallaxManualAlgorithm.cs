using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuantConnect;
using QuantConnect.Orders;

namespace LucrumLabs.Algorithm
{
    public class ManualTradeData
    {
        /// <summary>
        /// Time to setup trade
        /// </summary>
        public DateTime Time;

        public string Symbol;

        public OrderDirection Direction;

        public override string ToString()
        {
            return string.Format("{0} {1} {2}", Time, Symbol, Direction);
        }
    }
    public class ParallaxManualAlgorithm : ParallaxAlgorithm
    {
        private const string TradesFile = "/Users/mnguyen/gitrepos/Lean/Data/trades/minh_trades2.csv";

        private Dictionary<Symbol, List<ManualTradeData>> _manualTrades;
        
        public override void Initialize()
        {
            base.Initialize();
            
            // load trade data

            _manualTrades = LoadTrades(TradesFile);
        }

        protected override void SetupDates()
        {
            SetStartDate(2015, 12, 1);
            SetEndDate(2017, 12, 31);
        }

        protected override ParallaxTradeSettings GetTradeSettings()
        {
            var result = new ParallaxTradeSettings()
            {
                ScaleIn = false,
                ActiveTradeManagement = false,
                Tp1Fib = -1m,
                Sl1Fib = 1m,
                Tp2Fib = -0.618m
            };
            return result;
        }

        protected override void CheckSetup(Symbol symbol)
        {
            var window = _setupWindow[symbol];
            if (!window.IsReady)
            {
                return;
            }
            var ibar = window[1];
            var setupBar = window[0];
            
            // assume sorted by date and same timezone as algorithm
            List<ManualTradeData> trades = null;
            if (_manualTrades.TryGetValue(symbol, out trades))
            {
                var nextTrade = trades.Find(t => t.Symbol == symbol);
                if (nextTrade != null && setupBar.EndTime >= nextTrade.Time)
                {
                    Console.WriteLine("{0} - Setting up {1} trade for {2}", nextTrade.Time, nextTrade.Direction, nextTrade.Symbol);
                    trades.Remove(nextTrade);
                    TryOpenTrade(ibar, setupBar, nextTrade.Direction);
                }
            }
        }

        private Dictionary<Symbol, List<ManualTradeData>> LoadTrades(string path)
        {
            var results = new Dictionary<Symbol, List<ManualTradeData>>();
            using (var reader = new StreamReader(path))
            {
                int count = 0;
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split(',');
                    var symbol = SymbolCache.GetSymbol(values[1]);
                    var dt = DateTime.Parse(values[0], CultureInfo.CurrentCulture, DateTimeStyles.AdjustToUniversal);
                    dt = dt.ConvertFromUtc(TimeZone);
                    var trade = new ManualTradeData()
                    {
                        Time = dt,
                        Symbol = symbol,
                        Direction = values[2] == "Buy" || values[2] == "Long" ? OrderDirection.Buy : OrderDirection.Sell
                    };
                    List<ManualTradeData> tradeList = null;
                    if(!results.TryGetValue(symbol, out tradeList))
                    {
                        results[symbol] = tradeList = new List<ManualTradeData>();
                    }
                    tradeList.Add(trade);
                    count++;
                    Console.WriteLine("Loaded manual trade: {0}", trade);
                }
                Debug(string.Format("Loaded {0} manual trades", count));
            }

            return results;
        }

        public override void OnEndOfAlgorithm()
        {
            base.OnEndOfAlgorithm();

            foreach (var kvp in _manualTrades)
            {
                if (kvp.Value.Count > 0)
                {
                    Error(string.Format("{0} has {1} unprocessed trades", kvp.Key, kvp.Value.Count));
                }
            }
        }
    }
    
    
}