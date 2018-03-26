using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Opc.Ua;

namespace MqttSamplePublisher
{
    public class DataSetWriterSettings : DataSetWriterDataType
    {
        public new BrokerDataSetWriterTransportDataType TransportSettings
        { 
            get
            {
                return (BrokerDataSetWriterTransportDataType)ExtensionObject.ToEncodeable(base.TransportSettings);
            }

            set
            {
                base.TransportSettings = new ExtensionObject(value);
            }
        }

        public PublishedDataSetDataType DataSet { get; set; }

        public WriterGroupDataType Group { get; set; }

        public void Encode(ServiceMessageContext context, StreamWriter writer)
        {
            using (JsonEncoder encoder = new JsonEncoder(context, writer, true, false))
            {
                this.Encode(encoder);

                encoder.Close();
            }
        }

        public static async Task<DataSetWriterSettings> Decode(ServiceMessageContext context, StreamReader reader)
        {
            var settings = new DataSetWriterSettings();

            var json = await reader.ReadToEndAsync();

            using (var decoder = new JsonDecoder(json, context))
            {
                var writer = (DataSetWriterDataType)decoder.ReadEncodeable(null, typeof(DataSetWriterDataType));

                settings.Name = writer.Name;
                settings.DataSetName = writer.DataSetName;
                settings.DataSetFieldContentMask = writer.DataSetFieldContentMask;
                settings.KeyFrameCount = writer.KeyFrameCount;
                settings.MessageSettings = writer.MessageSettings;
                settings.TransportSettings = (BrokerDataSetWriterTransportDataType)ExtensionObject.ToEncodeable(writer.TransportSettings);

                decoder.Close();
            }

            return settings;
        }
    }
}