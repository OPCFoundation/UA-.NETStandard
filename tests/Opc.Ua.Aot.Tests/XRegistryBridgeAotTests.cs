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

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes owned JSON metadata, exact document bytes, DI and conditional replay
    /// without reflection-based serialization in a published NativeAOT process.
    /// </summary>
    public sealed class XRegistryBridgeAotTests
    {
        [Test]
        public async Task TransactionEnvelopeAndProviderPreserveGuardsAndBytesAsync()
        {
            using var model = JsonDocument.Parse("""
                {"groups":{"groups":{"singular":"group",
                "resources":{"schemas":{"singular":"schema","hasdocument":true}}}}}
                """);
            var services = new ServiceCollection();
            services.AddXRegistryTransactions(new XRegistryTransactionalOptions
            {
                RegistryId = "aot-registry",
                Model = model.RootElement,
                PublicRoot = new Uri("https://registry.example"),
                ShortLinksEnabled = true
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            IXRegistryEndpoint endpoint = provider.GetRequiredService<IXRegistryEndpoint>();
            var caller = new XRegistryCallContext("aot-test")
            {
                IsAuthenticated = true,
                Roles = ["xregistry.write"]
            };
            _ = await provider.GetRequiredService<IXRegistryShortLinkMaintenance>()
                .InitializeShortLinksAsync(caller).ConfigureAwait(false);
            using var body = JsonDocument.Parse("""{"versionid":"v1"}""");
            var codec = new XRegistryProtocolCodec();
            var write = new XRegistryRequest(XRegistryAction.Replace, "/groups/g/schemas/r")
            {
                Context = caller,
                Metadata = body.RootElement,
                Document = ByteString.From(new byte[] { 0, 255, 3 }),
                OperationId = "aot-write"
            };
            XRegistryRequest decoded = codec.DecodeRequest(codec.EncodeRequest(write), caller);
            XRegistryResponse created = await endpoint.ExecuteAsync(decoded).ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(decoded).ConfigureAwait(false);
            using var guard = JsonDocument.Parse("""{"epoch":0}""");
            var update = new XRegistryRequest(XRegistryAction.Merge, "/groups/g/schemas/r/versions/v1")
            {
                Context = caller,
                Metadata = guard.RootElement,
                View = XRegistryView.Metadata
            };
            IXRegistryPreparedOperation prepared = await provider.GetRequiredService<IXRegistryPreparedEndpoint>()
                .PrepareAsync(update).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable preparationLifetime = prepared.ConfigureAwait(false);
            XRegistryResponse beforeCommit = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1")
                {
                    Context = caller,
                    View = XRegistryView.Metadata
                }).ConfigureAwait(false);
            XRegistryResponse touched = await prepared.CommitAsync().ConfigureAwait(false);
            XRegistryResponse stale = await endpoint.ExecuteAsync(update).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") { Context = caller })
                .ConfigureAwait(false);
            XRegistryResponse wire = codec.DecodeResponse(codec.EncodeResponse(read));
            await Assert.That(created.StatusCode).IsEqualTo(201);
            await Assert.That(replay.StatusCode).IsEqualTo(201);
            await Assert.That(touched.StatusCode).IsEqualTo(200);
            await Assert.That(beforeCommit.Metadata.GetProperty("epoch").GetUInt64()).IsEqualTo(0UL);
            await Assert.That(ReferenceEquals(touched, prepared.Response)).IsTrue();
            await Assert.That(stale.StatusCode).IsEqualTo(400);
            await Assert.That(wire.Document.Span.SequenceEqual(new byte[] { 0, 255, 3 })).IsTrue();
            await Assert.That(wire.Metadata.GetProperty("epoch").GetUInt64()).IsEqualTo(1UL);
            XRegistryEndpointDescription description = codec.DecodeDescription(codec.EncodeDescription(
                await endpoint.InspectAsync(caller).ConfigureAwait(false)));
            await Assert.That(description.SupportsGenerationGuards).IsTrue();
            await Assert.That(description.SupportsVersionIncarnationGuards).IsTrue();
            await Assert.That(description.Generation).IsEqualTo(wire.Generation);
            await Assert.That(string.IsNullOrEmpty(wire.Generation)).IsFalse();
            await Assert.That(string.IsNullOrEmpty(wire.VersionIncarnation)).IsFalse();
            XRegistryRequest guardedRead = codec.DecodeRequest(codec.EncodeRequest(new XRegistryRequest(
                XRegistryAction.Read, "/groups/g/schemas/r/versions/v1")
            {
                ExpectedGeneration = beforeCommit.Generation,
                ExpectedVersionIncarnation = beforeCommit.VersionIncarnation
            }), caller);
            XRegistryResponse staleGeneration = await endpoint.ExecuteAsync(guardedRead).ConfigureAwait(false);
            await Assert.That(staleGeneration.StatusCode).IsEqualTo(409);
            await Assert.That(staleGeneration.Error?.Code).IsEqualTo("concurrent_change");
            XRegistryResponse currentGeneration = await endpoint.ExecuteAsync(guardedRead with
            {
                ExpectedGeneration = wire.Generation
            }).ConfigureAwait(false);
            await Assert.That(currentGeneration.StatusCode).IsEqualTo(200);
            await Assert.That(currentGeneration.Document.Span.SequenceEqual(new byte[] { 0, 255, 3 })).IsTrue();
            await Assert.That(currentGeneration.VersionIncarnation).IsEqualTo(beforeCommit.VersionIncarnation);
            string alias = new Uri(read.Metadata.GetProperty("shortself").GetString()!).AbsolutePath;
            XRegistryAddressResolution resolved = await provider.GetRequiredService<IXRegistryAddressResolver>()
                .ResolveAddressAsync(new XRegistryRequest(XRegistryAction.Read, alias) { Context = caller })
                .ConfigureAwait(false);
            XRegistryRequest aliasRequest = codec.DecodeRequest(codec.EncodeRequest(resolved.Request), caller);
            await Assert.That(aliasRequest.AddressPath).IsEqualTo(alias);
            XRegistryResponse aliasRead = await endpoint.ExecuteAsync(aliasRequest).ConfigureAwait(false);
            await Assert.That(aliasRead.Document).IsEqualTo(read.Document);
            using var attribute = JsonDocument.Parse("""{"type":"uinteger"}""");
            var nativeValue = Variant.From(ulong.MaxValue);
            JsonElement logical = XRegistryNativeAttributeCodec.Decode(nativeValue, attribute.RootElement,
                XRegistryNativeAttributeEncoding.Typed);
            await Assert.That(logical.GetUInt64()).IsEqualTo(ulong.MaxValue);
            using var compound = JsonDocument.Parse("""{"type":"array","item":{"type":"string"}}""");
            using var value = JsonDocument.Parse("""["null","last"]""");
            Variant encodedValue = XRegistryNativeAttributeCodec.Encode(value.RootElement, compound.RootElement,
                XRegistryNativeAttributeEncoding.CanonicalString);
            await Assert.That(encodedValue.TryGetValue(out string nativeText)).IsTrue();
            await Assert.That(nativeText).IsEqualTo("""["null","last"]""");
        }

        [Test]
        public async Task RegisteredNativeStructureUsesExplicitFieldAccessWithoutReflectionAsync()
        {
            var structure = new Structure(
                new XmlQualifiedName("Payload", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Payload", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Payload.Binary", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Payload.Xml", "urn:xregistry:aot:structure"),
                new StructureDefinition
                {
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "score",
                            DataType = DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar
                        },
                        new StructureField
                        {
                            Name = "tags",
                            DataType = DataTypeIds.String,
                            ValueRank = ValueRanks.OneDimension
                        }
                    ]
                },
                new Dictionary<string, BuiltInType>
                {
                    ["score"] = BuiltInType.Int32,
                    ["tags"] = BuiltInType.String
                });
            var mapping = new XRegistryNativeAttributeMapping(
                "/groups", XRegistryNativeAttributeScope.Group, ["payload"],
                [new("urn:xregistry:aot:structure", "Payload")])
            {
                StructureType = structure,
                StructureTypeId = structure.TypeId
            };
            using var definition = JsonDocument.Parse("""
                {"type":"object","attributes":{
                "score":{"type":"integer"},"tags":{"type":"array","item":{"type":"string"}}}}
                """);
            using var value = JsonDocument.Parse("""{"score":17,"tags":["null","last"]}""");
            Variant encoded = XRegistryNativeAttributeCodec.Encode(value.RootElement, definition.RootElement, mapping);
            await Assert.That(encoded.TryGetValue(out ExtensionObject extension)).IsTrue();
            await Assert.That(extension.TryGetValue(out IEncodeable body)).IsTrue();
            IStructure fields = body as IStructure
                ?? throw new InvalidOperationException("The registered native value has no typed field access.");
            await Assert.That(fields["score"].TryGetValue(out int score)).IsTrue();
            await Assert.That(score).IsEqualTo(17);
            await Assert.That(fields["tags"].TryGetValue(out ArrayOf<string> tags)).IsTrue();
            await Assert.That(tags.Count).IsEqualTo(2);
            await Assert.That(tags[0]).IsEqualTo("null");
            await Assert.That(tags[1]).IsEqualTo("last");
            JsonElement decoded = XRegistryNativeAttributeCodec.Decode(encoded, definition.RootElement, mapping);
            await Assert.That(decoded.GetProperty("score").GetInt32()).IsEqualTo(17);
            await Assert.That(decoded.GetProperty("tags")[0].GetString()).IsEqualTo("null");
            await Assert.That(decoded.GetProperty("tags")[1].GetString()).IsEqualTo("last");
            var optional = new StructureWithOptionalFields(
                new XmlQualifiedName("Reading", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Reading", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Reading.Binary", "urn:xregistry:aot:structure"),
                new ExpandedNodeId("Reading.Xml", "urn:xregistry:aot:structure"),
                new StructureDefinition
                {
                    StructureType = StructureType.StructureWithOptionalFields,
                    Fields =
                    [
                        new StructureField
                        {
                            Name = "reading",
                            DataType = DataTypeIds.Double,
                            ValueRank = ValueRanks.Scalar,
                            IsOptional = true
                        },
                        new StructureField
                        {
                            Name = "kind",
                            DataType = DataTypeIds.String,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                new Dictionary<string, BuiltInType>
                {
                    ["reading"] = BuiltInType.Double,
                    ["kind"] = BuiltInType.String
                });
            XRegistryNativeAttributeMapping conditionalMapping = mapping with
            {
                StructureType = optional,
                StructureTypeId = optional.TypeId
            };
            using var conditional = JsonDocument.Parse("""
                {"type":"object","attributes":{
                "kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{"reading":{"type":"decimal"}}}}}}}
                """);
            using var sensor = JsonDocument.Parse("""{"kind":"sensor","reading":0.125}""");
            Variant sensorValue = XRegistryNativeAttributeCodec.Encode(
                sensor.RootElement, conditional.RootElement, conditionalMapping);
            JsonElement sensorResult = XRegistryNativeAttributeCodec.Decode(
                sensorValue, conditional.RootElement, conditionalMapping);
            await Assert.That(sensorResult.GetProperty("reading").GetDouble()).IsEqualTo(0.125);
            await Assert.That(sensorResult.GetProperty("kind").GetString()).IsEqualTo("sensor");
            using var actuator = JsonDocument.Parse("""{"kind":"actuator"}""");
            Variant actuatorValue = XRegistryNativeAttributeCodec.Encode(
                actuator.RootElement, conditional.RootElement, conditionalMapping);
            JsonElement actuatorResult = XRegistryNativeAttributeCodec.Decode(
                actuatorValue, conditional.RootElement, conditionalMapping);
            await Assert.That(actuatorResult.GetProperty("kind").GetString()).IsEqualTo("actuator");
            await Assert.That(actuatorResult.TryGetProperty("reading", out _)).IsFalse();
        }
    }
}
