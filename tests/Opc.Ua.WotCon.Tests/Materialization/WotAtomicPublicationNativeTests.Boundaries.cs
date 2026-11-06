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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT atomic publication native.
    /// </summary>
    public sealed partial class WotAtomicPublicationNativeTests
    {
        /// <summary>
        /// Rebuilds native reference images after a real serving-source mutation, without replaying a decision.
        /// </summary>
        /// <param name="conflicts">The number of reference discovery attempts invalidated by a concurrent write.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task RealReferenceReservationsRetryWithinThePredecisionBudget(int conflicts)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            WotResource resource = await AddAsync("real-reference-conflict").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            (object? handle, _) = await m_server.CurrentInstance.NodeManager.GetManagerHandleAsync(
                Ua.ObjectIds.ObjectsFolder, ct).ConfigureAwait(false);
            Assert.That(handle, Is.TypeOf<NodeHandle>());
            NodeState objects = ((NodeHandle)handle!).Node;
            Assert.That(objects.ReferenceExists(
                Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder), Is.False);
            int armed = 0;
            int mutations = 0;
            int attempts = 0;
            var probe = new Mock<IAsyncNodeManager>();
            probe.As<IDisposable>();
            probe.Setup(owner => owner.NamespaceUris).Returns(["urn:wot:reference-conflict-probe"]);
            probe.Setup(owner => owner.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            probe.Setup(owner => owner.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            probe.Setup(owner => owner.GetManagerHandleAsync(
                Ua.ObjectIds.ObjectsFolder, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (Interlocked.Exchange(ref armed, 0) != 0)
                    {
                        if (objects.ReferenceExists(
                            Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder))
                        {
                            Assert.That(objects.RemoveReference(
                                Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder), Is.True);
                        }
                        else
                        {
                            objects.AddReference(Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder);
                        }
                        mutations++;
                    }
                    return new ValueTask<object>(result: null!);
                });
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(probe.Object));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory.Object, null, ct).ConfigureAwait(false);
            var converter = new Mock<IWotDocumentConverter>(MockBehavior.Strict);
            converter.Setup(value => value.ConvertAsync(
                It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns(async (WotResource input, ByteString content, WotRegistrySnapshot snapshot,
                    IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                {
                    WotConversionOutput output = await m_converter.ConvertAsync(
                        input, content, snapshot, contents, token).ConfigureAwait(false);
                    output.NodeSet!.Items![0].References =
                    [
                        new Reference { ReferenceType = "i=35", IsForward = false, Value = "i=85" }
                    ];
                    return output;
                });
            var host = new PausableWotProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle))
            {
                BeforeCommit = () =>
                {
                    Assert.That(m_registry.Current, Is.SameAs(before));
                    attempts++;
                    Volatile.Write(ref armed, attempts <= conflicts ? 1 : 0);
                    m_failDecision = attempts <= conflicts;
                }
            };
            using var coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: converter.Object)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            try
            {
                if (conflicts < 3)
                {
                    WotRefreshResult result = await coordinator.RefreshAsync(
                        HandoffRequest("real-reference-conflict"), ct).ConfigureAwait(false);
                    Assert.That(result.NewGeneration, Is.EqualTo(1u));
                    Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
                    Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
                    Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }
                else
                {
                    await Assert.ThatAsync(async () => await coordinator.RefreshAsync(
                        HandoffRequest("real-reference-conflict"), ct).ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>()
                            .With.Message.EqualTo("Serving references changed during preparation."))
                        .ConfigureAwait(false);
                    Assert.That(coordinator.Generation, Is.Zero);
                    Assert.That(m_registry.Current, Is.SameAs(before));
                    Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                        Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
                Assert.That(attempts, Is.EqualTo(Math.Min(conflicts + 1, 3)));
                Assert.That(mutations, Is.EqualTo(conflicts));
                Assert.That(host.DisposedPublications, Is.EqualTo(attempts));
                Assert.That(objects.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder),
                    Is.EqualTo(conflicts % 2 != 0), "Publication must preserve the unrelated serving-source write.");
            }
            finally
            {
                Volatile.Write(ref armed, 0);
                m_failDecision = false;
                objects.RemoveReference(Ua.ReferenceTypeIds.Organizes, false, Ua.ObjectIds.ViewsFolder);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using IPreparedNodeManagerBatch removal = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration)], cleanup.Token).ConfigureAwait(false);
                NodeManagerBatchResult removed = await removal.CommitAsync(_ => default, cleanup.Token)
                    .ConfigureAwait(false);
                await removed.Retirement.WaitAsync(cleanup.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies bounded conflict retries rebuild and dispose native candidates without publishing a failed attempt.
        /// </summary>
        /// <param name="conflicts">The number of predecision conflicts injected before success.</param>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task PredecisionReferenceConflictsRetryOnlyWithinThePublicationBudget(int conflicts)
        {
            WotResource resource = await AddAsync("conflict").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int attempts = 0;
            var host = new PausableWotProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle))
            {
                BeforeCommit = () =>
                {
                    Assert.That(m_registry.Current, Is.SameAs(before));
                    if (++attempts <= conflicts)
                    {
                        throw new InvalidOperationException("Serving references changed during batch preparation.");
                    }
                }
            };
            using var coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            if (conflicts < 3)
            {
                WotRefreshResult result = await coordinator.RefreshAsync(HandoffRequest("reference-conflict"))
                    .ConfigureAwait(false);

                Assert.That(result.NewGeneration, Is.EqualTo(1u));
                Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
                Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                await Assert.ThatAsync(async () => await coordinator.RefreshAsync(HandoffRequest("reference-conflict"))
                    .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Serving references changed during batch preparation.")).ConfigureAwait(false);

                Assert.That(coordinator.Generation, Is.Zero);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            Assert.That(attempts, Is.EqualTo(Math.Min(conflicts + 1, 3)));
            Assert.That(host.DisposedPublications, Is.EqualTo(attempts));
        }

        /// <summary>
        /// Verifies that an unrelated publication error is propagated immediately without retry.
        /// </summary>
        /// <param name="message">The unrelated or similar-but-not-exact failure diagnostic.</param>
        [TestCase("Unrelated publication failure.")]
        [TestCase("Serving references changed during preparation. Unrelated failure.")]
        [TestCase("Serving references changed during batch preparation. Unrelated failure.")]
        [TestCase("serving references changed during preparation.")]
        public async Task UnrelatedPredecisionFailuresAreNotRetried(string message)
        {
            await AddAsync("unrelated").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            var failure = new InvalidOperationException(message);
            int attempts = 0;
            var host = new PausableWotProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle))
            {
                BeforeCommit = () =>
                {
                    attempts++;
                    throw failure;
                }
            };
            using var coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };

            await Assert.ThatAsync(async () => await coordinator.RefreshAsync(HandoffRequest("unrelated-failure"))
                .ConfigureAwait(false), Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(host.DisposedPublications, Is.EqualTo(1));
            Assert.That(coordinator.Generation, Is.Zero);
            Assert.That(m_registry.Current, Is.SameAs(before));
        }

        /// <summary>
        /// Verifies that a conflict-shaped error after the decision cannot cause a committed publication to replay.
        /// </summary>
        /// <param name="message">The exact reference-conflict diagnostic raised after publication.</param>
        [TestCase("Serving references changed during batch preparation.")]
        [TestCase("Serving references changed during preparation.")]
        public async Task ReferenceConflictAfterDecisionIsNotRetried(string message)
        {
            WotResource resource = await AddAsync("committed").ConfigureAwait(false);
            var failure = new InvalidOperationException(message);
            int attempts = 0;
            var host = new PausableWotProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle))
            {
                BeforeCommit = () => attempts++,
                AfterCommit = () => throw failure
            };
            using var coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };

            await Assert.ThatAsync(async () => await coordinator.RefreshAsync(HandoffRequest("committed-conflict"))
                .ConfigureAwait(false), Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(host.DisposedPublications, Is.EqualTo(1));
            Assert.That(coordinator.Generation, Is.EqualTo(1u));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that native reference scc coactivates but ordering cycle cannot publish.
        /// </summary>
        /// <param name="ordering">The ordering flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task NativeReferenceSccCoactivatesButOrderingCycleCannotPublish(bool ordering)
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            string relation = ordering ? "tm:extends" : "ua:ConnectedTo";
            await SetUnitReferenceAsync(first, "second", relation).ConfigureAwait(false);
            await SetUnitReferenceAsync(second, "first", relation).ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Total, Is.EqualTo(2u));
            Assert.That(result.NewGeneration, Is.EqualTo(ordering ? 0u : 1u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(ordering ? 0u : 2u));
            Assert.That(result.Summary.Failed, Is.EqualTo(ordering ? 2u : 0u));
            Assert.That(m_coordinator.LastRefreshPlan!.AppliedAtomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
            Assert.That(m_coordinator.LastRefreshPlan.UnitCount, Is.EqualTo(1u));
            foreach (WotResource resource in new[] { first, second })
            {
                Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(ordering ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good));
            }
            if (ordering)
            {
                Assert.That(result.Results.All(row => row.Phase == WoTPhaseEnum.DependencyResolution), Is.True);
            }
        }

        /// <summary>
        /// Verifies that stale lifecycle capture cannot prepare and fresh capture can publish.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task StaleLifecycleCaptureCannotPrepareAndFreshCaptureCanPublish()
        {
            var host = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            IWotProjectionPublicationCapture captured = host.CapturePublication();
            const string firstUri = "urn:unit:revision:first";
            const string secondUri = "urn:unit:revision:second";
            var first = new WotProjectionDocument("first",
                [new WotProjectionSource("first", [firstUri], TestNodeSets.XmlBytes(firstUri))]);
            var second = new WotProjectionDocument("second",
                [new WotProjectionSource("second", [secondUri], TestNodeSets.XmlBytes(secondUri))]);
            await host.AddAsync(first).ConfigureAwait(false);
            IWotProjectionPublication stale = await captured.BeginAsync().ConfigureAwait(false);
            await using (stale.ConfigureAwait(false))
            {
                Assert.That(stale.IsCurrent, Is.False);
                await Assert.ThatAsync(async () => await stale.PrepareAsync([WotProjectionChange.Add(second)])
                    .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(secondUri), Is.EqualTo(-1));
            }
            IWotProjectionPublication current = await host.CapturePublication().BeginAsync().ConfigureAwait(false);
            await using (current.ConfigureAwait(false))
            {
                Assert.That(current.IsCurrent, Is.True);
                IWotPreparedProjectionPublication prepared = await current.PrepareAsync([WotProjectionChange.Add(second)])
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    await prepared.CommitAsync(_ => default, () => { }).ConfigureAwait(false);
                    Assert.That(prepared.IsCommitted, Is.True);
                }
            }
            var root = new NodeId(5000u, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(secondUri));
            Assert.That((await ReadNodeClassAsync(root).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that changed lifecycle during acquisition rebuilds before the final plan.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task ChangedLifecycleDuringAcquisitionRebuildsBeforeTheFinalPlan()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveAcquisition(entered, resume);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest("lifecycle-recapture"), timeout.Token).AsTask();
            const string model = "urn:unit:external-revision";
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                var external = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
                await external.AddAsync(new WotProjectionDocument("external",
                    [new WotProjectionSource("external", [model], TestNodeSets.XmlBytes(model))]), timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                resume.TrySetResult(true);
            }

            WotRefreshResult result = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_coordinator.LastRefreshPlan!.RequestId, Is.EqualTo("lifecycle-recapture"));
            Assert.That(m_coordinator.LastRefreshPlan.PreparationGeneration, Is.Zero);
            Assert.That((await ReadNodeClassAsync(Root(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            var externalRoot = new NodeId(5000u, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(model));
            Assert.That((await ReadNodeClassAsync(externalRoot).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that unprepared store is rejected before native lifecycle effects.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task UnpreparedStoreIsRejectedBeforeNativeLifecycleEffects()
        {
            using var registry = new WotRegistryService();
            WotRegistryMutationResult created = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things",
                ResourceId = "unprepared",
                Content = ByteString.From(TestMaterialization.Td("urn:unprepared"))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            WotRegistrySnapshot before = registry.Current;
            var registrations = m_server.NodeManagerLifecycle.Registrations;
            using var coordinator = new WotMaterializationCoordinator(
                registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle), documentConverter: m_converter);

            await Assert.ThatAsync(async () => await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            }).ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNotSupported))
                .ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(coordinator.SupportedAtomicities.IsEmpty, Is.True);
            Assert.That(coordinator.LastRefreshPlan, Is.Null);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(registrations));
        }

        /// <summary>
        /// Verifies that same content missing publication metadata is repaired instead of unchanged.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task SameContentMissingPublicationMetadataIsRepairedInsteadOfUnchanged()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("original")).ConfigureAwait(false);
            WotResource active = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
            await m_registry.ApplyProjectionResultsAsync(
                [new WotResourceProjection(active.GroupId, active.ResourceId,
                    WoTLoadStateEnum.Active, active.ActiveVersionId, active.RefreshGeneration,
                    0, NodeId.Null, active.Validation, active.Diagnostics, active.LastRefreshTime)])
                .ConfigureAwait(false);

            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("metadata-repair"))
                .ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(2u));
            Assert.That(result.Summary.Succeeded, Is.EqualTo(1u));
            Assert.That(result.Summary.Unchanged, Is.Zero);
            WotResource repaired = m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.That(repaired.RootNodeId, Is.EqualTo(Root(resource)));
            Assert.That(repaired.MaterializedNodeCount, Is.EqualTo(active.MaterializedNodeCount));
            Assert.That((await ReadNodeClassAsync(repaired.RootNodeId).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        private async Task SetUnitReferenceAsync(WotResource resource, string target, string relation)
        {
            WotRegistryMutationResult updated = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = "v1",
                Kind = resource.Kind,
                Content = ByteString.From(Encoding.UTF8.GetBytes($$"""
                    {"@context":["https://www.w3.org/2022/wot/td/v1.1",
                      {"ua":"http://opcfoundation.org/UA/","tm":"https://www.w3.org/2019/wot/tm#"}],
                     "id":"urn:{{resource.ResourceId}}","title":"Reference unit",
                     "links":[{"rel":"{{relation}}","href":"urn:{{target}}"}]}
                    """))
            }).ConfigureAwait(false);
            Assert.That(updated.Changed, Is.True, updated.Message);
        }
    }
}
