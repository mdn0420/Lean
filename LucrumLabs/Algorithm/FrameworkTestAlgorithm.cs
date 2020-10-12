using System;
using System.Collections.Generic;
using LucrumLabs.Alpha;
using LucrumLabs.Portfolio;
using LucrumLabs.Risk;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;

namespace LucrumLabs.Algorithm
{
    public class FrameworkTestAlgorithm : QCAlgorithm
    {
        private readonly TimeSpan TradingTimeFrame = TimeSpan.FromHours(4);
        
        private List<Symbol> _symbols = new List<Symbol>();
        
        public override void Initialize()
        {
            SetStartDate(2019, 8, 1);  //Set Start Date
            SetEndDate(2019, 8, 7);    //Set End Date
            SetCash(100000);

            UniverseSettings.Resolution = Resolution.Minute;
            var eurusd = QuantConnect.Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda);
            _symbols.Add(eurusd);
            
            SetUniverseSelection(new ManualUniverseSelectionModel(_symbols));

            var calendar = AlgoUtils.NewYorkClosePeriod(TimeZone, TradingTimeFrame);
            AddAlpha(new StdDevAlphaModel(calendar, 20, 2.5m));
            SetPortfolioConstruction(new TestPortfolioModel());
            SetRiskManagement(new TestRiskModel());
            SetExecution(new ImmediateExecutionModel());
        }
    }
}