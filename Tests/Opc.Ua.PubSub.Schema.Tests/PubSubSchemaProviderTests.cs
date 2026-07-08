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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.PubSub.Schema.Tests
{
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class PubSubSchemaProviderTests
    {
        [Test]
        public void AddSchemaOnPubSubBuilderRegistersSchemaProvider()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddSchema());

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            Assert.That(
                serviceProvider.GetRequiredService<IPubSubSchemaProvider>(),
                Is.TypeOf<PubSubSchemaProvider>());
        }

        [Test]
        public void CreateDataSetSchemaWithRawDataMapsBuiltInFields()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateRoot(provider, DataSetFieldContentMask.RawData);
            JsonObject properties = root["properties"]!.AsObject();
            JsonObject temperature = properties["Temperature"]!.AsObject();
            JsonObject name = properties["Name"]!.AsObject();
            JsonObject counter = properties["Counter"]!.AsObject();
            JsonObject flags = properties["Flags"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(temperature["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(temperature["minimum"]!.GetValue<long>(), Is.EqualTo(int.MinValue));
                Assert.That(name["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(counter["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(counter["pattern"]!.GetValue<string>(), Is.EqualTo("^-?\\d+$"));
                Assert.That(flags["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(flags["items"]!["type"]!.GetValue<string>(), Is.EqualTo("boolean"));
            });
        }

        [Test]
        public void CreateDataSetSchemaWithFieldMaskWrapsDataValueMembers()
        {
            var provider = new PubSubSchemaProvider();
            const DataSetFieldContentMask mask = DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds;

            JsonObject root = CreateRoot(provider, mask);
            JsonObject field = root["properties"]!["Temperature"]!.AsObject();
            JsonObject properties = field["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(field["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(properties.ContainsKey("Value"), Is.True);
                Assert.That(properties.ContainsKey("StatusCode"), Is.True);
                Assert.That(properties.ContainsKey("SourceTimestamp"), Is.True);
                Assert.That(properties.ContainsKey("SourcePicoseconds"), Is.True);
                Assert.That(properties.ContainsKey("ServerTimestamp"), Is.False);
                Assert.That(properties["Value"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(properties["SourceTimestamp"]!["format"]!.GetValue<string>(), Is.EqualTo("date-time"));
            });
        }

        [Test]
        public void CreateDataSetSchemaOutputParsesAsJson()
        {
            var provider = new PubSubSchemaProvider();

            string schema = provider.CreateDataSetSchema(
                CreateMetaData(),
                DataSetFieldContentMask.None).ToSchemaString();

            Assert.That(JsonNode.Parse(schema), Is.Not.Null);
        }

        [Test]
        public void AddPubSubSchemaRegistersProvider()
        {
            ServiceProvider services = new ServiceCollection()
                .AddOpcUa()
                .AddPubSubSchema()
                .Services
                .BuildServiceProvider();

            Assert.That(services.GetRequiredService<IPubSubSchemaProvider>(), Is.TypeOf<PubSubSchemaProvider>());
        }

        private static JsonObject CreateRoot(PubSubSchemaProvider provider, DataSetFieldContentMask mask)
        {
            var document = (JsonSchemaDocument)provider.CreateDataSetSchema(CreateMetaData(), mask);
            return document.Root;
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "Telemetry",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Name",
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Counter",
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Flags",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension
                    }
                ]
            };
        }
    }
}
