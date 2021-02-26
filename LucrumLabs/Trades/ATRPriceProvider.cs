using System;
using System.Diagnostics;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data.Consolidators;
using QuantConnect.Indicators;
using QuantConnect.Orders;

namespace LucrumLabs.Trades
{
    public class ATRPriceProvider : IDisposable
    {
        public Symbol Symbol { get;  }
        private QCAlgorithm _algorithm;
        private IDataConsolidator _consolidator;

        public AverageTrueRange ATR
        {
           get;
        }

        private decimal _slStep;
        private decimal _tpStep;
        
        public ATRPriceProvider(QCAlgorithm algorithm, Symbol symbol, Func<DateTime, CalendarInfo> timeFrame, decimal slStep, decimal tpStep, int atrPeriod)
        {
            _consolidator = new QuoteBarConsolidator(timeFrame);
            ATR = new AverageTrueRange(atrPeriod, MovingAverageType.Simple);
            _slStep = slStep;
            _tpStep = tpStep;
            _algorithm = algorithm;
            Symbol = symbol;
            
            _algorithm.RegisterIndicator(symbol, ATR, _consolidator);
        }

        public void CalculatePrices(out decimal entryPrice, out decimal slPrice, out decimal tpPrice, OrderDirection direction)
        {
            Debug.Assert(ATR.IsReady, "ATR indicator not ready yet");
            decimal currentPrice = _algorithm.Securities[Symbol].Price;
            int sign = direction == OrderDirection.Buy ? 1 : -1;
            decimal atr = ATR;

            entryPrice = currentPrice;
            slPrice = currentPrice - (sign * _slStep * atr);
            tpPrice = currentPrice + (sign * _tpStep * atr);
        }

        public void Dispose()
        {
            if (_algorithm != null)
            {
                _algorithm.SubscriptionManager.RemoveConsolidator(Symbol, _consolidator);
            }
        }
    }
}
