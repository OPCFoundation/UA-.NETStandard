using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Opc.Ua;

namespace MqttSamplePublisher
{
    public class BrokerSettings
    {
        public string Broker { get; set; }

        public string Topic { get; set; }

        public string ClientId { get; set; }

        public bool UseTls { get; set; }

        public BrokerCredentialType CredentialType { get; set; }

        public string CredentialName { get; set; }

        public string CredentialSecret { get; set; }

        public void Encode(ServiceMessageContext context, bool useReversibleEncoding, StreamWriter writer)
        {
            using (JsonEncoder encoder = new JsonEncoder(context, writer, useReversibleEncoding, false))
            {
                encoder.WriteString("Broker", Broker);
                encoder.WriteString("Topic", Topic);
                encoder.WriteString("ClientId", ClientId);
                encoder.WriteBoolean("UseTls", UseTls);
                encoder.WriteEnumerated("CredentialType", CredentialType);
                encoder.WriteString("CredentialName", CredentialName);
                encoder.WriteString("CredentialSecret", CredentialSecret);

                encoder.Close();
            }
        }

        public static async Task<BrokerSettings> Decode(ServiceMessageContext context, StreamReader reader)
        {
            var json = await reader.ReadToEndAsync();

            BrokerSettings output = new BrokerSettings();

            using (JsonDecoder decoder = new JsonDecoder(json, context))
            {
                output.Broker = decoder.ReadString("Broker");
                output.Topic = decoder.ReadString("Topic");
                output.ClientId = decoder.ReadString("ClientId");
                output.UseTls = decoder.ReadBoolean("UseTls");
                output.CredentialType = (BrokerCredentialType)decoder.ReadEnumerated("CredentialType", typeof(BrokerCredentialType));
                output.CredentialName = decoder.ReadString("CredentialName");
                output.CredentialSecret = decoder.ReadString("CredentialSecret");

                decoder.Close();
            }

            return output;
        }
    }
    public enum BrokerCredentialType
    {
        UserName = 1,
        Certificate = 2,
        AzureSharedAccessKey = 3
    }
}
