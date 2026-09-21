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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Public refresh-state contracts through prepared owners with deterministic document conversion.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Platform("Win")]
    public sealed class WotAuthoritativeRefreshStateTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_runtime = await PreparedWotTestRuntime.StartAsync();
            m_registry = await m_runtime.CreateRegistryAsync();
            m_operations.Clear();
            m_converter = new FakeWotDocumentConverter();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_runtime.Observe(changes => m_operations.AddRange(changes.ToList())),
                documentConverter: m_converter);
            m_events.Clear();
            m_identity = null;
            m_coordinator.Event += (_, change) => m_events.Add(change);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_coordinator.Dispose();
            await m_runtime.DisposeAsync();
        }

        [Test]
        public async Task EmptyRefreshDoesNotInventACommittedGeneration()
        {
            WotRefreshResult result = await m_coordinator.RefreshAsync(Request("empty")).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.Zero);
            Assert.That(result.Summary.Generation, Is.Zero);
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(result.Summary.Total, Is.Zero);
            Assert.That(result.Results, Is.Empty);
            WotMaterializationEventArgs completed = Completion();
            Assert.That(completed.Generation, Is.Zero);
            Assert.That(completed.Summary!.Generation, Is.Zero);
            Assert.That(completed.RequestId, Is.EqualTo("empty"));
        }

        [Test]
        public async Task UnchangedRefreshUsesTheCommittedGenerationEverywhere()
        {
            await RegisterAsync("v1").ConfigureAwait(false);
            WotRefreshResult first = await m_coordinator.RefreshAsync(Request("initial")).ConfigureAwait(false);
            Assert.That(first.NewGeneration, Is.EqualTo(1u));
            WotResource active = Resource();
            long metaEpoch = active.MetaEpoch;
            long versionEpoch = active.FindVersion("v1")!.Epoch;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(Request("unchanged")).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(result.Summary.Generation, Is.EqualTo(1u));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(result.Summary.Total, Is.EqualTo(1u));
            Assert.That(result.Summary.Unchanged, Is.EqualTo(1u));
            Assert.That(result.Summary.Succeeded, Is.Zero);
            Assert.That(result.Summary.Failed, Is.Zero);
            Assert.That(result.Summary.Retired, Is.Zero);
            WoTResourceLoadResultDataType row = result.Results.Single();
            Assert.That(row.Generation, Is.EqualTo(1u));
            Assert.That(row.VersionId, Is.EqualTo("v1"));
            Assert.That(row.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(row.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(Resource().RefreshGeneration, Is.EqualTo(1u));
            Assert.That(Resource().RootNodeId, Is.EqualTo(active.RootNodeId));
            Assert.That(Resource().MetaEpoch, Is.EqualTo(metaEpoch));
            Assert.That(Resource().FindVersion("v1")!.Epoch, Is.EqualTo(versionEpoch));
            Assert.That(m_events, Has.Count.EqualTo(1));
            WotMaterializationEventArgs completed = Completion();
            Assert.That(completed.Generation, Is.EqualTo(1u));
            Assert.That(completed.Summary!.Generation, Is.EqualTo(1u));
            Assert.That(completed.Summary.Unchanged, Is.EqualTo(1u));
            Assert.That(completed.RequestId, Is.EqualTo("unchanged"));
        }

        [Test]
        public async Task DryRunPreservesSnapshotAndReportsTheCommittedGeneration()
        {
            await RegisterAsync("v1").ConfigureAwait(false);
            WotRefreshResult first = await m_coordinator.RefreshAsync(Request("initial")).ConfigureAwait(false);
            Assert.That(first.NewGeneration, Is.EqualTo(1u));
            await RegisterAsync("v2").ConfigureAwait(false);
            WotRegistrySnapshot snapshot = m_registry.Current;
            m_events.Clear();
            WotRefreshRequest request = Request("dry-run");
            request.Options.DryRun = true;

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(snapshot));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Summary.Generation, Is.EqualTo(1u));
            WoTResourceLoadResultDataType row = result.Results.Single();
            Assert.That(row.Generation, Is.EqualTo(1u));
            Assert.That(row.VersionId, Is.EqualTo("v2"));
            Assert.That(row.ContentDigest, Is.EqualTo(Resource().FindVersion("v2")!.Digest));
            Assert.That(Resource().ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(Resource().RefreshGeneration, Is.EqualTo(1u));
            Assert.That(m_events.All(change => change.Kind == WotMaterializationEventKind.RefreshCompleted), Is.True);
            foreach (WotMaterializationEventArgs completed in m_events)
            {
                Assert.That(completed.Generation, Is.EqualTo(1u));
                Assert.That(completed.Summary!.Generation, Is.EqualTo(1u));
                Assert.That(completed.RequestId, Is.EqualTo("dry-run"));
            }
        }

        [Test]
        public async Task FailedCandidatePreservesLastValidMetadataAndNamesItsExactVersion()
        {
            await RegisterAsync("v1").ConfigureAwait(false);
            WotRefreshResult first = await m_coordinator.RefreshAsync(Request("initial")).ConfigureAwait(false);
            Assert.That(first.NewGeneration, Is.EqualTo(1u));
            WotResource active = Resource();
            Assert.That(active.RootNodeId.IsNull, Is.False);
            await RegisterAsync("v2").ConfigureAwait(false);
            m_converter.MarkInvalid(Resource().ResourceId);
            long metaEpoch = Resource().MetaEpoch;
            long versionEpoch = Resource().FindVersion("v2")!.Epoch;
            m_events.Clear();

            WotRefreshResult result = await m_coordinator.RefreshAsync(Request("failed-v2")).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(result.Summary.Generation, Is.EqualTo(1u));
            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(result.Summary.Total, Is.EqualTo(1u));
            Assert.That(result.Summary.Failed, Is.EqualTo(1u));
            Assert.That(result.Summary.Succeeded, Is.Zero);
            Assert.That(result.Summary.Retired, Is.Zero);
            WoTResourceLoadResultDataType row = result.Results.Single();
            Assert.That(row.VersionId, Is.EqualTo("v2"));
            Assert.That(row.Generation, Is.EqualTo(1u));
            Assert.That(row.Phase, Is.EqualTo(WoTPhaseEnum.FormatValidation));
            Assert.That(row.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(row.ContentDigest, Is.EqualTo(Resource().FindVersion("v2")!.Digest));
            Assert.That(Resource().ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(Resource().RefreshGeneration, Is.EqualTo(1u));
            Assert.That(Resource().RootNodeId, Is.EqualTo(active.RootNodeId));
            Assert.That(Resource().MaterializedNodeCount, Is.EqualTo(active.MaterializedNodeCount));
            Assert.That(Resource().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            Assert.That(Resource().MetaEpoch, Is.EqualTo(metaEpoch));
            Assert.That(Resource().FindVersion("v2")!.Epoch, Is.EqualTo(versionEpoch));
            WotMaterializationEventArgs failure = m_events.Single(
                change => change.Kind == WotMaterializationEventKind.ValidationFailure);
            Assert.That(failure.VersionId, Is.EqualTo("v2"));
            Assert.That(failure.Generation, Is.EqualTo(1u));
            Assert.That(failure.Phase, Is.EqualTo(WoTPhaseEnum.FormatValidation));
            Assert.That(failure.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(Completion().Generation, Is.EqualTo(1u));
            Assert.That(Completion().Summary!.Failed, Is.EqualTo(1u));
        }

        [Test]
        public async Task ExpectedGenerationMismatchFailsBeforeEffects()
        {
            await RegisterAsync("v1").ConfigureAwait(false);
            WotRefreshResult first = await m_coordinator.RefreshAsync(Request("initial")).ConfigureAwait(false);
            Assert.That(first.NewGeneration, Is.EqualTo(1u));
            WotRegistrySnapshot snapshot = m_registry.Current;
            int operations = m_operations.Count;
            m_events.Clear();
            WotRefreshRequest request = Request("stale");
            request.ExpectedGeneration = 2;
            request.Options.Force = true;

            await Assert.ThatAsync(
                async () => await m_coordinator.RefreshAsync(request).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(snapshot));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_operations, Has.Count.EqualTo(operations));
            Assert.That(m_events, Is.Empty);
        }

        private async Task RegisterAsync(string versionId)
        {
            WotRegistryMutationResult result = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = m_identity?.GroupId ?? WotRegistryGroups.ThingDescriptions,
                ResourceId = m_identity?.ResourceId ?? "sensor",
                VersionId = versionId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:sensor", versionId))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            Assert.That(result.Resource, Is.Not.Null);
            m_identity = result.Resource!;
            string modelUri = $"urn:wot:{m_identity.GroupId}/{m_identity.ResourceId}";
            NamespaceTable namespaces = m_runtime.Namespaces;
            namespaces.GetIndexOrAppend(modelUri);
            m_coordinator.ServerNamespaceUris = namespaces;
            m_converter.SetRootNodeId(m_identity.ResourceId, new ExpandedNodeId(5000u, modelUri));
        }

        private WotResource Resource()
        {
            WotResource identity = m_identity ??
                throw new InvalidOperationException("No test resource was registered.");
            return m_registry.Current.FindResource(identity.GroupId, identity.ResourceId) ??
                throw new InvalidOperationException("The test resource unexpectedly disappeared.");
        }

        private WotMaterializationEventArgs Completion()
        {
            return m_events.Single(change => change.Kind == WotMaterializationEventKind.RefreshCompleted);
        }

        private static WotRefreshRequest Request(string requestId)
        {
            return new WotRefreshRequest
            {
                RequestId = requestId,
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            };
        }

        private readonly List<WotMaterializationEventArgs> m_events = [];
        private WotRegistryService m_registry = null!;
        private readonly List<WotProjectionChange> m_operations = [];
        private PreparedWotTestRuntime m_runtime = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private WotResource? m_identity;
    }
}
