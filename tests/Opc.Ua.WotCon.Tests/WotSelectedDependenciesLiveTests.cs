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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;
using INodeManagerLifecycle = Opc.Ua.Server.INodeManagerLifecycle;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotSelectedDependenciesLiveTests
    {
        [Test]
        public void DependencySnapshotCapabilityIsTheRegisteredOwner()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryServer();
            using ServiceProvider provider = services.BuildServiceProvider();
            IWotRegistryService registry = provider.GetRequiredService<IWotRegistryService>();
            IWotRegistryDependencySnapshotProvider capability =
                provider.GetRequiredService<IWotRegistryDependencySnapshotProvider>();

            Assert.That(capability, Is.SameAs(registry));
            Assert.That(capability.SupportsDependencySnapshots, Is.True);
        }

        [Test]
        public void OlderProvidersDoNotReceiveAFabricatedSnapshotCapability()
        {
            IWotRegistryService older = new Mock<IWotRegistryService>().Object;
            var services = new ServiceCollection();
            services.AddSingleton(older);
            services.AddOpcUa().AddWotRegistryServer();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<IWotRegistryService>(), Is.SameAs(older));
            Assert.That(older, Is.Not.InstanceOf<IWotRegistryDependencySnapshotProvider>());
            Assert.That(() => provider.GetRequiredService<IWotRegistryDependencySnapshotProvider>(),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("The registered registry does not support dependency snapshots."));
        }

        [Test]
        public async Task NativeUnconfiguredOriginReportsUnsupportedWithoutInventingPins()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered source = await fixture.RegisterAsync("urn:b2:unconfigured");
            fixture.Coordinator.RegistryOrigin = null;

            DataValue value = await fixture.ReadAsync(source.Version, "DependencySnapshot");

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public void WireEdgesIdentifyExactVersionsWithoutChangingTheCapturedResourceGraph()
        {
            ByteString digest = WotContentDigest.Compute("original"u8);
            var snapshot = new WotDependencySnapshot(
                "v1", 1, "wire", DateTime.UtcNow, true, digest,
                [new WotDependency("/source", "model:Pump", "/model", "tm:extends", true)],
                [new WotDependencyTargetPin(
                    0, new WotRegistryOrigin("urn:b2:registry"), "/model/versions/v1",
                    "https://example.test/models/Pump", ExpandedNodeId.Null, digest)]);

            WoTDependencySnapshotDataType wire = snapshot.ToDataType();

            Assert.Multiple(() =>
            {
                Assert.That(wire.Edges[0].TargetXid, Is.EqualTo("/model/versions/v1"));
                Assert.That(wire.Edges[0].TargetUri, Is.EqualTo("model:Pump"));
                Assert.That(snapshot.Edges[0].TargetXid, Is.EqualTo("/model"));
            });
        }

        [Test]
        public async Task NativePropertiesWaitForActualActivation()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered source = await fixture.RegisterAsync("urn:b2:waiting");

            DataValue committed = await fixture.ReadAsync(source.Version, "DependencySnapshot");
            DataValue attempted = await fixture.ReadAsync(source.Version, "LastDependencyAttempt");

            Assert.Multiple(() =>
            {
                Assert.That(committed.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                Assert.That(attempted.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
                Assert.That(committed.WrappedValue.IsNull, Is.True);
                Assert.That(attempted.WrappedValue.IsNull, Is.True);
            });
        }

        [Test]
        public async Task GeneratedRefreshPinsAuthoritativeOriginContextAndExactSelectedVersion()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered model = await fixture.RegisterAsync(
                "https://example.test/models/Pump", model: true);
            Registered source = await fixture.RegisterAsync(
                "urn:b2:source", links: """[{"rel":"tm:extends","href":"model:Pump"}]""");
            Registered newer = await fixture.RegisterAsync("urn:b2:source", "v2");
            await newer.Allocation.Version.SetDefaultVersionAsync("v2", 0);
            await model.Allocation.Version.SetEnabledAsync(false, 0);
            Registered independent = await fixture.RegisterAsync("urn:b2:independent");
            fixture.Blobs.Reads.Clear();

            var result = await fixture.RefreshAsync([source.Selector], "exact-context");
            Assert.That(result.Results[0].LoadState, Is.EqualTo(WoTLoadStateEnum.Active),
                $"Phase={result.Results[0].Phase}; {result.Results[0].Message}");
            WoTDependencySnapshotDataType snapshot = await fixture.SnapshotAsync(source.Version);
            string sourceXid = await fixture.StringAsync(source.Logical, "Xid", XRegistryNamespace);
            string targetXid = await fixture.StringAsync(model.Version, "Xid", XRegistryNamespace);

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Count, Is.EqualTo(1));
                Assert.That(result.Results[0].VersionId, Is.EqualTo("v1"));
                Assert.That(result.Results[0].LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(snapshot.SourceVersionId, Is.EqualTo("v1"));
                Assert.That(snapshot.RequestId, Is.EqualTo("exact-context"));
                Assert.That(snapshot.Generation, Is.EqualTo(result.NewGeneration));
                Assert.That(snapshot.IsCommitted, Is.True);
                Assert.That(snapshot.EffectiveInputDigest.Length, Is.EqualTo(32));
                Assert.That(snapshot.Edges.Count, Is.EqualTo(1));
                Assert.That(snapshot.Edges[0].SourceXid, Is.EqualTo(sourceXid));
                Assert.That(snapshot.Edges[0].TargetUri, Is.EqualTo("model:Pump"));
                Assert.That(snapshot.Edges[0].TargetXid, Is.EqualTo(targetXid));
                Assert.That(snapshot.Targets[0].VersionXid, Is.EqualTo(targetXid));
                Assert.That(snapshot.Targets[0].VersionNodeId,
                    Is.EqualTo(NodeId.ToExpandedNodeId(model.Version, fixture.Client.Session.NamespaceUris)));
                Assert.That(snapshot.Targets[0].OriginRegistry.OriginUri, Is.Empty);
                Assert.That(snapshot.Targets[0].OriginRegistry.ServerUri,
                    Is.EqualTo(fixture.Client.Session.ConfiguredEndpoint.Description.Server.ApplicationUri));
                Assert.That(snapshot.Targets[0].OriginRegistry.RegistryNodeId, Is.EqualTo(ObjectIds.WoTRegistry));
                Assert.That(snapshot.Targets[0].DocumentUri, Is.EqualTo("https://example.test/models/Pump"));
                Assert.That(snapshot.Targets[0].ContentDigest, Is.EqualTo(WotContentDigest.Compute(model.Content)));
                Assert.That(fixture.Blobs.Reads, Does.Not.Contain(independent.Digest));
            });
            Assert.That(await fixture.StringAsync(model.Version, "ActiveVersionId"), Is.Empty);
            Assert.That((await fixture.ReadAsync(newer.Version, "DependencySnapshot")).StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That((await fixture.ReadAsync(source.Logical, "DependencySnapshot")).StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(await source.Allocation.Version.DownloadAsync(), Is.EqualTo(source.Content));
        }

        [Test]
        public async Task NativeNoopFailureAndDryRunKeepTheCommittedGraphDistinct()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered source = await fixture.RegisterAsync("urn:b2:attempts");
            var first = await fixture.RefreshAsync([source.Selector], "committed");
            WoTDependencySnapshotDataType committed = await fixture.SnapshotAsync(source.Version);

            var noop = await fixture.RefreshAsync([source.Selector], "noop");
            WoTDependencySnapshotDataType last = await fixture.SnapshotAsync(source.Version, attempt: true);
            Assert.Multiple(() =>
            {
                Assert.That(noop.Results[0].Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
                Assert.That(last.RequestId, Is.EqualTo("noop"));
                Assert.That(last.IsCommitted, Is.False);
                Assert.That(last.EffectiveInputDigest, Is.EqualTo(committed.EffectiveInputDigest));
                Assert.That(noop.NewGeneration, Is.EqualTo(first.NewGeneration));
            });
            await fixture.RefreshAsync([source.Selector], "dry", force: true, dryRun: true);
            Assert.That((await fixture.SnapshotAsync(source.Version)).RequestId, Is.EqualTo("committed"));
            Assert.That((await fixture.SnapshotAsync(source.Version, attempt: true)).RequestId, Is.EqualTo("noop"));

            fixture.Blobs.FailNextDigest = source.Digest;
            var failed = await fixture.RefreshAsync([source.Selector], "failed", force: true);
            WoTDependencySnapshotDataType after = await fixture.SnapshotAsync(source.Version);
            WoTDependencySnapshotDataType attempt = await fixture.SnapshotAsync(source.Version, attempt: true);
            Assert.Multiple(() =>
            {
                Assert.That(failed.Results[0].Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(failed.Results[0].Phase, Is.EqualTo(WoTPhaseEnum.Fetch));
                Assert.That(after.RequestId, Is.EqualTo("committed"));
                Assert.That(after.EffectiveInputDigest, Is.EqualTo(committed.EffectiveInputDigest));
                Assert.That(attempt.RequestId, Is.EqualTo("failed"));
                Assert.That(attempt.IsCommitted, Is.False);
                Assert.That(attempt.EffectiveInputDigest.Length, Is.Zero);
                Assert.That(attempt.Generation, Is.EqualTo(first.NewGeneration));
            });
        }

        [Test]
        public async Task NativeAcquisitionFailureDoesNotAbortIndependentSelectedWork()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered a = await fixture.RegisterAsync("urn:b2:independent-a");
            Registered b = await fixture.RegisterAsync("urn:b2:independent-b");
            Registered c = await fixture.RegisterAsync("urn:b2:unselected-c");
            fixture.Blobs.Reads.Clear();
            fixture.Blobs.FailNextDigest = b.Digest;

            var result = await fixture.RefreshAsync([a.Selector, b.Selector], "independent");
            WoTDependencySnapshotDataType success = await fixture.SnapshotAsync(a.Version);
            WoTDependencySnapshotDataType failure = await fixture.SnapshotAsync(b.Version, attempt: true);

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.ToList().Single(row =>
                    row.ResourceId == a.Allocation.Version.ResourceId).LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(result.Results.ToList().Single(row =>
                    row.ResourceId == b.Allocation.Version.ResourceId).Phase, Is.EqualTo(WoTPhaseEnum.Fetch));
                Assert.That(success.IsCommitted, Is.True);
                Assert.That(failure.IsCommitted, Is.False);
                Assert.That(failure.EffectiveInputDigest.Length, Is.Zero);
                Assert.That(fixture.Blobs.Reads, Does.Not.Contain(c.Digest));
            });
            Assert.That((await fixture.ReadAsync(b.Version, "DependencySnapshot")).StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
        }

        [Test]
        public async Task NativeCapturedVersionsSurviveDefaultChangeReloadUntilOperationRelease()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync(maxVersions: 2);
            Registered model = await fixture.RegisterAsync("https://example.test/models/Pump", model: true);
            Registered source = await fixture.RegisterAsync(
                "urn:b2:leased", links: """[{"rel":"tm:extends","href":"model:Pump"}]""");
            Registered nextModel = await fixture.RegisterAsync(
                "https://example.test/models/Pump", "v2", model: true);
            await model.Allocation.Version.SetEnabledAsync(false, 0);
            fixture.Host.Pause();
            Task<(WoTRefreshSummaryDataType Summary, ArrayOf<WoTResourceLoadResultDataType> Results,
                uint NewGeneration)> refresh = fixture.RefreshAsync([source.Selector], "leased");
            try
            {
                await Task.WhenAny(fixture.Host.Entered, refresh).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.That(fixture.Host.Entered.IsCompleted, Is.True, "The selected closure must reach its real host.");
                await nextModel.Allocation.Version.SetDefaultVersionAsync("v2", 0);
                await fixture.Registry.InitializeAsync();
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await model.Group.CreateDocumentResourceAsync(
                        "https://example.test/models/Pump", "v3"))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(await model.Allocation.Version.DownloadAsync(), Is.EqualTo(model.Content));
            }
            finally
            {
                fixture.Host.Release();
                await refresh;
            }
            Assert.That((await refresh).Results[0].LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            WoTDependencySnapshotDataType captured = await fixture.SnapshotAsync(source.Version);
            Assert.That(captured.Targets[0].ContentDigest, Is.EqualTo(WotContentDigest.Compute(model.Content)));
            Assert.That(captured.Targets[0].VersionXid,
                Is.EqualTo(await fixture.StringAsync(model.Version, "Xid", XRegistryNamespace)));

            WotRegistryResourceAllocation third = await model.Group.CreateDocumentResourceAsync(
                "https://example.test/models/Pump", "v3");
            await third.Version.Proxy.UploadAsync(model.Content);
            await fixture.Registry.InitializeAsync();
            WoTDependencySnapshotDataType retained = await fixture.SnapshotAsync(source.Version);
            Assert.Multiple(() =>
            {
                Assert.That(retained.Targets[0].VersionXid, Is.EqualTo(captured.Targets[0].VersionXid));
                Assert.That(retained.Targets[0].ContentDigest, Is.EqualTo(captured.Targets[0].ContentDigest));
                Assert.That(retained.EffectiveInputDigest, Is.EqualTo(captured.EffectiveInputDigest));
            });
            Assert.That(fixture.Registry.Current.FindResource(
                model.Group.GroupId, model.Allocation.Version.ResourceId)!.FindVersion("v1"), Is.Null);
        }

        [Test]
        public async Task NativeReciprocalEdgesActivateOneSemanticComponent()
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            string referenceId = NodeId.ToExpandedNodeId(
                Ua.ReferenceTypeIds.IsPhysicallyConnectedTo, fixture.Client.Session.NamespaceUris).ToString();
            Registered a = await fixture.RegisterAsync(
                "urn:b2:cycle-a", links: $$"""
                    [{"rel":"ua:ConnectedTo","href":"urn:b2:cycle-b","uav:refId":"{{referenceId}}"}]
                    """);
            Registered b = await fixture.RegisterAsync(
                "urn:b2:cycle-b", links: $$"""
                    [{"rel":"ua:ConnectedTo","href":"urn:b2:cycle-a","uav:refId":"{{referenceId}}"}]
                    """);

            var result = await fixture.RefreshAsync([a.Selector], "scc");
            Assert.That(result.Results.ToList().All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True,
                string.Join("; ", result.Results.ToList().Select(row => $"{row.Phase}: {row.Message}")));
            WoTDependencySnapshotDataType snapshot = await fixture.SnapshotAsync(a.Version);
            string aXid = await fixture.StringAsync(a.Version, "Xid", XRegistryNamespace);
            string bXid = await fixture.StringAsync(b.Version, "Xid", XRegistryNamespace);
            string[] expectedTargets = [aXid, bXid];
            string[] expectedUris = ["urn:b2:cycle-a", "urn:b2:cycle-b"];

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Count, Is.EqualTo(2));
                Assert.That(result.Results.ToList().All(row => row.LoadState == WoTLoadStateEnum.Active), Is.True);
                Assert.That(snapshot.Edges.ToList().Select(edge => edge.TargetUri), Is.EquivalentTo(expectedUris));
                Assert.That(snapshot.Targets.ToList().Select(target => target.VersionXid),
                    Is.EquivalentTo(expectedTargets));
                Assert.That(fixture.Host.Documents, Has.Count.EqualTo(1));
            });
            Assert.That((await fixture.SnapshotAsync(b.Version)).Generation, Is.EqualTo(snapshot.Generation));
        }

        [TestCase("DependencySnapshot")]
        [TestCase("LastDependencyAttempt")]
        public async Task NativeObservationDisclosureUsesTheExistingAuthorizationBoundary(string property)
        {
            await using LiveFixture fixture = await LiveFixture.CreateAsync();
            Registered source = await fixture.RegisterAsync("urn:b2:authorization");
            await fixture.RefreshAsync([source.Selector], "authorized");
            NodeId propertyId = await fixture.PropertyAsync(source.Version, property);
            fixture.Options.ManagementAccess = new WotManagementAccessPolicy
            {
                MinimumSecurityMode = MessageSecurityMode.None,
                AllowAnonymous = false,
                RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
            };

            DataValue denied = await fixture.ReadNodeAsync(propertyId);
            ServiceResultException refresh = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.RefreshAsync([source.Selector], "denied"))!;

            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(denied.WrappedValue.IsNull, Is.True);
                Assert.That(refresh.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            });
            fixture.Options.ManagementAccess = new WotManagementAccessPolicy
            {
                MinimumSecurityMode = MessageSecurityMode.None,
                AllowAnonymous = true,
                RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
            };
            Assert.That((await fixture.SnapshotAsync(source.Version)).RequestId, Is.EqualTo("authorized"));
        }

        private sealed class Registered
        {
            public Registered(
                WotRegistryGroupClient group, WotRegistryResourceAllocation allocation,
                ByteString content, string versionId)
            {
                Group = group;
                Allocation = allocation;
                Content = content;
                m_versionId = versionId;
            }

            public WotRegistryGroupClient Group { get; }
            public WotRegistryResourceAllocation Allocation { get; }
            public ByteString Content { get; }
            public NodeId Version => Allocation.Version.ResourceNodeId;
            public NodeId Logical => Allocation.LogicalResource.ResourceNodeId;
            public string Digest => WotContentDigest.ToHex(WotContentDigest.Compute(Content));
            public WoTResourceSelectorDataType Selector => new()
            {
                Kind = Group.Kind,
                GroupId = Group.GroupId,
                ResourceId = Allocation.Version.ResourceId,
                VersionId = m_versionId
            };

            private readonly string m_versionId;
        }

        private sealed class LiveFixture : IAsyncDisposable
        {
            public WotRegistryClient Client { get; private set; } = null!;
            public WotRegistryService Registry { get; private set; } = null!;
            public WotRegistryServerOptions Options { get; private set; } = null!;
            public ObservedBlobs Blobs { get; } = new();
            public ObservedHost Host { get; private set; } = null!;
            public WotMaterializationCoordinator Coordinator =>
                m_services!.GetRequiredService<WotMaterializationCoordinator>();

            public static async Task<LiveFixture> CreateAsync(int maxVersions = 8)
            {
                var fixture = new LiveFixture();
                bool initialized = false;
                try
                {
                    await fixture.StartAsync(maxVersions);
                    initialized = true;
                    return fixture;
                }
                finally
                {
                    if (!initialized)
                    {
                        await fixture.DisposeAsync();
                    }
                }
            }

            public async Task<Registered> RegisterAsync(
                string id, string version = "v1", bool model = false, string links = "[]")
            {
                WoTDocumentKindEnum kind = model
                    ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription;
                (WotRegistryGroupClient group, _) = await Client.GetOrCreateDocumentGroupAsync(
                    kind, model ? "urn:b2:models" : "urn:b2:things");
                WotRegistryResourceAllocation allocation = await group.CreateDocumentResourceAsync(id, version);
                string type = model ? "\"@type\":\"tm:ThingModel\"," : "\"@type\":\"uav:object\",";
                ByteString content = ByteString.From(Encoding.UTF8.GetBytes($$$"""
                    {
                      "@context":["https://www.w3.org/2022/wot/td/v1.1",
                        {"tm":"https://www.w3.org/2019/wot/tm#",
                         "ua":"http://opcfoundation.org/UA/","model":"https://example.test/models/"}],
                      {{{type}}}"id":"{{{id}}}","title":"{{{version}}}",
                      "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},"security":["nosec_sc"],
                      "links":{{{links}}}
                    }
                    """));
                await allocation.Version.Proxy.UploadAsync(content);
                await allocation.Version.SetEnabledAsync(true, 0);
                return new Registered(group, allocation, content, version);
            }

            public async Task<(WoTRefreshSummaryDataType Summary,
                ArrayOf<WoTResourceLoadResultDataType> Results, uint NewGeneration)> RefreshAsync(
                ArrayOf<WoTResourceSelectorDataType> selection, string requestId,
                bool force = false, bool dryRun = false)
            {
                return await Client.Proxy.RefreshAsync(
                    selection, new WoTRefreshOptionsDataType
                    {
                        Atomicity = WoTAtomicityEnum.PerClosure, Force = force, DryRun = dryRun
                    }, 0, requestId);
            }

            public Task<NodeId> PropertyAsync(NodeId owner, string name, string namespaceUri = Namespaces.WotCon)
            {
                return WotConBrowsePathResolver.ResolveChildAsync(
                    Client.Session, owner, Ua.ReferenceTypeIds.HasProperty,
                    Client.Session.NamespaceUris.GetIndexOrAppend(namespaceUri),
                    name, StatusCodes.BadNodeIdUnknown, "Required native dependency Property is missing.",
                    default, requireUnique: true)
                    .AsTask();
            }

            public async Task<DataValue> ReadAsync(NodeId owner, string name, string namespaceUri = Namespaces.WotCon)
            {
                return await ReadNodeAsync(await PropertyAsync(owner, name, namespaceUri));
            }

            public async Task<DataValue> ReadNodeAsync(NodeId nodeId)
            {
                ReadResponse result = await Client.Session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }], default);
                Assert.That(result.Results.Count, Is.EqualTo(1));
                return result.Results[0];
            }

            public async Task<string> StringAsync(
                NodeId owner, string name, string namespaceUri = Namespaces.WotCon)
            {
                DataValue value = await ReadAsync(owner, name, namespaceUri);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out string text), Is.True);
                return text;
            }

            public async Task<WoTDependencySnapshotDataType> SnapshotAsync(NodeId owner, bool attempt = false)
            {
                DataValue value = await ReadAsync(owner, attempt ? "LastDependencyAttempt" : "DependencySnapshot");
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                WoTDependencySnapshotDataType? snapshot;
                Assert.That(value.WrappedValue.TryGetStructure<WoTDependencySnapshotDataType>(
                    Client.Session.MessageContext, out snapshot), Is.True);
                return snapshot!;
            }

            public async ValueTask DisposeAsync()
            {
                Host?.Release();
                try
                {
                    if (m_managed is not null)
                    {
                        await m_managed.DisposeAsync();
                    }
                }
                finally
                {
                    try
                    {
                        if (m_session is not null)
                        {
                            await m_session.CloseAsync();
                        }
                    }
                    finally
                    {
                        try
                        {
                            m_session?.Dispose();
                            if (m_serverFixture is not null)
                            {
                                await m_serverFixture.StopAsync();
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (m_clientFixture is not null)
                                {
                                    await m_clientFixture.DisposeAsync();
                                }
                            }
                            finally
                            {
                                try
                                {
                                    if (m_services is not null)
                                    {
                                        await m_services.DisposeAsync();
                                    }
                                }
                                finally
                                {
                                    if (Directory.Exists(m_root))
                                    {
                                        Directory.Delete(m_root, recursive: true);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private async Task StartAsync(int maxVersions)
            {
                string? expected = Environment.GetEnvironmentVariable("WOT_B2_EXPECTED_RUNTIME");
                if (!string.IsNullOrEmpty(expected))
                {
                    Assert.That(Environment.Version.ToString(), Is.EqualTo(expected));
                }
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_root = Path.Combine(Path.GetTempPath(), "wot-b2-native", Guid.NewGuid().ToString("N"));
                m_serverFixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    UriScheme = Utils.UriSchemeOpcTcp, SecurityNone = true, AutoAccept = true
                };
                ReferenceServer server = await m_serverFixture.StartAsync(m_root);
                var services = new ServiceCollection();
                services.AddSingleton(telemetry);
                services.AddSingleton(server.NodeManagerLifecycle);
                services.AddSingleton<IXRegistryResourceStore>(Blobs);
                services.AddOpcUa().AddWotRegistryServer(options =>
                {
                    options.StorageFolder = Path.Combine(m_root, "registry");
                    options.AutoRefresh = false;
                    options.Bounds.MaxVersionsPerResource = maxVersions;
                    options.ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.None,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    };
                }).AddWotRegistryClient();
                services.AddSingleton<IWotProjectionHost>(provider =>
                    new ObservedHost(new LifecycleWotProjectionHost(
                        provider.GetRequiredService<INodeManagerLifecycle>(),
                        provider.GetRequiredService<IWotProjectionBindingRuntimeFactory>())));
                m_services = services.BuildServiceProvider();
                Options = m_services.GetRequiredService<WotRegistryServerOptions>();
                Registry = (WotRegistryService)m_services.GetRequiredService<IWotRegistryService>();
                Host = (ObservedHost)m_services.GetRequiredService<IWotProjectionHost>();
                await server.NodeManagerLifecycle.AddAsync(
                    m_services.GetRequiredService<WotRegistryNodeManagerFactory>(), null);
                m_clientFixture = new ClientFixture(false, false, telemetry);
                await m_clientFixture.LoadClientConfigurationAsync(m_root);
                m_session = await m_clientFixture.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None);
                m_managed = await ManagedSession.CreateAsync(
                    m_clientFixture.Config, m_clientFixture.Endpoint, m_clientFixture.SessionFactory,
                    telemetry: telemetry);
                Client = await m_services.GetRequiredService<
                    Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>()(m_managed, default);
            }

            private string m_root = string.Empty;
            private ServerFixture<ReferenceServer>? m_serverFixture;
            private ClientFixture? m_clientFixture;
            private ISession? m_session;
            private ManagedSession? m_managed;
            private ServiceProvider? m_services;
        }

        private sealed class ObservedBlobs : IXRegistryResourceStore
        {
            public ConcurrentQueue<string> Reads { get; } = new();
            public string? FailNextDigest { get; set; }

            public ValueTask<ByteString> ReadAsync(string resourceKey, long offset, int count, CancellationToken ct)
            {
                Reads.Enqueue(resourceKey);
                if (resourceKey == FailNextDigest)
                {
                    FailNextDigest = null;
                    throw new IOException("Injected B2 exact-input acquisition failure.");
                }
                return m_inner.ReadAsync(resourceKey, offset, count, ct);
            }

            public ValueTask WriteAsync(string resourceKey, long offset, ByteString data, CancellationToken ct)
            {
                return m_inner.WriteAsync(resourceKey, offset, data, ct);
            }

            public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct)
            {
                return m_inner.DeleteAsync(resourceKey, ct);
            }

            private readonly InMemoryResourceStore m_inner = new();
        }

        private sealed class ObservedHost : IWotProjectionHost
        {
            public ObservedHost(IWotProjectionHost inner)
            {
                m_inner = inner;
            }

            public ConcurrentQueue<WotProjectionDocument> Documents { get; } = new();
            public Task Entered => m_entered.Task;

            public void Pause()
            {
                m_release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void Release()
            {
                m_release?.TrySetResult(true);
            }

            public async ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                Documents.Enqueue(document);
                if (m_release is { } release)
                {
                    m_entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
                return await m_inner.AddAsync(document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ShadowReloadAsync(current, document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ImmediateReloadAsync(current, document, cancellationToken);
            }

            public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                return m_inner.RemoveAsync(handle, cancellationToken);
            }

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly IWotProjectionHost m_inner;
            private TaskCompletionSource<bool>? m_release;
        }

        private const string XRegistryNamespace = "http://opcfoundation.org/UA/xRegistry/";
    }
}
