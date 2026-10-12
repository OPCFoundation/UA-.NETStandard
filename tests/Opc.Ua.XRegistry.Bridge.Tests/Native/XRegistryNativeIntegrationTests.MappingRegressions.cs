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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [TestCase("""{"text":"x","ready":true}""")]
        [TestCase("{}")]
        public async Task PartialCompoundProjectionRejectsNewUnmappedLeavesAsync(string config)
        {
            await ResetEndpointAsync(k_compoundMappingModel).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g", """{"config":{"text":"x"}}""")).ConfigureAwait(false);
            XRegistryResponse before = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping("/schemagroups", XRegistryNativeAttributeScope.Group,
                ["config", "text"], [new(k_mappingNamespace, "Text")]);
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                var native = new XRegistryOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                Assert.ThrowsAsync<ServiceResultException>(async () => await native.ExecuteAsync(Request(
                    XRegistryAction.Replace, "/schemagroups/g", "{\"config\":" + config + "}"))
                    .ConfigureAwait(false));
                XRegistryResponse after = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(after.Generation, Is.EqualTo(before.Generation));
                    Assert.That(after.Metadata.GetProperty("config").GetRawText(), Is.EqualTo("""{"text":"x"}"""));
                });
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task PartialCompoundBaseReadRejectsUnrepresentedModelMembersAsync()
        {
            await ResetEndpointAsync(k_compoundMappingModel).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g", """{"config":{"text":"x","ready":true}}"""))
                .ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping("/schemagroups", XRegistryNativeAttributeScope.Group,
                ["config", "text"], [new(k_mappingNamespace, "Text")]);
            XRegistryBridgeNativeOptions incomplete = MappingOptions(mapping);
            XRegistryBridgeNativeOptions complete = incomplete with
            {
                AttributeMappings =
                [
                    mapping,
                    new("/schemagroups", XRegistryNativeAttributeScope.Group,
                        ["config", "ready"], [new(k_mappingNamespace, "Ready")])
                ]
            };
            await WithMappedManagerAsync(complete, manager =>
            {
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, incomplete, m_telemetry);
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false));
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task MappedResourceMetaRetainsCompoundValuesAndRejectsIncompleteReadProfilesAsync()
        {
            await ResetEndpointAsync("""
                {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{
                "singular":"schema","hasdocument":false,"metaattributes":{"config":{"type":"object",
                "attributes":{"text":{"type":"string"},"ready":{"type":"boolean"}}}}}}}}}
                """).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g/schemas/r",
                """{"meta":{"config":{"text":"meta","ready":true}},"name":"version"}""")).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201), seeded.Error?.Detail);
            var mapping = new XRegistryNativeAttributeMapping("/schemagroups/schemas",
                XRegistryNativeAttributeScope.Meta, ["config", "text"], [new(k_mappingNamespace, "MetaText")]);
            XRegistryBridgeNativeOptions incomplete = MappingOptions(mapping);
            XRegistryBridgeNativeOptions complete = incomplete with
            {
                AttributeMappings =
                [
                    mapping,
                    new("/schemagroups/schemas", XRegistryNativeAttributeScope.Meta,
                        ["config", "ready"], [new(k_mappingNamespace, "MetaReady")])
                ]
            };
            await WithMappedManagerAsync(complete, async manager =>
            {
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, complete, m_telemetry);
                XRegistryResponse read = await reader.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/meta")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.StatusCode, Is.EqualTo(200));
                    Assert.That(read.Metadata.GetProperty("config").GetProperty("text").GetString(),
                        Is.EqualTo("meta"));
                    Assert.That(read.Metadata.GetProperty("config").GetProperty("ready").GetBoolean(), Is.True);
                });
                var partialReader = new XRegistryBaseOpcUaEndpoint(
                    m_session, manager.RegistryNodeId, incomplete, m_telemetry);
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await partialReader.ExecuteAsync(Request(
                        XRegistryAction.Read, "/schemagroups/g/schemas/r/meta")).ConfigureAwait(false));
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ActualMappedDiscriminatorOverridesItsDefaultBeforeDependentPropertyDecodingAsync()
        {
            await ResetEndpointAsync("""
                {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
                "kind":{"type":"string","required":true,"default":"text","ifvalues":{
                "text":{"siblingattributes":{"reading":{"type":"string"}}},
                "sensor":{"siblingattributes":{"reading":{"type":"decimal"}}}}}},"resources":{}}}}
                """).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g", """{"kind":"sensor","reading":0.125}"""))
                .ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping("/schemagroups", XRegistryNativeAttributeScope.Group,
                ["reading"], [new(k_mappingNamespace, "Reading")]);
            XRegistryBridgeNativeOptions options = MappingOptions(mapping) with
            {
                AttributeMappings =
                [
                    mapping,
                    new("/schemagroups", XRegistryNativeAttributeScope.Group,
                        ["kind"], [new(k_mappingNamespace, "Kind")])
                ]
            };
            await WithMappedManagerAsync(options, async manager =>
            {
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                XRegistryResponse read = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.Metadata.GetProperty("kind").GetString(), Is.EqualTo("sensor"));
                    Assert.That(read.Metadata.GetProperty("reading").GetDouble(), Is.EqualTo(0.125));
                });
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task AbsentCanonicalMappedLabelRemainsAbsentAsync()
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", "{}")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"],
                [new(XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), new(k_mappingNamespace, "Count")])
            {
                Encoding = XRegistryNativeAttributeEncoding.CanonicalString
            };
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                XRegistryResponse read = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.StatusCode, Is.EqualTo(200));
                    Assert.That(read.Metadata.TryGetProperty("score", out _), Is.False);
                    Assert.That(read.Metadata.GetProperty("labels").TryGetProperty("Count", out _), Is.False);
                });
            }).ConfigureAwait(false);
        }

        [TestCase(0, false)]
        [TestCase(-2, true)]
        [TestCase(-3, true)]
        [TestCase(-4, false)]
        public async Task MappedScalarReadsRespectFlexibleAndInvalidValueRanksAsync(int rank, bool accepted)
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"score":7}""")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping("/schemagroups", XRegistryNativeAttributeScope.Group,
                ["score"], [new(k_mappingNamespace, "Count")]) { NativeType = BuiltInType.Int16 };
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                NodeId group = await FindChildEntityAsync(manager.RegistryNodeId, "/schemagroups/g")
                    .ConfigureAwait(false);
                NodeId property = await MappedPropertyAsync(group, mapping).ConfigureAwait(false);
                ILocalAddressSpace space = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
                Assert.That(space.TryGetNode(property, out NodeState? node), Is.True);
                var variable = node as PropertyState ?? throw new AssertionException("Mapped property is absent.");
                variable.ValueRank = rank;
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                if (accepted)
                {
                    XRegistryResponse read = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                        .ConfigureAwait(false);
                    Assert.That(read.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(7));
                }
                else
                {
                    ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                            .ConfigureAwait(false));
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                }
            }).ConfigureAwait(false);
        }

        private const string k_compoundMappingModel = """
            {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
            "config":{"type":"object","attributes":{"text":{"type":"string"},"ready":{"type":"boolean"}}}
            },"resources":{}}}}
            """;
    }
}
