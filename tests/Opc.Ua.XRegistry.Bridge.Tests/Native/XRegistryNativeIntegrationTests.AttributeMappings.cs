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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeMappingProfilesProjectAndReadTypedValuesWithoutFlatteningLabelsAsync(bool canonical)
        {
            await ResetEndpointAsync(k_mappingModel).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g",
                """
                {"score":7,"series":[0,4294967295],"config":{"text":"null","ready":true},"labels":{"user":"unchanged"}}
                """))
                .ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201), seeded.Error?.Detail);
            var score = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"],
                canonical
                    ? [new(XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), new(k_mappingNamespace, "Count")]
                    : [new(k_mappingNamespace, "Domain"), new(k_mappingNamespace, "Count")])
            {
                Encoding = canonical
                    ? XRegistryNativeAttributeEncoding.CanonicalString : XRegistryNativeAttributeEncoding.Typed,
                NativeType = canonical ? BuiltInType.String : BuiltInType.Int16,
                Writable = true
            };
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:xregistry:mapping-fixture:" + Guid.NewGuid().ToString("N"),
                RootIdentifier = "Mapped",
                AttributeMappings =
                [
                    score,
                    new("/schemagroups", XRegistryNativeAttributeScope.Group, ["series"],
                        [new(k_mappingNamespace, "Domain"), new(k_mappingNamespace, "Series")])
                    {
                        NativeType = BuiltInType.UInt32
                    },
                    new("/schemagroups", XRegistryNativeAttributeScope.Group, ["config", "text"],
                        [new(k_mappingNamespace, "Domain"), new(k_mappingNamespace, "Text")]),
                    new("/schemagroups", XRegistryNativeAttributeScope.Group, ["config", "ready"],
                        [new(k_mappingNamespace, "Domain"), new(k_mappingNamespace, "Ready")])
                ]
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory, callerContext: null)
                .ConfigureAwait(false);
            try
            {
                await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                XRegistryBridgeNodeManager manager = factory.Manager!;
                NodeId group = await FindChildEntityAsync(manager.RegistryNodeId, "/schemagroups/g")
                    .ConfigureAwait(false);
                NodeId property = await MappedPropertyAsync(group, score).ConfigureAwait(false);
                DataValue actual = await m_session.ReadValueAsync(property).ConfigureAwait(false);
                Assert.That(actual.StatusCode, Is.EqualTo(StatusCodes.Good));
                if (canonical)
                {
                    Assert.That(actual.WrappedValue.TryGetValue(out string count), Is.True);
                    Assert.That(count, Is.EqualTo("7"));
                }
                else
                {
                    Assert.That(actual.WrappedValue.TryGetValue(out short count), Is.True);
                    Assert.That(count, Is.EqualTo(7));
                }
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                XRegistryResponse read = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(7));
                    Assert.That(read.Metadata.GetProperty("series").GetRawText(), Is.EqualTo("[0,4294967295]"));
                    Assert.That(read.Metadata.GetProperty("config").GetProperty("text").GetString(),
                        Is.EqualTo("null"));
                    Assert.That(read.Metadata.GetProperty("config").GetProperty("ready").GetBoolean(), Is.True);
                    Assert.That(read.Metadata.GetProperty("labels").GetProperty("user").GetString(),
                        Is.EqualTo("unchanged"));
                    Assert.That(read.Metadata.GetProperty("labels").TryGetProperty("Count", out _), Is.False);
                });
                WriteResponse write = await m_session.WriteAsync(null,
                [
                    new WriteValue
                    {
                        NodeId = property,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(canonical ? Variant.From("12") : Variant.From((short)12))
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(write.Results[0], Is.EqualTo(StatusCodes.Good));
                XRegistryResponse stored = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(stored.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(12));
                    Assert.That(stored.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                });
                if (canonical)
                {
                    AttributesTypeClient labels = await m_generic.GetGroup(group).GetLabelsAsync(m_telemetry)
                        .ConfigureAwait(false) ??
                        throw new AssertionException("Mapped group Labels is missing.");
                    await labels.AddAttributeAsync("Count", "21", 1).ConfigureAwait(false);
                    XRegistryResponse updated = await m_forwarder.Inner.ExecuteAsync(
                        Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
                    Assert.That(updated.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(21));
                    Assert.That(updated.Metadata.GetProperty("labels").TryGetProperty("Count", out _), Is.False);
                }
                if (!canonical)
                {
                    var extended = new XRegistryOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await extended.ExecuteAsync(
                            Request(XRegistryAction.Merge, "/schemagroups/g", """{"score":40000}"""))
                            .ConfigureAwait(false));
                    XRegistryResponse unchanged = await m_forwarder.Inner.ExecuteAsync(
                        Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
                    Assert.That(unchanged.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(12));
                    Assert.That(unchanged.Generation, Is.EqualTo(stored.Generation));
                }
            }
            finally
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConditionalNativeMappingsFollowDiscriminatorsRatherThanProfileOrderAsync()
        {
            await ResetEndpointAsync("""
                {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
                  "kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{"reading":{"type":"decimal"}}}}}
                },"resources":{}}}}
                """).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/g",
                """{"kind":"actuator"}""")).ConfigureAwait(false);
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:xregistry:conditional-mapping:" + Guid.NewGuid().ToString("N"),
                RootIdentifier = "ConditionalMappings",
                AttributeMappings =
                [
                    new("/schemagroups", XRegistryNativeAttributeScope.Group, ["reading"],
                        [new(k_mappingNamespace, "Reading")]),
                    new("/schemagroups", XRegistryNativeAttributeScope.Group, ["kind"],
                        [new(k_mappingNamespace, "Kind")])
                ]
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory, callerContext: null)
                .ConfigureAwait(false);
            try
            {
                await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                XRegistryBridgeNodeManager manager = factory.Manager!;
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                XRegistryResponse inactive = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(inactive.Metadata.GetProperty("kind").GetString(), Is.EqualTo("actuator"));
                    Assert.That(inactive.Metadata.TryGetProperty("reading", out _), Is.False);
                });
                var native = new XRegistryOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                XRegistryResponse changed = await native.ExecuteAsync(Request(
                    XRegistryAction.Replace, "/schemagroups/g",
                    """{"kind":"SENSOR","reading":0.125}""")).ConfigureAwait(false);
                Assert.That(changed.StatusCode, Is.EqualTo(200), changed.Error?.Detail);
                XRegistryResponse active = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                    .ConfigureAwait(false);
                Assert.That(active.Metadata.GetProperty("reading").GetDouble(), Is.EqualTo(0.125));
            }
            finally
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReadOnlyCanonicalMappingRejectsLabelMethodWritesAsync()
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"score":7}""")).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            XRegistryResponse baseline = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"],
                [new(XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), new(k_mappingNamespace, "Count")])
            {
                Encoding = XRegistryNativeAttributeEncoding.CanonicalString
            };
            await WithMappedManagerAsync(MappingOptions(mapping), async manager =>
            {
                NodeId group = await FindChildEntityAsync(manager.RegistryNodeId, "/schemagroups/g")
                    .ConfigureAwait(false);
                NodeId property = await MappedPropertyAsync(group, mapping).ConfigureAwait(false);
                WriteResponse write = await m_session.WriteAsync(null,
                [
                    new WriteValue
                    {
                        NodeId = property,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From("12"))
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(write.Results[0], Is.EqualTo(StatusCodes.BadNotWritable));
                AttributesTypeClient labels = await m_generic.GetGroup(group).GetLabelsAsync(m_telemetry)
                    .ConfigureAwait(false) ??
                    throw new AssertionException("Group labels are absent.");
                ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await labels.AddAttributeAsync("Count", "12", 0).ConfigureAwait(false));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
                XRegistryResponse unchanged = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(unchanged.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(7));
                    Assert.That(unchanged.Generation, Is.EqualTo(baseline.Generation));
                });
            }).ConfigureAwait(false);
        }

        [TestCase("type")]
        [TestCase("rank")]
        [TestCase("denied")]
        public async Task MappedReadsRejectInvalidNativeMetadataAndDeniedValuesAsync(string failure)
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"score":7}""")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"], [new(k_mappingNamespace, "Count")])
            {
                NativeType = BuiltInType.Int16
            };
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                NodeId group = await FindChildEntityAsync(manager.RegistryNodeId, "/schemagroups/g")
                    .ConfigureAwait(false);
                NodeId property = await MappedPropertyAsync(group, mapping).ConfigureAwait(false);
                ILocalAddressSpace space = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
                Assert.That(space.TryGetNode(property, out NodeState? node), Is.True);
                PropertyState variable = node as PropertyState
                    ?? throw new AssertionException("Mapped property is absent.");
                if (failure == "type")
                {
                    variable.DataType = Ua.DataTypeIds.String;
                }
                else if (failure == "rank")
                {
                    variable.ValueRank = ValueRanks.OneDimension;
                }
                else
                {
                    variable.OnSimpleReadValueAsync = (_, _, _) =>
                        new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(
                            StatusCodes.BadUserAccessDenied, Variant.From((short)7)));
                }
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false));
                Assert.That(rejected.StatusCode, Is.EqualTo(
                    failure == "denied" ? StatusCodes.BadUserAccessDenied : StatusCodes.BadTypeMismatch));
            }).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RequiredAndOptionalMappingsPreserveMissingPropertySemanticsAsync(bool missing, bool required)
        {
            string model = """
                {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
                "score":{"type":"integer","required":REQUIRED}},"resources":{}}}}
                """.Replace("REQUIRED", required ? "true" : "false", StringComparison.Ordinal);
            await ResetEndpointAsync(model).ConfigureAwait(false);
            _ = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"score":7}""")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"], [new(k_mappingNamespace, "Count")]);
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                if (missing)
                {
                    options = options with
                    {
                        AttributeMappings =
                        [
                            new("/schemagroups", XRegistryNativeAttributeScope.Group, ["score"],
                                [new(k_mappingNamespace, "Absent")])
                        ]
                    };
                }
                else
                {
                    NodeId group = await FindChildEntityAsync(manager.RegistryNodeId, "/schemagroups/g")
                        .ConfigureAwait(false);
                    NodeId property = await MappedPropertyAsync(group, mapping).ConfigureAwait(false);
                    ILocalAddressSpace space = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
                    Assert.That(space.TryGetNode(property, out NodeState? node), Is.True);
                    PropertyState variable = node as PropertyState
                        ?? throw new AssertionException("Mapped property is absent.");
                    variable.OnSimpleReadValueAsync = (_, _, _) =>
                        new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(
                            StatusCodes.BadNoData, Variant.Null));
                }
                var reader = new XRegistryBaseOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                if (required)
                {
                    ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                            .ConfigureAwait(false));
                    Assert.That(rejected.StatusCode,
                        Is.EqualTo(missing ? StatusCodes.BadNodeIdUnknown : StatusCodes.BadNoData));
                }
                else
                {
                    XRegistryResponse read = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(read.StatusCode, Is.EqualTo(200));
                        Assert.That(read.Metadata.TryGetProperty("score", out _), Is.False);
                    });
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task MappedLabelCollisionRejectsBeforePublicationAsync()
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(Request(
                XRegistryAction.Replace, "/schemagroups/g", """{"score":7,"labels":{"user":"unchanged"}}"""))
                .ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            XRegistryResponse baseline = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"],
                [new(XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), new(k_mappingNamespace, "count")])
            {
                Encoding = XRegistryNativeAttributeEncoding.CanonicalString
            };
            XRegistryBridgeNativeOptions options = MappingOptions(mapping);
            await WithMappedManagerAsync(options, async manager =>
            {
                var native = new XRegistryOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await native.ExecuteAsync(Request(XRegistryAction.Merge, "/schemagroups/g",
                        """{"labels":{"count":"user"}}""")).ConfigureAwait(false));
                XRegistryResponse unchanged = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(unchanged.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(7));
                    Assert.That(unchanged.Metadata.GetProperty("labels").GetRawText(),
                        Is.EqualTo("""{"user":"unchanged"}"""));
                    Assert.That(unchanged.Generation, Is.EqualTo(baseline.Generation));
                });
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task MappedPropertyQuotaCountsLiveOwnersBeforeCommitAsync()
        {
            await ResetEndpointAsync(k_scoreMappingModel).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/a", """{"score":7}""")).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            XRegistryResponse baseline = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/a")).ConfigureAwait(false);
            var mapping = new XRegistryNativeAttributeMapping(
                "/schemagroups", XRegistryNativeAttributeScope.Group, ["score"], [new(k_mappingNamespace, "Count")]);
            XRegistryBridgeNativeOptions options = MappingOptions(mapping) with { MaxMappedProperties = 1 };
            await WithMappedManagerAsync(options, async manager =>
            {
                var native = new XRegistryOpcUaEndpoint(m_session, manager.RegistryNodeId, options, m_telemetry);
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await native.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/b", """{"score":8}"""))
                        .ConfigureAwait(false));
                XRegistryResponse unchanged = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/a")).ConfigureAwait(false);
                XRegistryResponse absent = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/b")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                    Assert.That(unchanged.Generation, Is.EqualTo(baseline.Generation));
                    Assert.That(unchanged.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(7));
                    Assert.That(absent.StatusCode, Is.EqualTo(404));
                });
                XRegistryResponse deleted = await native.ExecuteAsync(
                    Request(XRegistryAction.Delete, "/schemagroups/a"))
                    .ConfigureAwait(false);
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                XRegistryResponse replacement = await native.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/schemagroups/b", """{"score":8}""")).ConfigureAwait(false);
                Assert.That(replacement.StatusCode, Is.EqualTo(201));
                Assert.That(replacement.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(8));
            }).ConfigureAwait(false);
        }

        private async Task<NodeId> MappedPropertyAsync(NodeId root, XRegistryNativeAttributeMapping mapping)
        {
            for (int index = 0; index < mapping.BrowsePath.Count; index++)
            {
                XRegistryNativeBrowseName segment = mapping.BrowsePath[index];
                ArrayOf<ReferenceDescription> children = await XRegistryOpcUaEndpoint.BrowseAsync(
                    m_session, root, m_options, CancellationToken.None).ConfigureAwait(false);
                ReferenceDescription child = children.ToList().Single(value => value.BrowseName.Name == segment.Name &&
                    m_session.NamespaceUris.GetString(value.BrowseName.NamespaceIndex) == segment.NamespaceUri);
                root = ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
            }
            return root;
        }

        private XRegistryBridgeNativeOptions MappingOptions(XRegistryNativeAttributeMapping mapping)
        {
            return m_options with
            {
                NamespaceUri = "urn:xregistry:mapping-boundary:" + Guid.NewGuid().ToString("N"),
                AttributeMappings = [mapping]
            };
        }

        private async Task WithMappedManagerAsync(
            XRegistryBridgeNativeOptions options, Func<XRegistryBridgeNodeManager, Task> action)
        {
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(m_forwarder, options));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory, callerContext: null)
                .ConfigureAwait(false);
            try
            {
                await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                await action(factory.Manager!).ConfigureAwait(false);
            }
            finally
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }

        private const string k_mappingNamespace = "urn:xregistry:fixture:properties";

        private const string k_scoreMappingModel = """
            {"groups":{"schemagroups":{"singular":"schemagroup",
            "attributes":{"score":{"type":"integer"}},"resources":{}}}}
            """;

        private const string k_mappingModel = """
            {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
              "score":{"type":"integer"},"series":{"type":"array","item":{"type":"uinteger"}},
              "config":{"type":"object","attributes":{"text":{"type":"string"},"ready":{"type":"boolean"}}}
            },"resources":{"schemas":{"singular":"schema"}}}}}
            """;
    }
}
