using System;
using QuantConnect.Statistics;

namespace LucrumLabs.Trades
{
    public class AdvancedTrade : Trade
    {
        /// <summary>
        /// The date and time the trade was opened
        /// </summary>
        public DateTime OpenTime { get; set; }
        
        public decimal StopLossPrice { get; set; }
        
        public decimal TakeProfitPrice { get; set; }
    }
}
