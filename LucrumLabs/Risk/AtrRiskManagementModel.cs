using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;

namespace LucrumLabs.Risk
{
    public class AtrRiskManagementModel : RiskManagementModel
    {
        private PortfolioTargetCollection _targets;
        private Dictionary<Symbol, SymbolData> _symbolData = new Dictionary<Symbol,SymbolData>();

        private Func<DateTime, CalendarInfo> _timeFrame;

        private int _period;

        public AtrRiskManagementModel(Func<DateTime, CalendarInfo> timeFrame, int period)
        {
            _targets = new PortfolioTargetCollection();
            _timeFrame = timeFrame;
            _period = period;
        }
        
        public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
        {
            // see MaximumSectorExposureRiskManagementModel for target collection example
            yield break;
        }

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            // todo: handle removals
            
            var symbols = changes.AddedSecurities.Select(x => x.Symbol);
            foreach (var symbol in symbols)
            {
                if (!_symbolData.ContainsKey(symbol))
                {
                    //Console.WriteLine("StdDevAlphaModel - adding {0}", symbol);
                    _symbolData[symbol] = new SymbolData(algorithm, symbol, _timeFrame, _period);
                }
                else
                {
                    algorithm.Error(string.Format("StdDevAlphaModel received duplicate add security event for {0}", symbol));
                }
            }
        }

        private class SymbolData
        {
            public Symbol Symbol { get; }
            public AverageTrueRange ATR { get; }
            
            private IDataConsolidator _consolidator;
            

            public SymbolData(QCAlgorithm algorithm, Symbol symbol, Func<DateTime, CalendarInfo> timeFrame, int period)
            {
                Symbol = symbol;
                _consolidator = new QuoteBarConsolidator(timeFrame);
                ATR = new AverageTrueRange(period);
                algorithm.RegisterIndicator(symbol, ATR, _consolidator);
            }
        }
    }
}