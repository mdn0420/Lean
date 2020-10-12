using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Indicators.CandlestickPatterns;

namespace LucrumLabs.Alpha
{
    public class StdDevAlphaModel : AlphaModel
    {
        private Dictionary<Symbol, SymbolData> _symbolData = new Dictionary<Symbol,SymbolData>();

        private Func<DateTime, CalendarInfo> _timeFrame;

        private readonly int _period;
        private readonly decimal _k;

        public StdDevAlphaModel(Func<DateTime, CalendarInfo> timeFrame, int period, decimal k)
        {
            _timeFrame = timeFrame;
            _period = period;
            _k = k;
        }
        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            //Console.WriteLine("StdDevAlphaModel.Update - {0} - {1}", string.Join(",", data.QuoteBars.Keys), data.Time);
            foreach (var kvp in _symbolData)
            {
                var symbol = kvp.Key;
                var symbolData = kvp.Value;
                if (data.Time == symbolData.LastUpdate)
                {
                    if (symbolData.CurrentState == SymbolData.State.LOWER)
                    {
                        Console.WriteLine("{0} - Found long signal", data.Time);
                        yield return Insight.Price(symbol, Resolution.Hour, 5, InsightDirection.Up);
                    }
                    else if (symbolData.CurrentState == SymbolData.State.UPPER)
                    {
                        Console.WriteLine("{0} - Found short signal", data.Time);
                        yield return Insight.Price(symbol, Resolution.Hour, 5, InsightDirection.Down);
                    }
                }
            }
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var removed in changes.RemovedSecurities)
            {
                SymbolData data = null;
                if (_symbolData.TryGetValue(removed.Symbol, out data))
                {
                    data.RemoveConsolidators(algorithm);
                    _symbolData.Remove(removed.Symbol);
                }
            }
            
            var symbols = changes.AddedSecurities.Select(x => x.Symbol);
            foreach (var symbol in symbols)
            {
                if (!_symbolData.ContainsKey(symbol))
                {
                    //Console.WriteLine("StdDevAlphaModel - adding {0}", symbol);
                    _symbolData[symbol] = new SymbolData(algorithm, symbol, _timeFrame, _period, _k);
                }
                else
                {
                    algorithm.Error(string.Format("StdDevAlphaModel received duplicate add security event for {0}", symbol));
                }
            }
            
            // todo: lookback history

            if (changes.RemovedSecurities.Count > 0)
            {
                Console.WriteLine("StdDevAlphaModel removed {0}", string.Join(",", changes.RemovedSecurities));
            }
        }

        private class SymbolData
        {
            public enum State
            {
                INSIDE, // Inside the band
                UPPER, // Outside upper band
                LOWER // Outside lower band
            }
            public Symbol Symbol { get;  }
            private IDataConsolidator _consolidator;

            private BollingerBands _bollingerBands;

            public DateTime LastUpdate { get; private set; }

            public State CurrentState { get; private set; }

            public SymbolData(QCAlgorithm algorithm, Symbol symbol, Func<DateTime, CalendarInfo> timeFrame, int period, decimal k)
            {
                Symbol = symbol;
                LastUpdate = DateTime.MinValue;
                _consolidator = new QuoteBarConsolidator(timeFrame);
                _bollingerBands = new BollingerBands(period, k);
                _bollingerBands.Updated += OnBandsUpdated;
                algorithm.RegisterIndicator(symbol, _bollingerBands, _consolidator);
            }

            private void OnBandsUpdated(object sender, IndicatorDataPoint updated)
            {
                if (!_bollingerBands.IsReady)
                {
                    // todo: warmup indicators with historical data
                    return;
                }
                
                //PrintConsolidatedBar();
                //Console.WriteLine("midBB: {0}, upperBB: {1}, lowerBB: {2}", _bollingerBands.MiddleBand, _bollingerBands.UpperBand, _bollingerBands.LowerBand);

                LastUpdate = updated.Time;

                var price = updated.Price; // same as "close"
                if (price < _bollingerBands.LowerBand)
                {
                    PrintConsolidatedBar();
                    Console.WriteLine("midBB: {0}, upperBB: {1}, lowerBB: {2}", _bollingerBands.MiddleBand, _bollingerBands.UpperBand, _bollingerBands.LowerBand);
                    CurrentState = State.LOWER;
                }
                else if (price > _bollingerBands.UpperBand.Current)
                {
                    PrintConsolidatedBar();
                    Console.WriteLine("midBB: {0}, upperBB: {1}, lowerBB: {2}", _bollingerBands.MiddleBand, _bollingerBands.UpperBand, _bollingerBands.LowerBand);
                    CurrentState = State.UPPER;
                }
                else
                {
                    CurrentState = State.INSIDE;
                }
            }
            
            public void RemoveConsolidators(QCAlgorithm algorithm)
            {
                algorithm.SubscriptionManager.RemoveConsolidator(Symbol, _consolidator);
            }

            private void PrintConsolidatedBar()
            {
                // Get working data because handlers get callled before DataConsolidator.Consolidated is updated
                var consolidated = _consolidator.WorkingData as IBaseDataBar;
                if (consolidated != null)
                {
                    Console.WriteLine(
                        "{5} - {0} - O:{1} H:{2} L:{3} C:{4}",
                        Symbol,
                        consolidated.Open,
                        consolidated.High,
                        consolidated.Low,
                        consolidated.Close,
                        consolidated.Time
                    );
                }
            }
        }
    }
}