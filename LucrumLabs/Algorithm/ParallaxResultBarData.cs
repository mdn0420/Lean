using Newtonsoft.Json;
using NodaTime;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Forex;
using QuantConnect.Util;

namespace LucrumLabs.Algorithm
{
    public class ParallaxResultBarData : ResultBarData
    {
        [JsonConverter(typeof(JsonRoundingConverter))]
        public decimal BBMid;
        [JsonConverter(typeof(JsonRoundingConverter))]
        public decimal BBUpper;
        [JsonConverter(typeof(JsonRoundingConverter))]
        public decimal BBLower;

        [JsonConverter(typeof(JsonRoundingConverter))]
        public decimal StochK;
        [JsonConverter(typeof(JsonRoundingConverter))]
        public decimal StochD;

        public decimal atrPips;
        
        public ParallaxResultBarData(Forex forex, QuoteBar bar, DateTimeZone tz) : base(forex, bar, tz)
        {
        }
    }
}