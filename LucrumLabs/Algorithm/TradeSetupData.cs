using System;

namespace LucrumLabs.Algorithm
{
    public class TradeSetupData
    {
        public DateTime BarTime;
        
        public string symbol;

        public string direction;

        public decimal entryPrice;
        public decimal slPrice;
        public decimal tpPrice;

        public DateTime entryTime;
        public decimal fillPrice;
        public DateTime closeTime;
        public bool canceled;

        public decimal slPips;
        public decimal tpPips;
        public decimal plPips;

        /// <summary>
        /// If trade was completed, index of the closed trade data
        /// </summary>
        public int tradeIndex;
    }
}