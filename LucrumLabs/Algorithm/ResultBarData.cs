using System;
using Newtonsoft.Json;
using NodaTime;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Forex;
using QuantConnect.Util;

namespace LucrumLabs.Algorithm
{
    public class ResultBarData
    {
        public string Symbol;
        public DateTime Time { get; private set; }
        
        [JsonProperty(PropertyName = "O")]
        public decimal Open { get; private set; }

        [JsonProperty(PropertyName = "H")]
        public decimal High { get; private set; }

        [JsonProperty(PropertyName = "L")]
        public decimal Low{ get; private set; }

        [JsonProperty(PropertyName = "C")]
        public decimal Close { get; private set; }
        
        public decimal spread { get; private set; }

        public ResultBarData(Forex forex, QuoteBar bar, DateTimeZone tz)
        {
            Symbol = bar.Symbol;
            Open = bar.Open.SmartRounding();
            High = bar.High.SmartRounding();
            Low = bar.Low.SmartRounding();
            Close = bar.Close.SmartRounding();
            spread = bar.GetSpreadPips(forex);
            Time = bar.Time.ConvertToUtc(tz);
        }
    }
}