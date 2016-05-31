using Newtonsoft.Json;
using OpcUaWebTelemetry.JsonDataTypes;

namespace OpcUaWebTelemetry.JsonDataTypes
{

    public class MonitoredItem
    {

        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Uri")]
        public string Uri { get; set; }
    }

    public class Values
    {

        [JsonProperty("Value")]
        public string Value { get; set; }

        [JsonProperty("SourceTimestamp")]
        public string SourceTimestamp { get; set; }

        [JsonProperty("ServerTimestamp")]
        public string ServerTimestamp { get; set; }
    }

}

namespace OpcUaWebTelemetry.JsonData
{

    public class data
    {
        [JsonProperty("MonitoredItem")]
        public MonitoredItem MonitoredItem { get; set; }

        [JsonProperty("ClientHandle")]
        public int ClientHandle { get; set; }

        [JsonProperty("Value")]
        public Values Value { get; set; }
    }

}
