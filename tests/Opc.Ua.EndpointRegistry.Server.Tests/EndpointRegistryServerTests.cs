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
