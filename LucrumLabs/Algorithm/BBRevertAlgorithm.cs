using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using LucrumLabs.Alpha;
using LucrumLabs.Portfolio;
using LucrumLabs.Trades;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Brokerages;
using QuantConnect.Orders;

namespace LucrumLabs.Algorithm
{
    public class BBRevertAlgorithm : QCAlgorithm
    {
        private readonly TimeSpan TradingTimeFrame = TimeSpan.FromHours(1);
        
        private List<Symbol> _symbols = new List<Symbol>();

        private ConcurrentBag<IOrderEventHandler> _orderEventHandlers = new ConcurrentBag<IOrderEventHandler>();
        public override void Initialize()
        {
            SetTradeBuilder(new ManualTradeBuilder());
            
            SetBrokerageModel(BrokerageName.OandaBrokerage);
            SetStartDate(2020, 3, 1);  //Set Start Date
            SetEndDate(2020, 4, 1);    //Set End Date
            SetCash(100000);
            //SetTimeZone(DateTimeZone.Utc);
            // data is loaded in NYC time

            UniverseSettings.Resolution = Resolution.Minute;
            var eurusd = QuantConnect.Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda);
            _symbols.Add(eurusd);
            SetUniverseSelection(new ManualUniverseSelectionModel(_symbols));

            var calendar = AlgoUtils.NewYorkClosePeriod(TimeZone, TradingTimeFrame);
            AddAlpha(new StdDevAlphaModel(calendar, 20, 2m));

            var entryExit = new EntryExitPortfolioModel(calendar);
            // PortfolioConstrucitonModel not optional
            SetPortfolioConstruction(entryExit);
            // Risk model optional
            //SetRiskManagement(new TestRiskModel());
            //SetExecution(new ImmediateExecutionModel());
            SetExecution(new NullExecutionModel());
            
            _orderEventHandlers.Add(entryExit);
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            foreach (var handler in _orderEventHandlers)
            {
                handler.OnOrderEvent(orderEvent);
            }
        }
    }
}
