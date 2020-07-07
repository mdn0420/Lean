using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Framework.Selection;

namespace LucrumLabs
{
    public class TestAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2010, 01, 01);
            SetEndDate(2011, 12, 31);
            
            SetCash(100000);

            UniverseSettings.Resolution = Resolution.Minute;
            var symbols = new [] { QuantConnect.Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda) };
            AddUniverseSelection(new ManualUniverseSelectionModel(symbols));
            
            AddAlpha(new EmaCrossAlphaModel(12, 26, Resolution.Daily));
            
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            SetExecution(new ImmediateExecutionModel());
            SetRiskManagement(new MaximumDrawdownPercentPortfolio());
        }

    }
}