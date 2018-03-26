using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Opc.Ua;

namespace MqttSamplePublisher
{
    public class SubscriptionSettings
    {
        public string DataSetWriterId { get; set; }

        public string DataSetClassId { get; set; }

        public uint PublishingInterval { get; set; }

        public ConfigurationVersionDataType MetaDataVersion { get; set; }

        public DataSetFieldContentMask FieldContentMask { get; set; }

        public IList<MonitoredItemSettings> MonitoredItems { get; set; }

        public void Encode(ServiceMessageContext context, bool useReversibleEncoding, StreamWriter writer)
        {
            using (JsonEncoder encoder = new JsonEncoder(context, writer, useReversibleEncoding, false))
            {
                encoder.WriteString("DataSetWriterId", DataSetWriterId);
                encoder.WriteString("DataSetClassId", DataSetClassId);
                encoder.WriteUInt32("PublishingInterval", PublishingInterval);
                encoder.WriteEncodeable("ConfigurationVersion", MetaDataVersion, typeof(ConfigurationVersionDataType));
                encoder.WriteEnumerated("FieldContentMask", FieldContentMask);
                encoder.WriteEncodeableArray("MonitoredItems", (IList<IEncodeable>)MonitoredItems, typeof(MonitoredItemSettings));

                encoder.Close();
            }
        }

        public static async Task<SubscriptionSettings> Decode(ServiceMessageContext context, StreamReader reader)
        {
            var settings = new SubscriptionSettings();

            var json = await reader.ReadToEndAsync();

            using (var decoder = new JsonDecoder(json, context))
            {
                settings.DataSetWriterId = decoder.ReadString("DataSetWriterId");
                settings.DataSetClassId = decoder.ReadString("DataSetClassId");
                settings.PublishingInterval = decoder.ReadUInt32("PublishingInterval");
                settings.MetaDataVersion = (ConfigurationVersionDataType)decoder.ReadEncodeable("ConfigurationVersion", typeof(ConfigurationVersionDataType));
                settings.FieldContentMask = (DataSetFieldContentMask)decoder.ReadEnumerated("FieldContentMask", typeof(DataSetFieldContentMask));
                settings.MonitoredItems = (IList<MonitoredItemSettings>)decoder.ReadEncodeableArray("MonitoredItems", typeof(MonitoredItemSettings));

                decoder.Close();
            }

            return settings;
        }
    }

    public class MonitoredItemSettings : IEncodeable
    {
        public string Name { get; set; }

        public NodeId NodeId { get; set; }

        public uint AttributeId { get; set; }

        public uint SamplingInterval { get; set; }

        public ExpandedNodeId TypeId { get; set; }

        public ExpandedNodeId BinaryEncodingId { get; set; }

        public ExpandedNodeId XmlEncodingId { get; set; }

        public void Decode(IDecoder decoder)
        {
            NodeId = decoder.ReadNodeId("NodeId");
            AttributeId = decoder.ReadUInt32("AttributeId");
            SamplingInterval = decoder.ReadUInt32("SamplingInterval");
        }

        public void Encode(IEncoder encoder)
        {
            encoder.WriteNodeId("NodeId", NodeId);
            encoder.WriteUInt32("AttributeId", AttributeId);
            encoder.WriteUInt32("SamplingInterval", SamplingInterval);
        }

        public bool IsEqual(IEncodeable encodeable)
        {
            return Object.ReferenceEquals(this, encodeable);            
        }
    }
}