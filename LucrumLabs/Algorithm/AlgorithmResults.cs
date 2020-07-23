using System.Collections.Generic;

namespace LucrumLabs.Algorithm
{
    public class AlgorithmResults
    {
        public List<ResultBarData> BarData = new List<ResultBarData>(1024);

        public List<TradeSetupData> TradeSetups = new List<TradeSetupData>();
    }
}