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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("Integration")]
    [Category("NativeTcp")]
    [NonParallelizable]
    public sealed class WotAtomicPublicationNativeTests : IAsyncDisposable
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_disposed = 0;
            m_failDecision = false;
            m_committedWarning = false;
            m_events.Clear();
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotAtomicPublicationNativeTests),
                Guid.NewGuid().ToString("N"));
            m_store = new FileWotRegistryStore(
                Path.Combine(m_root, "registry"),
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
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
            m_registry = new WotRegistryService(m_store);
            await m_registry.InitializeAsync().ConfigureAwait(false);
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(Path.Combine(m_root, "pki")).ConfigureAwait(false);
            m_converter = new FakeWotDocumentConverter();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            m_client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "pki")).ConfigureAwait(false);
            Assert.That(m_client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
            m_session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await DisposeAsync().ConfigureAwait(false);
        }

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
                    if (m_client is not null)
                    {
                        await m_client.DisposeAsync().ConfigureAwait(false);
                    }
                    m_coordinator?.Dispose();
                    m_registry?.Dispose();
                    m_store?.Dispose();
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

        private readonly List<WotMaterializationEventArgs> m_events = [];
        private string m_root = null!;
        private FileWotRegistryStore m_store = null!;
        private WotRegistryService m_registry = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private ClientFixture m_client = null!;
        private Opc.Ua.Client.ISession m_session = null!;
        private bool m_failDecision;
        private bool m_committedWarning;
        private int m_disposed;
    }
}
