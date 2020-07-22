using System;
using Python.Runtime;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;

namespace LucrumLabs.Data
{
    public class SmoothQuoteBarConsolidator : QuoteBarConsolidator
    {
        private QuoteBar _lastBar;
        
        public SmoothQuoteBarConsolidator(TimeSpan period) : base(period)
        {
        }

        public SmoothQuoteBarConsolidator(int maxCount) : base(maxCount)
        {
        }

        public SmoothQuoteBarConsolidator(int maxCount, TimeSpan period) : base(maxCount, period)
        {
        }

        public SmoothQuoteBarConsolidator(Func<DateTime, CalendarInfo> func) : base(func)
        {
        }

        public SmoothQuoteBarConsolidator(PyObject pyfuncobj) : base(pyfuncobj)
        {
        }

        protected override void AggregateBar(ref QuoteBar workingBar, QuoteBar data)
        {
            // Check if we're starting a new bar
            bool shouldSmooth = workingBar == null;
            
            base.AggregateBar(ref workingBar, data);

            // If starting new bar, grab the close of the last bar
            if (shouldSmooth && _lastBar != null)
            {
                if (workingBar.Bid != null)
                {
                    var lastClose = _lastBar.Bid.Close;
                    workingBar.Bid.Open = lastClose;
                    if (lastClose > workingBar.Bid.High)
                    {
                        workingBar.Bid.High = lastClose;
                    }

                    if (lastClose < workingBar.Bid.Low)
                    {
                        workingBar.Bid.Low = lastClose;
                    }
                }

                if (workingBar.Ask != null)
                {
                    var lastClose = _lastBar.Ask.Close;
                    workingBar.Ask.Open = lastClose;
                    if (lastClose > workingBar.Ask.High)
                    {
                        workingBar.Ask.High = lastClose;
                    }

                    if (lastClose < workingBar.Ask.Low)
                    {
                        workingBar.Ask.Low = lastClose;
                    }
                }
            }
            _lastBar = workingBar;
        }
    }
}