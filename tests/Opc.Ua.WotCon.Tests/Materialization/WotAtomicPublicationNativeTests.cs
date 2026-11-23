// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT atomic publication native.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("NativeTcp")]
    public sealed partial class WotAtomicPublicationNativeTests : IAsyncDisposable
    {
        /// <summary>
        /// Initializes native publication storage, failure injection, and server fixtures for the test.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_disposed = 0;
            m_failDecision = false;
            m_committedWarning = false;
            m_indeterminateDecision = false;
            m_events.Clear();
            m_root = Path.Combine(
                Path.GetTempPath(),
                "apn",
                Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(m_root);
            m_pkiRoot = Path.Combine(m_root, "pki");
            m_storage = new PreparedWotTestStorage(Path.Combine(m_root, "registry"));
            m_store = m_storage.OpenStore(
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    if (m_indeterminateDecision)
                    {
                        File.Move(destination, backup);
                        throw new IOException("The publication decision has no authoritative primary.");
                    }
                    if (m_failDecision)
                    {
                        throw new IOException("The publication decision was conclusively not committed.");
                    }
                    File.Replace(source, destination, backup);
                    if (m_committedWarning)
                    {
                        throw new IOException("The publication committed with uncertain final durability.");
                    }
                });
            Assert.That(m_store.SupportsPreparedCommits, Is.True);
            m_registry = new WotRegistryService(m_store);
            await m_registry.InitializeAsync().ConfigureAwait(false);
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            await WotTestPki.SeedAsync<StandardServer>(m_pkiRoot, telemetry => new StandardServer(telemetry), 0)
                .ConfigureAwait(false);
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_converter = new FakeWotDocumentConverter();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            m_client = await GetSharedClientAsync().ConfigureAwait(false);
            Assert.That(m_client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
            m_session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the native publication fixtures created for the test.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the client configuration shared by every case of the fixture.
        /// </summary>
        [OneTimeTearDown]
        public static async Task DisposeSharedClientAsync()
        {
            ClientFixture? client;
            string? root;
            await s_clientLock.WaitAsync().ConfigureAwait(false);
            try
            {
                client = s_client;
                root = s_clientRoot;
                s_client = null;
                s_clientRoot = null;
            }
            finally
            {
                s_clientLock.Release();
            }
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Verifies that per registry invalid peer leaves every native candidate unpublished.
        /// </summary>
        [Test]
        public async Task PerRegistryInvalidPeerLeavesEveryNativeCandidateUnpublished()
        {
            WotResource good = await AddAsync("good").ConfigureAwait(false);
            WotResource bad = await AddAsync("bad").ConfigureAwait(false);
            NodeId goodRoot = Root(good);
            m_converter.MarkInvalid(bad.ResourceId);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "registry-invalid-peer",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            DataValue value = await ReadNodeClassAsync(goodRoot).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(m_registry.Current.RefreshGeneration, Is.Zero);
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(m_registry.Current.FindResource(good.GroupId, good.ResourceId)!.ActiveVersionId,
                Is.Null.Or.Empty);
            Assert.That(m_registry.Current.FindResource(good.GroupId, good.ResourceId)!.RootNodeId.IsNull, Is.True);
            Assert.That(m_events.Any(change => change.Kind == WotMaterializationEventKind.Resource &&
                change.LoadState == WoTLoadStateEnum.Active), Is.False);
        }

        /// <summary>
        /// Verifies that confirmed store noncommit leaves native routing and metadata unpublished.
        /// </summary>
        [Test]
        public async Task ConfirmedStoreNoncommitLeavesNativeRoutingAndMetadataUnpublished()
        {
            WotResource good = await AddAsync("good").ConfigureAwait(false);
            NodeId goodRoot = Root(good);
            WotRegistrySnapshot before = m_registry.Current;
            m_failDecision = true;

            await Assert.ThatAsync(
                async () => await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = "registry-decision-failure",
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            DataValue value = await ReadNodeClassAsync(goodRoot).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(m_events.Any(change => change.Kind == WotMaterializationEventKind.Resource &&
                change.LoadState == WoTLoadStateEnum.Active), Is.False);
        }

        /// <summary>
        /// Verifies that per registry success and committed warning publish one native generation.
        /// </summary>
        /// <param name="durabilityWarning">The durability-warning flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PerRegistrySuccessAndCommittedWarningPublishOneNativeGeneration(bool durabilityWarning)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            NodeId firstRoot = Root(first);
            NodeId secondRoot = Root(second);
            long storeGeneration = m_registry.Current.Generation;
            m_committedWarning = durabilityWarning;

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "registry-committed",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = firstRoot, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = secondRoot, AttributeId = Attributes.NodeClass }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(storeGeneration + 1));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(m_registry.Current));
            foreach (WotResource original in new[] { first, second })
            {
                WotResource published = m_registry.Current.FindResource(original.GroupId, original.ResourceId)!;
                Assert.That(published.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(published.RefreshGeneration, Is.EqualTo(1u));
                Assert.That(published.RootNodeId, Is.EqualTo(Root(original)));
                Assert.That(published.MetaEpoch, Is.EqualTo(original.MetaEpoch));
            }
            WotMaterializationEventArgs[] active = [.. m_events.Where(change =>
                change.Kind == WotMaterializationEventKind.Resource && change.LoadState == WoTLoadStateEnum.Active)];
            Assert.That(active, Has.Length.EqualTo(2));
            Assert.That(active.All(change => change.Generation == 1 && change.VersionId == "v1" &&
                change.RequestId == "registry-committed"), Is.True);
            if (durabilityWarning)
            {
                Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
                Assert.That(result.Results.All(row => row.Message is { } message &&
                    message.Contains("durability", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "The committed store warning must remain visible, not disappear behind binding warnings.");
            }
        }

        /// <summary>
        /// Verifies that changed failure cannot split the committed native image or block the next publication.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task ChangedFailureCannotSplitTheCommittedNativeImageOrBlockTheNextPublication()
        {
            const string requestId = "registry-notification-failure";
            const string failureMessage = "The publication notification failed once.";
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            long storeGeneration = m_registry.Current.Generation;
            int notifications = 0;
            EventHandler<WotRegistryChangedEventArgs> handler = (_, _) =>
            {
                notifications++;
                if (notifications == 1)
                {
                    throw new InvalidOperationException(failureMessage);
                }
            };
            WotRefreshResult? result = null;
            Exception? reportedFailure = null;
            m_registry.Changed += handler;
            try
            {
                result = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    RequestId = requestId,
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                reportedFailure = failure;
            }
            finally
            {
                m_registry.Changed -= handler;
            }

            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = Root(first), AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = Root(second), AttributeId = Attributes.NodeClass }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(storeGeneration + 1));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(m_registry.Current));
            Assert.That(m_coordinator.CommittedPublication.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(reportedFailure, Is.Null, reportedFailure?.Message);
            Assert.That(result, Is.Not.Null);
            WotRefreshResult committed = result!;
            Assert.That(committed.NewGeneration, Is.EqualTo(1u));
            Assert.That(committed.Summary.Generation, Is.EqualTo(1u));
            Assert.That(committed.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(committed.Results, Has.Length.EqualTo(2));
            Assert.That(committed.Results.All(row => row.Generation == 1 && row.VersionId == "v1"), Is.True);
            Assert.That(committed.Results.Any(row => row.Message is { } message &&
                message.Contains(failureMessage, StringComparison.Ordinal)), Is.True,
                "A committed notification failure must remain explicit in the returned diagnostics.");
            foreach (WotResource original in new[] { first, second })
            {
                WotResource published = m_registry.Current.FindResource(original.GroupId, original.ResourceId)!;
                Assert.That(published.RootNodeId, Is.EqualTo(Root(original)));
                Assert.That(published.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(published.RefreshGeneration, Is.EqualTo(1u));
            }
            WotMaterializationEventArgs completion = m_events.Single(
                change => change.Kind == WotMaterializationEventKind.RefreshCompleted);
            Assert.That(completion.Generation, Is.EqualTo(1u));
            Assert.That(completion.RequestId, Is.EqualTo(requestId));
            Assert.That(completion.Summary!.Generation, Is.EqualTo(1u));
            Assert.That(completion.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));

            WotResource third = await AddAsync("third").ConfigureAwait(false);
            WotRefreshResult next = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "registry-after-notification-failure",
                ExpectedGeneration = 1,
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
            Assert.That(m_coordinator.CommittedPublication.RegistrySnapshot, Is.SameAs(m_registry.Current));
            Assert.That((await ReadNodeClassAsync(Root(third)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                if (m_session is not null)
                {
                    try
                    {
                        await m_session.CloseAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_session.Dispose();
                        m_session = null!;
                    }
                }
            }
            finally
            {
                try
                {
                    if (m_fixture is not null)
                    {
                        await m_fixture.StopAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_server?.Dispose();
                    m_coordinator?.Dispose();
                    m_registry?.Dispose();
                    m_store?.Dispose();
                    m_storage?.Dispose();
                }
            }
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        private async Task<WotResource> AddAsync(string id)
        {
            WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = id,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:" + id))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            WotResource resource = created.Resource ?? throw new InvalidOperationException("No test resource.");
            string model = ModelUri(resource);
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(model);
            m_converter.SetRootNodeId(resource.ResourceId, new ExpandedNodeId(5000u, model));
            return resource;
        }

        /// <summary>
        /// Returns the client configuration shared by every case of the fixture.
        /// </summary>
        /// <remarks>
        /// Loading a client configuration and checking its certificates took
        /// about 400 ms of every case. The configuration is the same for every
        /// case and the client fixture only creates independent sessions from
        /// it, so the cases share one. Every case still opens its own session.
        /// </remarks>
        private static async Task<ClientFixture> GetSharedClientAsync()
        {
            await s_clientLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (s_client is null)
                {
                    string root = Path.Combine(
                        Path.GetTempPath(),
                        "apn",
                        "client",
                        Guid.NewGuid().ToString("N").Substring(0, 12));
                    await WotTestPki.SeedAsync<StandardServer>(root, telemetry => new StandardServer(telemetry), 0)
                        .ConfigureAwait(false);
                    var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                    try
                    {
                        await client.LoadClientConfigurationAsync(root).ConfigureAwait(false);
                    }
                    catch
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                    s_clientRoot = root;
                    s_client = client;
                }
                return s_client;
            }
            finally
            {
                s_clientLock.Release();
            }
        }

        /// <summary>
        /// Starts a second server of the same type as the fixture server, which
        /// stands for the fixture server after a restart.
        /// </summary>
        private static Task<PreparedWotTestRuntime> StartRestartedServerAsync()
        {
            return PreparedWotTestRuntime.StartAsync(telemetry => new StandardServer(telemetry));
        }
        private NodeId Root(WotResource resource)
        {
            return new NodeId(5000u, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(ModelUri(resource)));
        }

        private async Task<DataValue> ReadNodeClassAsync(NodeId nodeId)
        {
            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private static string ModelUri(WotResource resource)
        {
            return $"urn:wot:{resource.GroupId}/{resource.ResourceId}";
        }

        private static readonly SemaphoreSlim s_clientLock = new(1, 1);
        private static ClientFixture? s_client;
        private static string? s_clientRoot;

        private readonly List<WotMaterializationEventArgs> m_events = [];

        private string m_pkiRoot = null!;
        private string m_root = null!;
        private FileWotRegistryStore m_store = null!;
        private PreparedWotTestStorage m_storage = null!;
        private WotRegistryService m_registry = null!;
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "The client is shared by every case and disposed in DisposeSharedClientAsync.")]
        private ClientFixture m_client = null!;
        private Opc.Ua.Client.ISession m_session = null!;
        private bool m_failDecision;
        private bool m_committedWarning;
        private bool m_indeterminateDecision;
        private int m_disposed;
    }
}
