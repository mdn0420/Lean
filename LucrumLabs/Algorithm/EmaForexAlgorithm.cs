using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace LucrumLabs.Algorithm
{
    /// <summary>
    /// 1. Use EmaCross
    /// 2. Calculate lot sizes based on risk/pip values
    ///     - Stop loss price based on ATR
    /// 3. Implement stop losses and take profits
    /// </summary>
    public class EmaForexAlgorithm : QCAlgorithm
    {
        private MovingAverageConvergenceDivergence _macd;
        private Chart _tradePlot;
        private Series _priceSeries;
        
        public override void Initialize()
        {
            SetStartDate(2010, 01, 05);
            SetEndDate(2010, 01, 06);
            
            SetCash(100000);

            
            AddForex("EURUSD", Resolution.Minute, Market.Oanda);


            //UniverseSettings.Resolution = Resolution.Minute;
            //var symbols = new [] { QuantConnect.Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda) };
            //AddUniverseSelection(new ManualUniverseSelectionModel(symbols));
            
            //AddAlpha(new EmaCrossAlphaModel());
            
            //SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            //SetExecution(new ImmediateExecutionModel());
            //SetRiskManagement(new NullRiskManagementModel());
            
            _macd = MACD("EURUSD", 12, 26,9, MovingAverageType.Exponential, Resolution.Daily);
            
            _tradePlot = new Chart("Trade Plot");
            _priceSeries = new Series("Price", SeriesType.Line, "$");
            _tradePlot.AddSeries(new Series("MACD", SeriesType.Line));
            _tradePlot.AddSeries(_priceSeries);
        }

        private int dataCount = 0;

        public override void OnData(Slice slice)
        {
            //Log(slice.Time.ToString());
            var quote = slice.QuoteBars["EURUSD"];
            if (dataCount < 3)
            {
                Log(quote.ToString());
                //Log(quote.ToString());
            }

            Plot("Trade Plot", "MACD", _macd.Signal);
            
            Plot("Trade Plot", "Price", quote.Close);
            
            /*
            Plot("Trade Plot", "Price", quote.Open);
            Plot("Trade Plot", "Price", quote.High);
            Plot("Trade Plot", "Price", quote.Low);
            Plot("Trade Plot", "Price", quote.Close);*/

            dataCount++;
        }
    }
}