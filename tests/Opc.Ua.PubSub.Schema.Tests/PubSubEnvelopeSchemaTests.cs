/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.PubSub.Schema.Tests
{
    [TestFixture]
    public class PubSubEnvelopeSchemaTests
    {
        [Test]
        public void CreateDataSetMessageSchemaHonorsHeaderMask()
        {
            var provider = new PubSubSchemaProvider();
            const JsonDataSetMessageContentMask mask = JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.SequenceNumber;

            JsonObject root = CreateDataSetMessageRoot(provider, mask);
            JsonObject properties = root["properties"]!.AsObject();
            JsonObject payload = properties["Payload"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(properties["DataSetWriterId"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(properties["Timestamp"]!["format"]!.GetValue<string>(), Is.EqualTo("date-time"));
                Assert.That(properties["SequenceNumber"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(properties["MessageType"]!["enum"]!.AsArray(), Has.Count.EqualTo(2));
                Assert.That(payload["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(payload["properties"]!["Temperature"], Is.Not.Null);
                Assert.That(payload["properties"]!["Enabled"], Is.Not.Null);
            });
        }

        [Test]
        public void CreateDataSetMessageSchemaWithNoMaskContainsPayloadAndMessageType()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateDataSetMessageRoot(provider, JsonDataSetMessageContentMask.None);
            JsonObject properties = root["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(properties.ContainsKey("Payload"), Is.True);
                Assert.That(properties.ContainsKey("MessageType"), Is.True);
                Assert.That(properties.ContainsKey("DataSetWriterId"), Is.False);
                Assert.That(properties.ContainsKey("Timestamp"), Is.False);
                Assert.That(properties, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void CreateNetworkMessageSchemaHonorsEnvelopeMask()
        {
            var provider = new PubSubSchemaProvider();
            const JsonNetworkMessageContentMask mask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetClassId;

            JsonObject root = CreateNetworkMessageRoot(provider, mask);
            JsonObject properties = root["properties"]!.AsObject();
            JsonObject messages = properties["Messages"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(properties["MessageType"]!["const"]!.GetValue<string>(), Is.EqualTo("ua-data"));
                Assert.That(messages["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(messages["items"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/DataSetMessage"));
                Assert.That(properties.ContainsKey("PublisherId"), Is.True);
                Assert.That(properties.ContainsKey("DataSetClassId"), Is.True);
                Assert.That(properties.ContainsKey("ReplyTo"), Is.False);
            });
        }

        [Test]
        public void CreateNetworkMessageSchemaWithSingleDataSetMessageUsesObjectMessages()
        {
            var provider = new PubSubSchemaProvider();
            const JsonNetworkMessageContentMask mask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage;

            JsonObject root = CreateNetworkMessageRoot(provider, mask);
            JsonObject messages = root["properties"]!["Messages"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(messages["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(messages["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/DataSetMessage"));
            });
        }

        [Test]
        public void CreateMetaDataMessageSchemaContainsMetaDataEnvelope()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateMetaDataMessageRoot(provider);
            JsonObject properties = root["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(properties["MessageType"]!["const"]!.GetValue<string>(), Is.EqualTo("ua-metadata"));
                Assert.That(properties["MetaData"]!["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(properties.ContainsKey("PublisherId"), Is.True);
                Assert.That(properties.ContainsKey("DataSetWriterId"), Is.True);
            });
        }

        [Test]
        public void EnvelopeSchemasParseAndDeclareDraft202012()
        {
            var provider = new PubSubSchemaProvider();

            string dataSetMessage = provider.CreateDataSetMessageSchema(
                CreateMetaData(),
                JsonDataSetMessageContentMask.None,
                DataSetFieldContentMask.None).ToSchemaString();
            string networkMessage = provider.CreateNetworkMessageSchema(
                CreateMetaData(),
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                JsonDataSetMessageContentMask.None,
                DataSetFieldContentMask.None).ToSchemaString();
            string metaDataMessage = provider.CreateMetaDataMessageSchema(CreateMetaData()).ToSchemaString();

            Assert.Multiple(() =>
            {
                AssertDialect(dataSetMessage);
                AssertDialect(networkMessage);
                AssertDialect(metaDataMessage);
            });
        }

        private static void AssertDialect(string schema)
        {
            JsonObject root = JsonNode.Parse(schema)!.AsObject();
            Assert.That(root["$schema"]!.GetValue<string>(), Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
        }

        private static JsonObject CreateDataSetMessageRoot(
            PubSubSchemaProvider provider,
            JsonDataSetMessageContentMask mask)
        {
            var document = (JsonSchemaDocument)provider.CreateDataSetMessageSchema(
                CreateMetaData(),
                mask,
                DataSetFieldContentMask.RawData);
            return document.Root;
        }

        private static JsonObject CreateNetworkMessageRoot(
            PubSubSchemaProvider provider,
            JsonNetworkMessageContentMask mask)
        {
            var document = (JsonSchemaDocument)provider.CreateNetworkMessageSchema(
                CreateMetaData(),
                mask,
                JsonDataSetMessageContentMask.DataSetWriterId,
                DataSetFieldContentMask.RawData);
            return document.Root;
        }

        private static JsonObject CreateMetaDataMessageRoot(PubSubSchemaProvider provider)
        {
            var document = (JsonSchemaDocument)provider.CreateMetaDataMessageSchema(CreateMetaData());
            return document.Root;
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "TelemetryEnvelope",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Enabled",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }
    }
}
