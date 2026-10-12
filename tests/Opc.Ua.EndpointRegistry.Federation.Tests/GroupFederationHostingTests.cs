/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.EndpointRegistry.Federation.Server;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;
using static Opc.Ua.EndpointRegistry.Federation.Tests.FederationTestSupport;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class GroupFederationHostingTests
    {
        private const string LocalXid = "/messagegroups/federated-local";
        private const string RemoteXid = "/messagegroups/g";
        private const string RemoteJson =
            """
            {"messagegroupid":"g","name":"remote group","description":"all source fields",
             "epoch":7,"self":"https://source.example.test/messagegroups/g",
             "xid":"/messagegroups/g","createdat":"2026-10-09T00:00:00Z","modifiedat":"2026-10-09T01:00:00Z",
             "x-object":{"null":null,"decimal":1.00,"ordered":[true,"second",{}]},
             "x-ua-group-reference":{"untrusted":"source extension, not local identity pins"},
             "messages":{"m":{"messageid":"m","epoch":3,"description":"nested",
               "x-extension":{"keep":false}}}}
            """;

        [SetUp]
        public async Task StartAsync()
        {
            m_binding = null;
            m_observationFailure = null;
            m_options = null;
            m_telemetry = NUnitTelemetryContext.Create();
            m_pki = Path.Combine(TestContext.CurrentContext.WorkDirectory, nameof(GroupFederationHostingTests),
                Guid.NewGuid().ToString("N"));
            m_store = new HostingStateStore();
            m_fixture = new ServerFixture<GroupServer>(telemetry => new GroupServer(telemetry, m_store))
            {
                AutoAccept = true,
                SecurityNone = false
            };
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_pki, "server")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_client = new ClientFixture(m_telemetry);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_pki, "client")).ConfigureAwait(false);
            m_session = await m_client.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_fixture.Port}/{nameof(GroupServer)}"),
                SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry().Commit();
        }

        [TearDown]
        public async Task StopAsync()
        {
            if (m_binding is not null)
            {
                await m_binding.DisposeAsync().ConfigureAwait(false);
            }
            m_session?.Dispose();
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (m_store is not null)
            {
                await m_store.DisposeAsync().ConfigureAwait(false);
            }
            if (m_pki is not null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, recursive: true);
            }
        }

        [Test]
        public async Task VerifiedGroupOwnsReadOnlyNativePropertiesAndPreservesCompleteSourceAsync()
        {
            FederationGroupSnapshot observation = UaObservation();
            await BindAsync(observation).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_session!.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            RegistryEntityReferenceDataType local = Registry.FindCatalogReference(LocalXid, "Group")!;
            Assert.That(local.NativeTarget, Is.Not.EqualTo(observation.Source.NativeTarget));
            Assert.That(local.Xid, Is.EqualTo(LocalXid));
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Source.Xid, Is.EqualTo(RemoteXid));

            DataValue originValue = await ReadChildAsync(group, XRegistry.BrowseNames.OriginRegistry).ConfigureAwait(false);
            Assert.That(originValue.GetValue(ExtensionObject.Null).TryGetValue(
                out RegistryOriginDataType? origin, m_session.MessageContext), Is.True);
            Assert.That(origin!.OriginUri, Is.EqualTo("urn:test:one"));
            await AssertExternalReferenceAsync(group, Target, Application).ConfigureAwait(false);
            Assert.That((await ReadChildAsync(group, XRegistry.BrowseNames.GroupUrl).ConfigureAwait(false))
                .GetValue(string.Empty), Is.EqualTo(Locator));
            DataValue snapshotValue = await ReadChildAsync(group, "SourceSnapshot").ConfigureAwait(false);
            Assert.That(snapshotValue.GetValue(ExtensionObject.Null).TryGetValue(
                out RegistryReadResultDataType? snapshot, m_session.MessageContext), Is.True);
            Assert.That(snapshot!.Epoch, Is.EqualTo(7));
            Assert.That(snapshot.Document.TryGetValue(out MessageGroupDataType? metadata, m_session.MessageContext), Is.True);
            Assert.That(metadata!.MessageGroupId, Is.EqualTo("g"));
            Assert.That(RegistryValues.Identical(Mapper.Restore(metadata), Json(RemoteJson)), Is.True);
            Assert.That(Host.Current.Revision, Is.EqualTo(2), "The pins and metadata share one authoritative commit.");
        }

        [TestCase(6u, "older")]
        [TestCase(7u, "same epoch changed fields")]
        public async Task StaleOrContradictoryEpochCannotReplaceMetadataAsync(uint epoch, string description)
        {
            FederationGroupSnapshot observation = UaObservation();
            await BindAsync(observation).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            ulong revision = Host.Current.Revision;
            m_observation = UaObservation(json: RemoteJson.Replace("\"epoch\":7", "\"epoch\":" + epoch,
                StringComparison.Ordinal).Replace("all source fields", description, StringComparison.Ordinal), epoch: epoch);

            Assert.ThrowsAsync<ArgumentException>(async () => await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false));

            Assert.That(Host.Current.Revision, Is.EqualTo(revision));
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task RestoredPinsAreNotAuthenticationAndRequireMatchingFreshObservationAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            await RestartAsync().ConfigureAwait(false);
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options!);
            await m_binding.StartAsync().ConfigureAwait(false);
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(group).ConfigureAwait(false);
            Assert.That(Host.Current.Revision, Is.EqualTo(2));

            m_observation = UaObservation(target: new ExpandedNodeId("hostile-new-target", 0, Namespaces.EndpointRegistry));
            Assert.ThrowsAsync<ArgumentException>(async () => await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false));
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            await AssertUnavailableAsync(group).ConfigureAwait(false);

            m_observation = UaObservation();
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Source.NativeTarget, Is.EqualTo(Target));
            await AssertExternalReferenceAsync(group, Target, Application).ConfigureAwait(false);
            Assert.That(Host.Current.Revision, Is.EqualTo(2), "Fresh reauthentication of identical bytes is a CAS no-op.");
        }

        [TestCase("origin")]
        [TestCase("application")]
        [TestCase("root")]
        [TestCase("xid")]
        [TestCase("collection")]
        [TestCase("native-target")]
        [TestCase("locator")]
        public async Task HostileRelocationCannotChangeConfiguredOrDurablePinsAsync(string pin)
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observation = pin switch
            {
                "origin" => UaObservation(origin: "urn:hostile:origin"),
                "application" => UaObservation(application: "urn:hostile:application"),
                "root" => UaObservation(root: new ExpandedNodeId("wrong-root", 0, Namespaces.EndpointRegistry)),
                "xid" => UaObservation(xid: "/messagegroups/another", json: RemoteJson.Replace(
                    "\"messagegroupid\":\"g\"", "\"messagegroupid\":\"another\"", StringComparison.Ordinal)),
                "collection" => UaObservation(xid: "/endpoints/g",
                    json: """{"endpointid":"g","epoch":7,"usage":["producer"],"protocol":"MQTT/5.0"}"""),
                "native-target" => UaObservation(target: new ExpandedNodeId("another-target", 0, Namespaces.EndpointRegistry)),
                _ => UaObservation(locator: "opc.tcp://hostile.example.test:4840")
            };

            Assert.ThrowsAsync<ArgumentException>(async () => await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false));

            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task AuthorizedRelocationChangesOnlyLocatorAndKeepsPairedUriPinsAsync()
        {
            const string moved = "opc.tcp://relocated.example.test:62541";
            FederationGroupSnapshot original = UaObservation(pairedOrigin: true);
            var trust = new FederationTrustBinding(original.Source, Application, Root, [Locator, moved]);
            await BindAsync(original, trust).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observation = UaObservation(locator: moved, pairedOrigin: true);
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            Assert.That((await ReadChildAsync(group, XRegistry.BrowseNames.GroupUrl).ConfigureAwait(false))
                .GetValue(string.Empty), Is.EqualTo(moved));
            await AssertExternalReferenceAsync(group, Target, Application).ConfigureAwait(false);
            DataValue originValue = await ReadChildAsync(group, XRegistry.BrowseNames.OriginRegistry).ConfigureAwait(false);
            Assert.That(originValue.GetValue(ExtensionObject.Null).TryGetValue(
                out RegistryOriginDataType? origin, m_session!.MessageContext), Is.True);
            Assert.That(origin!.OriginUri, Is.Empty);
            Assert.That(origin.ServerUri, Is.EqualTo(Application));
            Assert.That(origin.RegistryNodeId, Is.EqualTo(Root));
            Assert.That(origin.RegistryNodeId.NamespaceIndex, Is.Zero);
            Assert.That(origin.RegistryNodeId.ServerIndex, Is.Zero);
            Assert.That(Host.Current.Revision, Is.EqualTo(3));
        }

        [TestCase("messagegroups")]
        [TestCase("endpoints")]
        public async Task HttpsGroupProviderPublishesCompleteNativeMetadataWithoutInventingTargetAsync(string collection)
        {
            const string metadataUrl = "https://authorized.example.test/group";
            const string evidenceUrl = "https://authorized.example.test/observation";
            string remoteXid = "/" + collection + "/g";
            string localXid = "/" + collection + "/federated-local";
            string metadata = collection == "messagegroups" ? RemoteJson :
                """
                {"endpointid":"g","epoch":7,"usage":["producer"],"protocol":"MQTT/5.0","channel":"orders",
                 "protocoloptions":{"host":"mqtts://broker.example.test"},
                 "x-object":{"null":null,"decimal":1.00,"ordered":[true,"second",{}]}}
                """;
            string evidence = "{\"OriginUri\":\"urn:test:https\",\"Xid\":\"" + remoteXid +
                "\",\"CollectionName\":\"" + collection + "\",\"GroupId\":\"g\",\"Epoch\":7}";
            using var handler = new GroupHttpHandler(metadataUrl, metadata, evidence);
            using var client = new HttpClient(handler);
            var trust = new FederationTrustBinding(new RegistryEntityReferenceDataType { OriginUri = "urn:test:https" },
                string.Empty, ExpandedNodeId.Null, [metadataUrl, evidenceUrl]);
            var provider = new HttpFederationProvider(client, trust, [], m_telemetry!);
            m_options = new EndpointRegistryGroupFederationOptions
            {
                Sources =
                [
                    new GroupFederationSource(localXid, remoteXid, trust,
                        ct => provider.PreloadGroupAsync(metadataUrl, evidenceUrl, Mapper, cancellationToken: ct))
                ]
            };
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options);
            await m_binding.StartAsync().ConfigureAwait(false);
            await m_binding.RefreshAsync(localXid).ConfigureAwait(false);
            NodeId group = Registry.FindCatalogObject(localXid)!.NodeId;
            Assert.That(m_binding.GetVerifiedSource(localXid)!.Source.HasNativeTarget, Is.False);
            Assert.That(m_binding.GetVerifiedSource(localXid)!.Source.NativeTarget.IsNull, Is.True);
            Assert.That((await ReadChildAsync(group, XRegistry.BrowseNames.ExternalReference).ConfigureAwait(false))
                .GetValue(ExpandedNodeId.Null).IsNull, Is.True);
            Assert.That((await ReadChildAsync(group, XRegistry.BrowseNames.GroupUrl).ConfigureAwait(false))
                .GetValue(string.Empty), Is.EqualTo(metadataUrl));
            DataValue value = await ReadChildAsync(group, "SourceSnapshot").ConfigureAwait(false);
            Assert.That(value.GetValue(ExtensionObject.Null).TryGetValue(
                out RegistryReadResultDataType? snapshot, m_session!.MessageContext), Is.True);
            Assert.That(snapshot!.Document.TryGetValue(out RegistryRecordDataType? record, m_session.MessageContext), Is.True);
            Assert.That(RegistryValues.Identical(Mapper.Restore(record!), Json(metadata)), Is.True);
            if (record is EndpointDataType endpoint)
            {
                Assert.That(endpoint.Channel, Is.EqualTo("orders"));
                Assert.That(endpoint.ProtocolOptions, Is.InstanceOf<EndpointProtocolOptionsMQTT50DataType>());
            }
        }

        [Test]
        public async Task OrdinaryPropertyWritesAndNativeMutationsCannotChangeProviderOwnedGroupAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            foreach (string name in new[]
            {
                XRegistry.BrowseNames.OriginRegistry, XRegistry.BrowseNames.ExternalReference,
                XRegistry.BrowseNames.GroupUrl, "SourceSnapshot"
            })
            {
                NodeId property = await ChildAsync(group, name).ConfigureAwait(false);
                DataValue value = await m_session!.ReadValueAsync(property).ConfigureAwait(false);
                WriteResponse response = await m_session!.WriteAsync(null,
                    [new WriteValue
                    {
                        NodeId = property, AttributeId = Attributes.Value,
                        Value = new DataValue(value.WrappedValue)
                    }],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.BadNotWritable), name);
            }
            var access = TypedAccess();
            RegistryMutationResultDataType written = await access.WriteDocumentAsync(new RegistryWriteRequestDataType
            {
                TargetXid = LocalXid,
                Definition = Mapper.Project(Json("""{"messagegroupid":"federated-local","description":"hostile"}"""),
                    nameof(MessageGroupDataType))
            }).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            RegistryMutationResultDataType changed = await access.ApplyChangesAsync(new RegistryChangeRequestDataType
            {
                TargetXid = LocalXid,
                Changes =
                [
                    new RegistryChangeDataType
                    {
                        Operation = 0, Path = [new RegistryPathElementDataType { Kind = 0, Name = "description" }],
                        Value = new RegistryStringValueDataType { Kind = 2, Value = "hostile" }
                    }
                ]
            }).ConfigureAwait(false);
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            ServiceResultException deleteError = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new GroupTypeClient(m_session!, group, m_telemetry!).DeleteAsync(0).ConfigureAwait(false))!;
            Assert.That(deleteError.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Source.NativeTarget, Is.EqualTo(Target));
        }

        [Test]
        public async Task InvalidationRemovalAndDisposalClearTransportExposureWithoutOrdinaryWriteEscapeAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            NodeId retiredProperty = await ChildAsync(group, XRegistry.BrowseNames.ExternalReference).ConfigureAwait(false);
            await m_binding.InvalidateAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(group).ConfigureAwait(false);
            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            await m_binding.RemoveAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            RegistryReadResultDataType absent = await TypedAccess().ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = LocalXid,
                DocumentKind = "metadata",
                View = 1,
                MaxItems = 100
            }).ConfigureAwait(false);
            Assert.That(absent.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            ReadResponse retiredRead = await m_session!.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = retiredProperty, AttributeId = Attributes.Value }], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(retiredRead.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            group = await LocalGroupAsync().ConfigureAwait(false);
            await m_binding.DisposeAsync().ConfigureAwait(false);
            await m_binding.DisposeAsync().ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(group).ConfigureAwait(false);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false));
            RegistryMutationResultDataType protectedResult = await Host.DeleteAsync(LocalXid, 0).ConfigureAwait(false);
            Assert.That(protectedResult.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void DiFactoryAddsOnlyTheOptInGroupLifecycleManager()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            var options = new EndpointRegistryGroupFederationOptions
            {
                Sources =
                [
                    new GroupFederationSource(LocalXid, RemoteXid, Binding(UaObservation().Source),
                        _ => throw new InvalidOperationException("Composition must not authenticate or connect."))
                ]
            };
            builder.AddEndpointRegistryGroupFederation(options);
            Assert.That(services.Count(descriptor => descriptor.ImplementationInstance is OpcUaServerNodeManagerRegistration
            { AsyncFactory: EndpointRegistryGroupFederationNodeManagerFactory }), Is.EqualTo(1));
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(EndpointRegistryNodeManager)), Is.False);
        }

        [Test]
        public async Task ReferencesAreInvalidBeforeRetirementAndReturnOnlyAfterActivationAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            NodeId group = await LocalGroupAsync().ConfigureAwait(false);
            var retiring = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<RegistryCommittedState, CancellationToken, ValueTask> activation = Host.Activation!;
            Host.Activation = async (state, ct) =>
            {
                retiring.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                await activation(state, ct).ConfigureAwait(false);
            };
            Task<RegistryMutationResultDataType> mutation = Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/ordinary",
                Definition = Mapper.Project(Json("""{"messagegroupid":"ordinary"}"""), nameof(MessageGroupDataType))
            }).AsTask();
            try
            {
                await retiring.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
                await AssertUnavailableAsync(group).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                Host.Activation = activation;
                Assert.That((await mutation.ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Source.NativeTarget, Is.EqualTo(Target));
            await AssertExternalReferenceAsync(await LocalGroupAsync().ConfigureAwait(false), Target, Application)
                .ConfigureAwait(false);
        }

        [TestCase("role", "MetadataResource")]
        [TestCase("xid", "/endpoints/g")]
        [TestCase("originuri", "urn:changed:origin")]
        [TestCase("originapplicationuri", "urn:contradictory:origin")]
        [TestCase("registryroot", "ns=4;s=not-portable")]
        [TestCase("applicationuri", "urn:changed:application")]
        [TestCase("nativetarget", "ns=7;s=session-local")]
        [TestCase("role", null)]
        [TestCase("nativetarget", null)]
        [TestCase("metadata", null)]
        [TestCase("unexpected", "not a field of the durable pin format")]
        public async Task RestoreRejectsContradictoryOrSessionIndexedPinsAsync(string field, string? value)
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            await m_binding.DisposeAsync().ConfigureAwait(false);
            RegistryStoredState stored = await m_store!.ReadAsync().ConfigureAwait(false);
            var document = (RegistryObjectValueDataType)RegistryValues.Parse(stored.Document.ToArray());
            var change = new RegistryChangeDataType
            {
                Operation = value is null ? (byte)1 : (byte)0,
                Path =
                [
                    new RegistryPathElementDataType { Kind = 0, Name = "messagegroups" },
                    new RegistryPathElementDataType { Kind = 0, Name = "federated-local" },
                    new RegistryPathElementDataType { Kind = 0, Name = "x-ua-group-reference" },
                    new RegistryPathElementDataType { Kind = 0, Name = field }
                ]
            };
            if (value is not null)
            {
                change.Value = new RegistryStringValueDataType { Kind = 2, Value = value };
            }
            else
            {
                change.Value = null!;
            }
            RegistryValueDataType changed = RegistryValues.ApplyChanges(document, [change]);
            Assert.That((await m_store.CommitAsync(stored.Revision, RegistryValues.ToJson(changed))
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            await RestartAsync().ConfigureAwait(false);
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options!);

            Assert.ThrowsAsync<ArgumentException>(async () => await m_binding.StartAsync().ConfigureAwait(false));

            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
        }

        [Test]
        public async Task ProviderFailureRevokesAnExistingTargetWithoutChangingCommittedMetadataAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observationFailure = new ServiceResultException(StatusCodes.BadNoCommunication);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task SecuredOpcUaProviderComposesWithHostedReferenceAndExposesActualRemoteGroupAsync()
        {
            await using var remoteStore = new MemoryRegistryStateStore();
            var remoteFixture = new ServerFixture<GroupServer>(telemetry => new GroupServer(telemetry, remoteStore))
            {
                AutoAccept = true,
                SecurityNone = false
            };
            ISession? remoteSession = null;
            try
            {
                await remoteFixture.LoadConfigurationAsync(Path.Combine(m_pki!, "remote")).ConfigureAwait(false);
                remoteFixture.Config.ApplicationUri = "urn:test:remote-group-host";
                remoteFixture.Config.ServerConfiguration!.UserTokenPolicies +=
                    new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
                await remoteFixture.StartAsync().ConfigureAwait(false);
                remoteSession = await m_client!.ConnectAsync(
                    new Uri($"opc.tcp://localhost:{remoteFixture.Port}/{nameof(GroupServer)}"),
                    SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
                remoteSession.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry()
                    .AddOpcUaEndpointRegistry().Commit();
                RegistryMutationResultDataType result = await remoteFixture.Server.Registry.Generic!.WriteAsync(
                    new RegistryWriteRequestDataType
                    {
                        TargetXid = RemoteXid,
                        Definition = Mapper.Project(Json(
                            """{"messagegroupid":"g","description":"from actual secured source","x-preserved":{"n":null}}"""),
                            nameof(MessageGroupDataType))
                    }).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                var origin = new RegistryEntityReferenceDataType
                {
                    ApplicationUri = remoteSession.ConfiguredEndpoint.Description.Server.ApplicationUri,
                    RegistryNode = ObjectIds.EndpointRegistry
                };
                var trust = new FederationTrustBinding(origin, origin.ApplicationUri!, ObjectIds.EndpointRegistry,
                    [remoteSession.ConfiguredEndpoint.Description.EndpointUrl!]);
                var provider = new OpcUaFederationProvider(remoteSession, trust, Mapper, m_telemetry!);
                m_options = new EndpointRegistryGroupFederationOptions
                {
                    Sources =
                    [
                        new GroupFederationSource(LocalXid, RemoteXid, trust,
                            ct => provider.PreloadGroupAsync(RemoteXid, cancellationToken: ct))
                    ]
                };
                m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options);
                await m_binding.StartAsync().ConfigureAwait(false);
                await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
                FederationGroupSnapshot observed = m_binding.GetVerifiedSource(LocalXid)!;
                RegistryEntityReferenceDataType actual = remoteFixture.Server.Registry.FindCatalogReference(RemoteXid, "Group")!;
                Assert.That(observed.Source.NativeTarget, Is.EqualTo(actual.NativeTarget));
                Assert.That(observed.RegistryRoot, Is.EqualTo(ObjectIds.EndpointRegistry));
                Assert.That(observed.Source.Xid, Is.EqualTo(RemoteXid));
                await AssertExternalReferenceAsync(await LocalGroupAsync().ConfigureAwait(false),
                    actual.NativeTarget, origin.ApplicationUri!).ConfigureAwait(false);
                Assert.That(remoteSession.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(m_session!.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            }
            finally
            {
                remoteSession?.Dispose();
                await remoteFixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FailedAuthoritativeCasDoesNotAuthenticateCandidateAndCanBeRetriedAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observation = UaObservation(json: RemoteJson.Replace("\"epoch\":7", "\"epoch\":8", StringComparison.Ordinal), epoch: 8);
            m_store!.RejectCommit = true;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Host.Current.Revision, Is.EqualTo(2));
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
            m_store.RejectCommit = false;
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            Assert.That(Host.Current.Revision, Is.EqualTo(3));
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Epoch, Is.EqualTo(8));
        }

        [Test]
        public async Task DurableActivationFailureStaysUnauthenticatedAcrossRecoveryAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observation = UaObservation(json: RemoteJson.Replace("\"epoch\":7", "\"epoch\":8", StringComparison.Ordinal), epoch: 8);
            Host.Activation = (_, _) => throw new ServiceResultException(StatusCodes.BadResourceUnavailable);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.UncertainNotAllNodesAvailable));
            Assert.That(Host.Current.Revision, Is.EqualTo(3), "The shared durable commit must not be reported as a no-op.");
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await RestartAsync().ConfigureAwait(false);
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options!);
            await m_binding.StartAsync().ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid)!.Epoch, Is.EqualTo(8));
            Assert.That(Host.Current.Revision, Is.EqualTo(3));
        }

        [Test]
        public async Task NativeSourceXidClaimCannotContradictVerifiedGroupSelectionAsync()
        {
            await BindAsync(UaObservation(json: RemoteJson.Replace(
                "\"xid\":\"/messagegroups/g\"", "\"xid\":\"/messagegroups/another\"", StringComparison.Ordinal)))
                .ConfigureAwait(false);

            Assert.ThrowsAsync<ArgumentException>(async () => await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false));

            Assert.That(Host.Current.Revision, Is.EqualTo(1));
            Assert.That(m_binding!.GetVerifiedSource(LocalXid), Is.Null);
            Assert.That(Registry.FindCatalogObject(LocalXid), Is.Null);
        }

        [Test]
        public async Task EqualRemoteGroupIdsRemainSeparatedByOriginAndCollectionAsync()
        {
            FederationGroupSnapshot first = UaObservation();
            FederationGroupSnapshot anotherOrigin = UaObservation(origin: "urn:test:two");
            FederationGroupSnapshot anotherCollection = UaObservation(xid: "/endpoints/g",
                target: new ExpandedNodeId("other-collection-target", 0, Namespaces.EndpointRegistry),
                json: """{"endpointid":"g","epoch":7,"usage":["producer"],"protocol":"MQTT/5.0"}""");
            const string firstLocal = "/messagegroups/federated-one";
            const string anotherLocal = "/messagegroups/federated-two";
            const string endpointLocal = "/endpoints/federated-one";
            m_options = new EndpointRegistryGroupFederationOptions
            {
                Sources =
                [
                    new GroupFederationSource(firstLocal, RemoteXid, Binding(first.Source),
                        _ => new ValueTask<FederationGroupSnapshot>(first)),
                    new GroupFederationSource(anotherLocal, RemoteXid, Binding(anotherOrigin.Source),
                        _ => new ValueTask<FederationGroupSnapshot>(anotherOrigin)),
                    new GroupFederationSource(endpointLocal, "/endpoints/g", Binding(anotherCollection.Source),
                        _ => new ValueTask<FederationGroupSnapshot>(anotherCollection))
                ]
            };
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options);
            await m_binding.StartAsync().ConfigureAwait(false);
            await m_binding.RefreshAsync(firstLocal).ConfigureAwait(false);
            await m_binding.RefreshAsync(anotherLocal).ConfigureAwait(false);
            await m_binding.RefreshAsync(endpointLocal).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(firstLocal)!.Origin,
                Is.Not.EqualTo(m_binding.GetVerifiedSource(anotherLocal)!.Origin));
            Assert.That(m_binding.GetVerifiedSource(firstLocal)!.Source.Xid, Is.EqualTo(RemoteXid));
            Assert.That(m_binding.GetVerifiedSource(endpointLocal)!.Source.Xid, Is.EqualTo("/endpoints/g"));
            Assert.That(Registry.FindCatalogReference(firstLocal, "Group")!.NativeTarget,
                Is.Not.EqualTo(Registry.FindCatalogReference(anotherLocal, "Group")!.NativeTarget));
            Assert.That(Registry.FindCatalogObject(endpointLocal), Is.Not.Null);
            Assert.That(Host.Current.Revision, Is.EqualTo(4));
        }

        [Test]
        public async Task IntegrationManagerRestoresWithoutFetchingAndCleansUpOnServerStopAsync()
        {
            await BindAsync(UaObservation()).ConfigureAwait(false);
            await m_binding!.RefreshAsync(LocalXid).ConfigureAwait(false);
            m_observationFailure = new InvalidOperationException("Startup must not call the configured provider.");
            await RestartAsync(integration: true).ConfigureAwait(false);
            EndpointRegistryGroupFederationNodeManager manager = m_fixture!.Server.Integration!;
            m_binding = manager.Binding!;
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
            await AssertUnavailableAsync(await LocalGroupAsync().ConfigureAwait(false)).ConfigureAwait(false);
            m_observationFailure = null;
            await m_binding.RefreshAsync(LocalXid).ConfigureAwait(false);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Not.Null);
            await m_fixture.StopAsync().ConfigureAwait(false);
            m_fixture = null;
            Assert.That(manager.Binding, Is.Null);
            Assert.That(m_binding.GetVerifiedSource(LocalXid), Is.Null);
        }

        private async Task BindAsync(FederationGroupSnapshot observation, FederationTrustBinding? trust = null)
        {
            m_observation = observation;
            m_options = new EndpointRegistryGroupFederationOptions
            {
                Sources =
                [
                    new GroupFederationSource(LocalXid, RemoteXid, trust ?? Binding(observation.Source),
                        _ => m_observationFailure is null ? new ValueTask<FederationGroupSnapshot>(m_observation!) :
                            throw m_observationFailure)
                ]
            };
            m_binding = new EndpointRegistryGroupFederationBinding(Registry, m_options);
            await m_binding.StartAsync().ConfigureAwait(false);
        }

        private static FederationGroupSnapshot UaObservation(
            string json = RemoteJson, uint epoch = 7, string origin = "urn:test:one", string application = Application,
            ExpandedNodeId root = default, ExpandedNodeId target = default, string xid = RemoteXid,
            string locator = Locator, bool pairedOrigin = false)
        {
            root = root.IsNull ? Root : root;
            target = target.IsNull ? Target : target;
            var source = new RegistryEntityReferenceDataType
            {
                OriginUri = pairedOrigin ? string.Empty : origin,
                ApplicationUri = pairedOrigin ? application : string.Empty,
                RegistryNode = pairedOrigin ? root : ExpandedNodeId.Null,
                Xid = xid,
                Role = "Group",
                Locator = locator,
                HasNativeTarget = true,
                NativeTarget = target
            };
            bool endpoint = xid.StartsWith("/endpoints/", StringComparison.Ordinal);
            return new FederationGroupSnapshot(source, Binding(source, application, root), RootEvidence(root),
                TargetEvidence(target, [XRegistry.ObjectTypeIds.GroupType,
                    endpoint ? ObjectTypeIds.EndpointGroupType : ObjectTypeIds.MessageDefinitionGroupType], role: "Group"),
                Mapper.Project(Json(json), endpoint ? nameof(EndpointDataType) : nameof(MessageGroupDataType)), epoch);
        }

        private async Task RestartAsync(bool integration = false)
        {
            await m_binding!.DisposeAsync().ConfigureAwait(false);
            m_binding = null;
            m_session!.Dispose();
            m_session = null;
            await m_fixture!.StopAsync().ConfigureAwait(false);
            m_fixture = new ServerFixture<GroupServer>(telemetry =>
                new GroupServer(telemetry, m_store!, integration ? m_options : null))
            {
                AutoAccept = true,
                SecurityNone = false
            };
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_pki!, "server")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_session = await m_client!.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_fixture.Port}/{nameof(GroupServer)}"),
                SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry().Commit();
        }

        private NativeRegistryAccessTypeClient TypedAccess() => new(m_session!,
            ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.EndpointRegistryTypedAccess, m_session!.NamespaceUris), m_telemetry!);

        private async Task AssertUnavailableAsync(NodeId group)
        {
            foreach (string name in new[] { XRegistry.BrowseNames.ExternalReference, XRegistry.BrowseNames.GroupUrl })
            {
                NodeId property = await ChildAsync(group, name).ConfigureAwait(false);
                ReadResponse response = await m_session!.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = property, AttributeId = Attributes.Value }], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            }
        }

        private async Task AssertExternalReferenceAsync(NodeId group, ExpandedNodeId target, string application)
        {
            ExpandedNodeId actual = (await ReadChildAsync(group, XRegistry.BrowseNames.ExternalReference).ConfigureAwait(false))
                .GetValue(ExpandedNodeId.Null);
            Assert.That(actual.WithServerIndex(0), Is.EqualTo(target));
            Assert.That(actual.ServerIndex, Is.GreaterThan(0));
            DataValue servers = await m_session!.ReadValueAsync(Ua.VariableIds.Server_ServerArray).ConfigureAwait(false);
            Assert.That(servers.GetValue(ArrayOf<string>.Empty)[checked((int)actual.ServerIndex)], Is.EqualTo(application));
        }

        private async Task<NodeId> LocalGroupAsync()
        {
            NodeId root = ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, m_session!.NamespaceUris);
            NodeId collection = await ChildAsync(root, BrowseNames.MessageGroups).ConfigureAwait(false);
            return await ChildAsync(collection, "federated-local").ConfigureAwait(false);
        }

        private async Task<DataValue> ReadChildAsync(NodeId parent, string name) =>
            await m_session!.ReadValueAsync(await ChildAsync(parent, name).ConfigureAwait(false)).ConfigureAwait(false);

        private async Task<NodeId> ChildAsync(NodeId parent, string name)
        {
            BrowseResponse response = await m_session!.BrowseAsync(null, null, 0,
            [
                new BrowseDescription
                {
                    NodeId = parent, BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences, IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], CancellationToken.None).ConfigureAwait(false);
            ReferenceDescription child = response.Results[0].References.ToArray()!.Single(item => item.BrowseName.Name == name);
            return ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
        }

        private EndpointRegistryNodeManager Registry => m_fixture!.Server.Registry;
        private RegistryNativeHost Host => Registry.Generic!;

        private sealed class GroupServer : ReferenceServer
        {
            public GroupServer(
                ITelemetryContext telemetry, IRegistryStateStore store, EndpointRegistryGroupFederationOptions? options = null)
                : base(telemetry)
            {
                AddNodeManager(new CapturingFactory((server, configuration) =>
                    Registry = new EndpointRegistryNodeManager(server, configuration, new EndpointRegistryServerOptions
                    {
                        Generic = new EndpointRegistryCatalogOptions { Store = store }
                    })));
                if (options is not null)
                {
                    AddNodeManager(new CapturingFactory((server, configuration) =>
                        Integration = new EndpointRegistryGroupFederationNodeManager(server, configuration, options),
                        [EndpointRegistryGroupFederationNodeManager.NamespaceUri]));
                }
            }

            public EndpointRegistryNodeManager Registry { get; private set; } = null!;
            public EndpointRegistryGroupFederationNodeManager? Integration { get; private set; }
        }

        private sealed class CapturingFactory(
            Func<IServerInternal, ApplicationConfiguration, IAsyncNodeManager> create,
            ArrayOf<string> namespaces = default) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => namespaces.Count == 0 ? [Namespaces.EndpointRegistry] : namespaces;
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default) =>
                new(create(server, configuration));
        }

        private sealed class HostingStateStore : IRegistryStateStore
        {
            public bool RejectCommit { get; set; }
            public ValueTask<RegistryStoredState> ReadAsync(CancellationToken cancellationToken = default) =>
                m_memory.ReadAsync(cancellationToken);
            public async ValueTask<RegistryStateCommit> CommitAsync(
                ulong expectedRevision, ByteString document, CancellationToken cancellationToken = default) =>
                RejectCommit ? new RegistryStateCommit(StatusCodes.BadInvalidState,
                    await m_memory.ReadAsync(cancellationToken).ConfigureAwait(false)) :
                    await m_memory.CommitAsync(expectedRevision, document, cancellationToken).ConfigureAwait(false);
            public ValueTask DisposeAsync() => m_memory.DisposeAsync();
            private readonly MemoryRegistryStateStore m_memory = new();
        }

        private sealed class GroupHttpHandler(string metadataUrl, string metadata, string evidence) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(request.RequestUri!.OriginalString == metadataUrl ? metadata : evidence,
                        Encoding.UTF8, "application/json")
                });
        }

        private ITelemetryContext? m_telemetry;
        private string? m_pki;
        private HostingStateStore? m_store;
        private ServerFixture<GroupServer>? m_fixture;
        private ClientFixture? m_client;
        private ISession? m_session;
        private EndpointRegistryGroupFederationBinding? m_binding;
        private FederationGroupSnapshot? m_observation;
        private EndpointRegistryGroupFederationOptions? m_options;
        private Exception? m_observationFailure;
    }
}
