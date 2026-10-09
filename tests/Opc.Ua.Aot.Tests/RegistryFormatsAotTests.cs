/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.EndpointRegistry;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;
using RegistryEndpoint = Opc.Ua.EndpointRegistry.EndpointDataType;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the actual schema document providers under trimming and NativeAOT.
    /// </summary>
    public sealed class RegistryFormatsAotTests
    {
        [Test]
        public async Task SchemaProvidersPreserveValuesAndKnownFingerprints()
        {
            using ServiceProvider services = new ServiceCollection().AddSchemaRegistryFormats().BuildServiceProvider();
            ISchemaFormatProvider[] formats = services.GetServices<ISchemaFormatProvider>().ToArray();
            await Assert.That(formats.Length).IsEqualTo(3);
            var json = new JsonSchemaFormatProvider();
            SchemaContentDataType jsonSchema = json.Parse(
                Encoding.UTF8.GetBytes("{\"type\":\"object\",\"properties\":{\"x\":{\"default\":1.00}}}"));
            await Assert.That(RegistryValues.Identical(
                RegistryValues.Parse(json.Serialize(jsonSchema).Span),
                RegistryValues.Parse(Encoding.UTF8.GetBytes(
                    "{\"type\":\"object\",\"properties\":{\"x\":{\"default\":1.00}}}")))).IsTrue();
            var avro = new AvroSchemaFormatProvider();
            await Assert.That(avro.ComputeSchemaId(Encoding.UTF8.GetBytes("\"string\"")))
                .IsEqualTo(ByteString.FromHexString("C70345637248018F"));
            var arrow = new ArrowSchemaFormatProvider();
            ByteString raw = ByteString.FromHexString(
                "FFFFFFFF800000001000000000000A000C000600050008000A000000000104000C000000080008000000040008000000" +
                "040000000100000014000000100014000800000007000C00000010001000000000000002100000001C00000004000000" +
                "00000000020000006964000008000C00080007000800000000000001400000000000000000000000");
            SchemaContentDataType schema = arrow.Parse(raw.Span);
            await Assert.That(schema.IsEqual(arrow.Parse(arrow.Serialize(schema).Span))).IsTrue();
            await Assert.That(arrow.ComputeSchemaId(raw.Span))
                .IsEqualTo(ByteString.FromHexString("9972E47DBCA6850A"));
            foreach (string fixture in new[] { "arrow-metadata.ipc", "arrow-families.ipc" })
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RegistryFixtures", fixture));
                SchemaContentDataType complete = arrow.Parse(bytes);
                await Assert.That(complete.IsEqual(arrow.Parse(arrow.Serialize(complete).Span))).IsTrue();
            }
        }

        [Test]
        public async Task NativeRecordMapperAndModelDocumentsSurviveTrimming()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            context.NamespaceUris.GetIndexOrAppend(SchemaRegistry.Namespaces.SchemaRegistry);
            context.NamespaceUris.GetIndexOrAppend(EndpointRegistry.Namespaces.EndpointRegistry);
            context.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().AddOpcUaEndpointRegistry().Commit();
            RegistryRecordMapper mapper = EndpointRegistryNativeCatalog.CreateMapper(context,
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()]);
            RegistryRecordDataType record = mapper.Canonicalize(new RegistryEndpoint
            {
                PresentFields = ["EndpointId", "Usage", "Protocol", "ProtocolOptions"],
                EndpointId = "aot-endpoint",
                Usage = ["producer"],
                Protocol = "MQTT/5.0",
                ProtocolOptions = new EndpointProtocolOptionsMQTT50DataType
                {
                    PresentFields = ["Topic", "Qos"],
                    Topic = "factory/aot",
                    Qos = new RegistryNumberValueDataType
                    {
                        Kind = 3,
                        Coefficient = ByteString.From([1]),
                        IsInteger = true
                    }
                }
            });
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteExtensionObject(null, new ExtensionObject(record));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ExtensionObject decoded = decoder.ReadExtensionObject(null);
            bool found = decoded.TryGetValue(out RegistryEndpoint? endpoint, context);
            await Assert.That(found).IsTrue();
            await Assert.That(endpoint!.EndpointId).IsEqualTo("aot-endpoint");
            await Assert.That(((EndpointProtocolOptionsMQTT50DataType)endpoint.ProtocolOptions).Topic)
                .IsEqualTo("factory/aot");
            await Assert.That(RegistryValues.Identical(mapper.Restore(record), mapper.Restore(endpoint))).IsTrue();
            RegistryRecordDataType model = mapper.Project(EndpointRegistryNativeCatalog.ReadModel(),
                nameof(RegistryModelDocumentDataType));
            await Assert.That(model is RegistryModelDocumentDataType).IsTrue();
            await Assert.That(RegistryValues.Identical(mapper.Restore(model),
                EndpointRegistryNativeCatalog.ReadModel())).IsTrue();
        }
    }
}
