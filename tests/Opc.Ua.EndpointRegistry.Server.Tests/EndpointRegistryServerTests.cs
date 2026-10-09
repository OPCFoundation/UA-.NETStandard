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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.EndpointRegistry.Server.Tests
{
    /// <summary>
    /// Native Message Catalog workflows through generated proxies over real secured TCP sessions.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    [NonParallelizable]
    public sealed class EndpointRegistryServerTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pki = Path.Combine(TestContext.CurrentContext.WorkDirectory, nameof(EndpointRegistryServerTests),
                Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<RegistryServer>(context => new RegistryServer(context))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_client = new ClientFixture(m_telemetry);
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_pki, "server")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await m_fixture.StartAsync().ConfigureAwait(false);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_pki, "client")).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (m_pki is not null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, recursive: true);
            }
        }

        [Test]
        public async Task NativeMessageGroupIsRegisteredReadBrowsedChangedAndDeletedAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            NativeRegistryAccessTypeClient access = TypedAccess(session);
            RegistryRecordMapper mapper = Mapper(session);

            RegistryMutationResultDataType written = await access.WriteDocumentAsync(new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/factory",
                Definition = mapper.Canonicalize(FactoryGroup()),
                ExpectedEpoch = 0
            }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(written));
            Assert.That(written.Epoch, Is.EqualTo(1));
            Assert.That(written.Target.IsNull, Is.False);

            RegistryReadResultDataType read = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/messagegroups/factory/messages/temperature",
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 10,
                ContinuationPoint = ByteString.Empty
            }).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Document.TryGetValue(out MessageDefinitionDataType? message), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(read.Epoch, Is.EqualTo(1));
                Assert.That(message!.MessageId, Is.EqualTo("temperature"));
                Assert.That(message.ProtocolOptions, Is.TypeOf<MessageDefinitionProtocolOptionsMQTT50DataType>());
                Assert.That(((MessageDefinitionProtocolOptionsMQTT50DataType)message.ProtocolOptions).TopicName,
                    Is.EqualTo("factory/line1/temperature"));
                Assert.That(message.DataSchema, Is.TypeOf<JsonSchemaContentDataType>());
            });

            NodeId messageNode = await ResolveAsync(session, "factory", "temperature").ConfigureAwait(false);
            RegistryReadResultDataType snapshot = await ReadSnapshotAsync(session, messageNode).ConfigureAwait(false);
            Assert.That(snapshot.Epoch, Is.EqualTo(1));
            Assert.That(snapshot.Document.TryGetValue(out MessageDefinitionDataType? browsed), Is.True);
            Assert.That(browsed!.DataContentType, Is.EqualTo("application/json"));

            RegistryMutationResultDataType changed = await access.ApplyChangesAsync(new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/factory/messages/temperature",
                ExpectedEpoch = 1,
                Changes =
                [
                    new RegistryChangeDataType
                    {
                        Operation = 0,
                        Path = [new RegistryPathElementDataType { Kind = 0, Name = "description" }],
                        Value = new RegistryStringValueDataType { Kind = 2, Value = "Line 1 temperature" }
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(changed));
            Assert.That(changed.Epoch, Is.EqualTo(2));
            snapshot = await ReadSnapshotAsync(session, messageNode).ConfigureAwait(false);
            Assert.That(snapshot.Epoch, Is.EqualTo(2));

            RegistryMutationResultDataType stale = await access.ApplyChangesAsync(new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/factory/messages/temperature",
                ExpectedEpoch = 1,
                Changes =
                [
                    new RegistryChangeDataType
                    {
                        Operation = 1,
                        Path = [new RegistryPathElementDataType { Kind = 0, Name = "description" }]
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(stale.Issues[0].Code, Is.EqualTo("E_EPOCH_CONFLICT"));

            NodeId groupNode = await ResolveAsync(session, "factory").ConfigureAwait(false);
            var group = new GroupTypeClient(session, groupNode, m_telemetry!);
            await group.DeleteAsync(0).ConfigureAwait(false);
            Assert.That(await TryResolveAsync(session, "factory").ConfigureAwait(false), Is.EqualTo(NodeId.Null));
            RegistryReadResultDataType missing = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/messagegroups/factory",
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 10,
                ContinuationPoint = ByteString.Empty
            }).ConfigureAwait(false);
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task MutationsRequireAuthorizationAndSignAndEncryptAsync()
        {
            using ISession user = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
            RegistryRecordMapper mapper = Mapper(user);
            var request = new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/denied",
                Definition = mapper.Canonicalize(new MessageGroupDataType
                {
                    PresentFields = ["MessageGroupId"],
                    MessageGroupId = "denied"
                }),
                ExpectedEpoch = 0
            };
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(
                async () => await TypedAccess(user).WriteDocumentAsync(request).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            using ISession insecure = await ConnectAsync(SecurityPolicies.None,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            ServiceResultException plain = Assert.ThrowsAsync<ServiceResultException>(
                async () => await TypedAccess(insecure).WriteDocumentAsync(request).ConfigureAwait(false))!;
            Assert.That(plain.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));

            RegistryReadResultDataType read = await TypedAccess(user).ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/messagegroups/denied",
                DocumentKind = "metadata",
                View = 0,
                MaxItems = 10,
                ContinuationPoint = ByteString.Empty
            }).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task SemanticRuleViolationsAreReturnedAsTypedDiagnosticsAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            RegistryRecordMapper mapper = Mapper(session);
            MessageGroupDataType group = FactoryGroup();
            group.MessageGroupId = "wildcard";
            var message = (MessageDefinitionDataType)group.Messages.Entries[0].Value;
            ((MessageDefinitionProtocolOptionsMQTT50DataType)message.ProtocolOptions).TopicName = "factory/+/temperature";

            RegistryMutationResultDataType result = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/messagegroups/wildcard",
                    Definition = mapper.Canonicalize(group),
                    ExpectedEpoch = 0
                }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(result.Issues[0].Code, Is.EqualTo("E_MQTT_TOPIC"), Detail(result));
                Assert.That(result.Epoch, Is.Zero);
            });
            Assert.That(await TryResolveAsync(session, "wildcard").ConfigureAwait(false), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task EndpointCatalogProjectsProtocolTypedOptionsAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            var endpoint = (EndpointDataType)Mapper(session).Project(RegistryValues.Parse(
                """
                {"endpointid":"orders","usage":["producer"],"protocol":"MQTT/5.0","protocoloptions":{
                  "endpoints":[{"uri":"mqtts://broker.example.test"}],"topic":"factory/line1/orders","qos":1}}
                """u8), DataTypeIds.EndpointDataType);

            RegistryMutationResultDataType written = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/endpoints/orders",
                    Definition = endpoint,
                    ExpectedEpoch = 0
                }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(written));

            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            NodeId group = await PathAsync(session, ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry,
                session.NamespaceUris), new QualifiedName(BrowseNames.Endpoints, ns), new QualifiedName("orders", ns))
                .ConfigureAwait(false);
            NodeId options = await PathAsync(session, group, new QualifiedName(BrowseNames.Options, ns))
                .ConfigureAwait(false);
            NodeId value = await PathAsync(session, options, new QualifiedName(BrowseNames.Value, ns))
                .ConfigureAwait(false);
            NodeId usage = await PathAsync(session, group, new QualifiedName(BrowseNames.Usage, ns))
                .ConfigureAwait(false);
            BrowseResponse typeDefinition = await session.BrowseAsync(null, null, 0,
            [
                new BrowseDescription
                {
                    NodeId = options,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasTypeDefinition,
                    IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], default).ConfigureAwait(false);
            DataValue optionsValue = await session.ReadValueAsync(value).ConfigureAwait(false);
            DataValue usageValue = await session.ReadValueAsync(usage).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ExpandedNodeId.ToNodeId(typeDefinition.Results[0].References[0].NodeId,
                        session.NamespaceUris),
                    Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.MqttEndpointOptionsType, session.NamespaceUris)));
                Assert.That(optionsValue.WrappedValue.TryGetValue(out ExtensionObject settings) &&
                    settings.TryGetValue(out EndpointProtocolOptionsMQTT50DataType? mqtt, session.MessageContext) &&
                    mqtt!.Topic == "factory/line1/orders", Is.True);
                Assert.That(usageValue.WrappedValue.TryGetValue(out ArrayOf<string> roles) &&
                    roles.Count == 1 && roles[0] == "producer", Is.True);
            });
        }

        [Test]
        public async Task JsonCompatibilityPatchAndMetadataFileShareTheNativeStateAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            var root = new EndpointRegistryTypeClient(session,
                ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, session.NamespaceUris), m_telemetry!);
            (NodeId created, uint epoch) = await CommitAsync(root,
                "/messagegroups/json",
                "{\"messagegroupid\":\"json\",\"messages\":{\"m\":{\"messageid\":\"m\",\"description\":\"x\"}}}", 0)
                .ConfigureAwait(false);
            Assert.That(created.IsNull, Is.False);
            Assert.That(epoch, Is.EqualTo(1));

            ushort xns = session.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            NodeId message = await ResolveAsync(session, "json", "m").ConfigureAwait(false);
            NodeId file = await PathAsync(session, message, new QualifiedName(BrowseNames.Metadata, xns))
                .ConfigureAwait(false);
            uint pinned = await OpenAsync(session, file).ConfigureAwait(false);

            (_, uint changed) = await CommitAsync(root, "/messagegroups/json/messages/m",
                "{\"description\":null,\"datacontenttype\":\"application/json\"}", 1).ConfigureAwait(false);
            Assert.That(changed, Is.EqualTo(2));

            string before = await ReadToEndAsync(session, file, pinned).ConfigureAwait(false);
            uint current = await OpenAsync(session, file).ConfigureAwait(false);
            string after = await ReadToEndAsync(session, file, current).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(before, Is.EqualTo("{\"messageid\":\"m\",\"description\":\"x\",\"epoch\":1}"));
                Assert.That(after,
                    Is.EqualTo("{\"messageid\":\"m\",\"epoch\":2,\"datacontenttype\":\"application/json\"}"));
            });
            ServiceResultException write = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session.CallAsync(file, await PathAsync(session, file,
                    new QualifiedName(Ua.BrowseNames.Open)).ConfigureAwait(false), default,
                    new Variant((byte)2)).ConfigureAwait(false))!;
            Assert.That(write.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public async Task OversizedStringIsReadThroughABoundedNativeSnapshotAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            string description = new string('d', 20000) + "\U0001F600";
            RegistryMutationResultDataType written = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/messagegroups/large",
                    Definition = Mapper(session).Canonicalize(new MessageGroupDataType
                    {
                        PresentFields = ["Description", "MessageGroupId"],
                        Description = description,
                        MessageGroupId = "large"
                    }),
                    ExpectedEpoch = 0
                }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(written));

            NativeRegistryAccessTypeClient access = TypedAccess(session);
            RegistrySnapshotOpenResultDataType opened = await access.OpenDocumentAsync(
                new RegistrySnapshotOpenRequestDataType
                {
                    TargetXid = "/messagegroups/large",
                    DocumentKind = "metadata",
                    View = 1,
                    ExpectedEpoch = written.Epoch
                }).ConfigureAwait(false);
            Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.Good));
            ushort xns = session.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            NodeId limitsNode = await PathAsync(session,
                ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.EndpointRegistryTypedAccess, session.NamespaceUris),
                new QualifiedName(XRegistry.BrowseNames.SnapshotLimits, xns)).ConfigureAwait(false);
            DataValue limitsValue = await session.ReadValueAsync(limitsNode).ConfigureAwait(false);
            Assert.That(limitsValue.WrappedValue.TryGetValue(out ExtensionObject limitsObject), Is.True);
            Assert.That(limitsObject.TryGetValue(out RegistrySnapshotLimitsDataType? limits, session.MessageContext),
                Is.True);
            var text = new System.Text.StringBuilder();
            ByteString continuation = ByteString.Empty;
            while (true)
            {
                RegistrySnapshotReadResultDataType part = await access.ReadDocumentPartAsync(
                    new RegistrySnapshotReadRequestDataType
                    {
                        SnapshotId = opened.SnapshotId,
                        Path = [new RegistryPathElementDataType { Kind = 0, Name = "Description" }],
                        Offset = (uint)CountScalars(text.ToString()),
                        MaxItems = limits!.MaxReadItems,
                        MaxBytes = Math.Min(limits.MaxReadBytes, 8192u),
                        ContinuationPoint = continuation
                    }).ConfigureAwait(false);
                Assert.That(part.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(part.Value.TryGetValue(out string chunk), Is.True);
                text.Append(chunk);
                continuation = part.ContinuationPoint;
                if (part.Complete)
                {
                    break;
                }
            }
            Assert.That(text.ToString(), Is.EqualTo(description));
            RegistrySnapshotCloseResultDataType closed = await access.CloseDocumentAsync(opened.SnapshotId)
                .ConfigureAwait(false);
            Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private static int CountScalars(string text)
        {
            int count = 0;
            for (int index = 0; index < text.Length; index++)
            {
                count++;
                if (char.IsHighSurrogate(text[index]))
                {
                    index++;
                }
            }
            return count;
        }

        private static async Task<(NodeId Target, uint Epoch)> CommitAsync(
            EndpointRegistryTypeClient root,
            string xid,
            string patch,
            uint expectedEpoch)
        {
            return await root.CommitMetadataAsync(xid, ByteString.From(System.Text.Encoding.UTF8.GetBytes(patch)),
                expectedEpoch).ConfigureAwait(false);
        }

        private static async Task<uint> OpenAsync(ISession session, NodeId file)
        {
            NodeId open = await PathAsync(session, file, new QualifiedName(Ua.BrowseNames.Open)).ConfigureAwait(false);
            ArrayOf<Variant> output = await session.CallAsync(file, open, default, new Variant((byte)1))
                .ConfigureAwait(false);
            return output[0].TryGetValue(out uint handle) ? handle : throw new AssertionException("No file handle.");
        }

        private static async Task<string> ReadToEndAsync(ISession session, NodeId file, uint handle)
        {
            NodeId read = await PathAsync(session, file, new QualifiedName(Ua.BrowseNames.Read)).ConfigureAwait(false);
            NodeId close = await PathAsync(session, file, new QualifiedName(Ua.BrowseNames.Close)).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            while (true)
            {
                ArrayOf<Variant> output = await session.CallAsync(file, read, default, new Variant(handle),
                    new Variant(7)).ConfigureAwait(false);
                if (!output[0].TryGetValue(out ByteString chunk) || chunk.Length == 0)
                {
                    break;
                }
                buffer.Write(chunk.ToArray(), 0, chunk.Length);
            }
            await session.CallAsync(file, close, default, new Variant(handle)).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static async Task<NodeId> PathAsync(ISession session, NodeId start, params QualifiedName[] names)
        {
            var elements = new RelativePathElement[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                elements[index] = Element(names[index]);
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                new[]
                {
                    new BrowsePath { StartingNode = start, RelativePath = new RelativePath { Elements = elements } }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThan(0),
                "Browse path not found: " + string.Join<QualifiedName>("/", names));
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static MessageGroupDataType FactoryGroup()
        {
            return new MessageGroupDataType
            {
                PresentFields = ["MessageGroupId", "Protocol", "Messages"],
                MessageGroupId = "factory",
                Protocol = "MQTT/5.0",
                Messages = new MessageDefinitionMapDataType
                {
                    Entries =
                    [
                        new MessageDefinitionMapEntryDataType
                        {
                            Name = "temperature",
                            Value = new MessageDefinitionDataType
                            {
                                PresentFields =
                                [
                                    "MessageId",
                                    "Protocol",
                                    "DataSchemaFormat",
                                    "DataSchema",
                                    "DataContentType",
                                    "ProtocolOptions"
                                ],
                                MessageId = "temperature",
                                Protocol = "MQTT/5.0",
                                DataSchemaFormat = "JsonSchema/2020-12",
                                DataSchema = new JsonSchemaFormatProvider().Parse(
                                    "{\"type\":\"number\",\"x-unit\":\"C\"}"u8),
                                DataContentType = "application/json",
                                ProtocolOptions = new MessageDefinitionProtocolOptionsMQTT50DataType
                                {
                                    PresentFields = ["TopicName"],
                                    TopicName = "factory/line1/temperature"
                                }
                            }
                        }
                    ]
                }
            };
        }

        private async Task<ISession> ConnectAsync(string policy, IUserIdentity identity)
        {
            ISession session = await m_client!.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_fixture!.Port}/{nameof(RegistryServer)}"),
                policy,
                userIdentity: identity).ConfigureAwait(false);
            session.MessageContext.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry()
                .Commit();
            return session;
        }

        private NativeRegistryAccessTypeClient TypedAccess(ISession session)
        {
            return new NativeRegistryAccessTypeClient(session,
                ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.EndpointRegistryTypedAccess, session.NamespaceUris), m_telemetry!);
        }

        private static RegistryRecordMapper Mapper(ISession session)
        {
            return EndpointRegistryNativeCatalog.CreateMapper(session.MessageContext,
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()]);
        }

        private static async Task<NodeId> ResolveAsync(ISession session, params string[] names)
        {
            NodeId result = await TryResolveAsync(session, names).ConfigureAwait(false);
            return result.IsNull ? throw new AssertionException("The projected node was not found.") : result;
        }

        private static async Task<NodeId> TryResolveAsync(ISession session, params string[] names)
        {
            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            var elements = new System.Collections.Generic.List<RelativePathElement>
            {
                Element(new QualifiedName(BrowseNames.MessageGroups, ns))
            };
            for (int index = 0; index < names.Length; index++)
            {
                if (index > 0)
                {
                    elements.Add(Element(new QualifiedName(BrowseNames.Messages, ns)));
                }
                elements.Add(Element(new QualifiedName(names[index], ns)));
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                new[]
                {
                    new BrowsePath
                    {
                        StartingNode = ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, session.NamespaceUris),
                        RelativePath = new RelativePath { Elements = elements.ToArray() }
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            return response.Results.Count == 0 || StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0
                ? NodeId.Null
                : ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static RelativePathElement Element(QualifiedName name)
        {
            return new RelativePathElement
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = name
            };
        }

        private static async Task<RegistryReadResultDataType> ReadSnapshotAsync(ISession session, NodeId entity)
        {
            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                new[]
                {
                    new BrowsePath
                    {
                        StartingNode = entity,
                        RelativePath = new RelativePath
                        {
                            Elements = [Element(new QualifiedName(BrowseNames.Snapshot, ns))]
                        }
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            NodeId snapshot = ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, session.NamespaceUris);
            DataValue value = await session.ReadValueAsync(snapshot).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, value.StatusCode.ToString());
            return value.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out RegistryReadResultDataType? result, session.MessageContext) &&
                result is not null
                ? result
                : throw new AssertionException("The Snapshot Variable does not hold a native read result.");
        }

        private static string Detail(RegistryMutationResultDataType result)
        {
            return result.Issues.Count == 0 ? string.Empty : result.Issues[0].Code + ": " + result.Issues[0].Detail;
        }

        private sealed class RegistryServer : ReferenceServer
        {
            public RegistryServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
                AddNodeManager(new EndpointRegistryNodeManagerFactory(new EndpointRegistryServerOptions
                {
                    Generic = new EndpointRegistryCatalogOptions { RegistryId = "test-registry" }
                }));
            }
        }

        private ITelemetryContext? m_telemetry;
        private string? m_pki;
        private ServerFixture<RegistryServer>? m_fixture;
        private ClientFixture? m_client;
    }
}
