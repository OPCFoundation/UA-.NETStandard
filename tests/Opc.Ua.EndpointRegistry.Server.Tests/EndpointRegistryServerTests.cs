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
using System.Threading;
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
            await using XRegistry.Client.RegistrySnapshotClient client = await XRegistry.Client.RegistrySnapshotClient
                .OpenAsync(access,
                    new RegistrySnapshotOpenRequestDataType
                    {
                        TargetXid = "/messagegroups/large",
                        DocumentKind = "metadata",
                        View = 1,
                        ExpectedEpoch = written.Epoch
                    }, limits!.MaxReadItems, limits.MaxReadBytes).ConfigureAwait(false);
            string throughClient = await client.ReadStringAsync(
                [new RegistryPathElementDataType { Kind = 0, Name = "Description" }]).ConfigureAwait(false);
            Assert.That(throughClient, Is.EqualTo(description));
            Assert.That(client.TargetEpoch, Is.EqualTo(written.Epoch));
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

        [Test]
        public async Task ResolveMessageReturnsTypedOverlayAndVerifiedLocalOriginsAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            var group = new MessageGroupDataType
            {
                PresentFields = ["MessageGroupId", "Messages"],
                MessageGroupId = "resolution",
                Messages = new MessageDefinitionMapDataType
                {
                    Entries =
                    [
                        new MessageDefinitionMapEntryDataType
                        {
                            Name = "base",
                            Value = new MessageDefinitionDataType
                            {
                                PresentFields = ["MessageId", "Description", "DataContentType"],
                                MessageId = "base",
                                Description = "Inherited description",
                                DataContentType = "application/json"
                            }
                        },
                        new MessageDefinitionMapEntryDataType
                        {
                            Name = "derived",
                            Value = new MessageDefinitionDataType
                            {
                                PresentFields = ["MessageId", "BaseMessageUri"],
                                MessageId = "derived",
                                BaseMessageUri = "/messagegroups/resolution/messages/base"
                            }
                        }
                    ]
                }
            };
            RegistryMutationResultDataType written = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/messagegroups/resolution",
                    Definition = Mapper(session).Canonicalize(group)
                }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(written));
            var root = new EndpointRegistryTypeClient(session,
                ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, session.NamespaceUris), m_telemetry!);

            NativeMessageResolutionResultDataType result = await root.ResolveMessageAsync(
                new MessageResolutionRequestDataType { Reference = "/messagegroups/resolution/messages/derived" })
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Status, Is.EqualTo("complete"));
                Assert.That(result.Definition.MessageId, Is.EqualTo("derived"));
                Assert.That(result.Definition.Description, Is.EqualTo("Inherited description"));
                Assert.That(result.Definition.DataContentType, Is.EqualTo("application/json"));
                Assert.That(result.Schema, Is.Null);
                Assert.That(result.Sources.Count, Is.EqualTo(2));
                Assert.That(result.Sources[0].ApplicationUri,
                    Is.EqualTo(session.ConfiguredEndpoint.Description.Server.ApplicationUri));
                Assert.That(result.Sources[0].RegistryNode, Is.EqualTo(ObjectIds.EndpointRegistry));
                Assert.That(result.Sources[0].Role, Is.EqualTo("MetadataResource"));
                Assert.That(result.Sources[0].NativeTarget, Is.Not.EqualTo(ObjectIds.EndpointRegistry));
            });
            NativeMessageResolutionResultDataType missing = await root.ResolveMessageAsync(
                new MessageResolutionRequestDataType { Reference = "/messagegroups/resolution/messages/missing" })
                .ConfigureAwait(false);
            Assert.That(missing.Status, Is.EqualTo("missing-inputs"));
            Assert.That(missing.Issues[0].Code, Is.EqualTo("E_REFERENCE_MISSING"));
            Assert.That(missing.Definition, Is.Null);
        }

        [Test]
        public async Task MediaRootIsIsolatedAndRejectsDirectMessageCreationAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            RegistryRecordMapper mapper = Mapper(session);
            var endpoint = new EndpointDataType
            {
                PresentFields = ["EndpointId", "Usage", "Protocol", "ProtocolOptions"],
                EndpointId = "shared-name",
                Usage = ["consumer"],
                Protocol = "HTTP",
                ProtocolOptions = new EndpointProtocolOptionsHTTPDataType
                {
                    PresentFields = ["Method"],
                    Method = "GET"
                }
            };
            RegistryRecordDataType definition = mapper.Canonicalize(endpoint);
            var mediaAccess = new NativeRegistryAccessTypeClient(session,
                ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.MediaEndpointRegistryTypedAccess, session.NamespaceUris),
                m_telemetry!);
            RegistryMutationResultDataType generic = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType { TargetXid = "/endpoints/shared-name", Definition = definition })
                .ConfigureAwait(false);
            RegistryMutationResultDataType media = await mediaAccess.WriteDocumentAsync(
                new RegistryWriteRequestDataType { TargetXid = "/endpoints/shared-name", Definition = definition })
                .ConfigureAwait(false);
            Assert.That(generic.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(generic));
            Assert.That(media.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(media));
            Assert.That(media.Target, Is.Not.EqualTo(generic.Target));
            RegistryMutationResultDataType rejected = await mediaAccess.WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/endpoints/shared-name/messages/forbidden",
                    Definition = mapper.Canonicalize(new MessageDefinitionDataType
                    {
                        PresentFields = ["MessageId", "Protocol", "ProtocolOptions"],
                        MessageId = "forbidden",
                        Protocol = "HTTP",
                        ProtocolOptions = new MessageDefinitionProtocolOptionsHTTPDataType()
                    })
                }).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejected.Issues[0].Code, Is.EqualTo("E_MEDIA_ASSOCIATION"), Detail(rejected));
            RegistryReadResultDataType unchanged = await mediaAccess.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/endpoints/shared-name",
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 10
            }).ConfigureAwait(false);
            Assert.That(unchanged.Epoch, Is.EqualTo(media.Epoch));
            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.EndpointRegistry);
            NodeId mediaNode = await PathAsync(session,
                ExpandedNodeId.ToNodeId(ObjectIds.MediaEndpointRegistry, session.NamespaceUris),
                new QualifiedName(BrowseNames.Endpoints, ns), new QualifiedName("shared-name", ns))
                .ConfigureAwait(false);
            BrowseResponse type = await session.BrowseAsync(null, null, 0,
            [
                new BrowseDescription
                {
                    NodeId = mediaNode,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasTypeDefinition,
                    BrowseDirection = BrowseDirection.Forward,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], default).ConfigureAwait(false);
            Assert.That(ExpandedNodeId.ToNodeId(type.Results[0].References[0].NodeId, session.NamespaceUris),
                Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.MediaEndpointGroupType, session.NamespaceUris)));
        }

        [Test]
        public async Task ModelAndCapabilitiesAreCompleteNamedNativeDocumentsAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            NativeRegistryAccessTypeClient access = TypedAccess(session);
            RegistryReadResultDataType model = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/",
                DocumentKind = "model",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(model.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(model.Document.TryGetValue(out RegistryModelDocumentDataType? nativeModel), Is.True);
            Assert.That(nativeModel!.Groups.Entries.Count, Is.EqualTo(2));
            RegistryReadResultDataType capabilities = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/",
                DocumentKind = "capabilities",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(capabilities.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(capabilities.Document.TryGetValue(out RegistryCapabilitiesDocumentDataType? nativeCapabilities),
                Is.True);
            Assert.That(nativeCapabilities!.Pagination, Is.True);
            RegistrySnapshotOpenResultDataType opened = await access.OpenDocumentAsync(
                new RegistrySnapshotOpenRequestDataType
                {
                    TargetXid = "/",
                    DocumentKind = "model",
                    View = 1,
                    ExpectedEpoch = model.Epoch
                }).ConfigureAwait(false);
            Assert.That(opened.TargetEpoch, Is.EqualTo(model.Epoch));
            RegistrySnapshotReadResultDataType fields = await access.ReadDocumentPartAsync(
                new RegistrySnapshotReadRequestDataType
                {
                    SnapshotId = opened.SnapshotId,
                    Path = [],
                    MaxItems = 128,
                    MaxBytes = 16384
                }).ConfigureAwait(false);
            Assert.That(fields.Kind, Is.EqualTo(4));
            Assert.That(fields.TotalLength, Is.GreaterThan(0));
            Assert.That((await access.CloseDocumentAsync(opened.SnapshotId).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(null, "1")]
        [TestCase("v9", "v9")]
        public async Task SoleVersionAliasesRetainAuthoredAbsenceAndNeverSelectAnUnretainedVersionAsync(
            string? authoredVersion, string retainedVersion)
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            NativeRegistryAccessTypeClient access = TypedAccess(session);
            string groupId = "version-alias-" + retainedVersion;
            string groupXid = "/messagegroups/" + groupId;
            RegistryMutationResultDataType written = await access.WriteDocumentAsync(new RegistryWriteRequestDataType
            {
                TargetXid = groupXid,
                Definition = Mapper(session).Canonicalize(new MessageGroupDataType
                {
                    PresentFields = ["MessageGroupId", "Messages"],
                    MessageGroupId = groupId,
                    Messages = new MessageDefinitionMapDataType
                    {
                        Entries =
                        [
                            new MessageDefinitionMapEntryDataType
                            {
                                Name = "m",
                                Value = new MessageDefinitionDataType
                                {
                                    PresentFields = authoredVersion is null ? ["MessageId"] : ["MessageId", "VersionId"],
                                    MessageId = "m",
                                    VersionId = authoredVersion ?? string.Empty
                                }
                            }
                        ]
                    }
                })
            }).ConfigureAwait(false);
            RegistryReadResultDataType alias = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = groupXid + "/messages/m/versions/" + retainedVersion,
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(written));
            Assert.That(alias.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(alias.Document.TryGetValue(out MessageDefinitionDataType? message), Is.True);
            Assert.That(message!.MessageId, Is.EqualTo("m"));
            Assert.That(message.PresentFields.Contains("VersionId"), Is.EqualTo(authoredVersion is not null));
            Assert.That(message.VersionId, Is.EqualTo(authoredVersion ?? string.Empty));
            RegistryReadResultDataType absent = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = groupXid + "/messages/m/versions/absent",
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(absent.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task InheritedLabelMethodsShareTypedStateAndExactEpochChecksAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            ushort xns = session.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            NodeId root = ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, session.NamespaceUris);
            NodeId labels = await PathAsync(session, root, new QualifiedName(XRegistry.BrowseNames.Labels, xns))
                .ConfigureAwait(false);
            NodeId add = await PathAsync(session, labels, new QualifiedName(XRegistry.BrowseNames.AddAttribute, xns))
                .ConfigureAwait(false);
            await session.CallAsync(labels, add, default, new Variant("owner"), new Variant("plant"), new Variant(0u))
                .ConfigureAwait(false);
            RegistryReadResultDataType read = await TypedAccess(session).ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/",
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(read.Document.TryGetValue(out EndpointRegistryDocumentDataType? document), Is.True);
            Assert.That(document!.Labels.Entries[0].Name, Is.EqualTo("owner"));
            Assert.That(document.Labels.Entries[0].Value, Is.EqualTo("plant"));
            uint epoch = read.Epoch;
            await session.CallAsync(labels, add, default, new Variant("owner"), new Variant("plant"), new Variant(epoch))
                .ConfigureAwait(false);
            RegistryReadResultDataType unchanged = await TypedAccess(session).ReadDocumentAsync(
                new RegistryReadRequestDataType { TargetXid = "/", DocumentKind = "metadata", View = 1, MaxItems = 100 })
                .ConfigureAwait(false);
            Assert.That(unchanged.Epoch, Is.EqualTo(epoch));
        }

        [Test]
        public async Task CollectionQualifiedLifecycleEventsReachTheTcpSubscriberAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256,
                new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            EventFilter filter = GroupCreatedEventTypeRecord.EventFilters.Build(session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(session.NamespaceUris));
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(null, 20, 100, 10,
                0, true, 0, default).ConfigureAwait(false);
            CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(null,
                subscription.SubscriptionId, TimestampsToReturn.Both,
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = Ua.ObjectIds.Server, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter)
                    }
                }
            ], default).ConfigureAwait(false);
            Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            RegistryRecordMapper mapper = Mapper(session);
            RegistryMutationResultDataType group = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/messagegroups/event-shared",
                    Definition = mapper.Canonicalize(new MessageGroupDataType
                    {
                        PresentFields = ["MessageGroupId"],
                        MessageGroupId = "event-shared"
                    })
                }).ConfigureAwait(false);
            RegistryMutationResultDataType endpoint = await TypedAccess(session).WriteDocumentAsync(
                new RegistryWriteRequestDataType
                {
                    TargetXid = "/endpoints/event-shared",
                    Definition = mapper.Canonicalize(new EndpointDataType
                    {
                        PresentFields = ["EndpointId", "Usage"],
                        EndpointId = "event-shared",
                        Usage = ["producer"]
                    })
                }).ConfigureAwait(false);
            Assert.That(group.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(group));
            Assert.That(endpoint.StatusCode, Is.EqualTo(StatusCodes.Good), Detail(endpoint));
            var subjects = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (subjects.Count < 2)
            {
                PublishResponse published = await session.PublishAsync(null, default, cancellation.Token)
                    .ConfigureAwait(false);
                foreach (ExtensionObject data in published.NotificationMessage.NotificationData)
                {
                    if (data.TryGetValue(out EventNotificationList? events))
                    {
                        foreach (EventFieldList fields in events!.Events)
                        {
                            for (int index = 0; index < filter.SelectClauses.Count; index++)
                            {
                                ArrayOf<QualifiedName> path = filter.SelectClauses[index].BrowsePath;
                                if (path.Count == 1 && path[0].Name == XRegistry.BrowseNames.Subject &&
                                    fields.EventFields[index].TryGetValue(out string subject) &&
                                    subject.EndsWith("/event-shared", StringComparison.Ordinal))
                                {
                                    subjects.Add(subject);
                                }
                            }
                        }
                    }
                }
            }
            Assert.That(subjects, Is.EquivalentTo(s_eventSubjects));
            await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], default).ConfigureAwait(false);
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
                    Generic = new EndpointRegistryCatalogOptions { RegistryId = "test-registry" },
                    EventsEnabled = true,
                    EventSourceUrl = "urn:test:catalog-events",
                    Media = new EndpointRegistryCatalogOptions
                    {
                        RegistryId = "media-registry",
                        Collections = ["endpoints"]
                    }
                }));
            }
        }

        private ITelemetryContext? m_telemetry;
        private static readonly string[] s_eventSubjects = ["/messagegroups/event-shared", "/endpoints/event-shared"];
        private string? m_pki;
        private ServerFixture<RegistryServer>? m_fixture;
        private ClientFixture? m_client;
    }
}
