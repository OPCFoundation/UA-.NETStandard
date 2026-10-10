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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [Test]
        [Platform("Win")]
        public async Task InvocationKeepsRegistryMutationAfterEveryNativeUnit()
        {
            HandoffProbe probe = ObserveHandoff();
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            WotResource second = await AddAsync("second").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var mutations = new List<Task>();
            EventHandler<WotRegistryChangedEventArgs> observe = (_, change) =>
            {
                if (change.ProjectionOnly && change.Current.RefreshGeneration == 1 && mutations.Count == 0)
                {
                    mutations.Add(m_registry.SetEnabledAsync(second.GroupId, second.ResourceId, false).AsTask());
                }
            };
            probe.BeforeDecisionAsync = async _ =>
            {
                if (probe.DecisionCount == 1)
                {
                    entered.TrySetResult(true);
                    await resume.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            m_registry.Changed += observe;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "isolated-registry",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }, timeout.Token).AsTask();
            WotRefreshResult? result = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(mutations, Has.Count.EqualTo(1));
                Assert.That(mutations[0].IsCompleted, Is.False);
                Assert.That(m_registry.Current.FindResource(second.GroupId, second.ResourceId)!.Enabled, Is.True);
                Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(1u));
            }
            finally
            {
                resume.TrySetResult(true);
                m_registry.Changed -= observe;
                result = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                foreach (Task mutation in mutations)
                {
                    await mutation.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Assert.That(result!.NewGeneration, Is.EqualTo(2u));
            Assert.That(m_registry.Current.FindResource(second.GroupId, second.ResourceId)!.Enabled, Is.False);
            Assert.That((await ReadNodeClassAsync(Root(first)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((await ReadNodeClassAsync(Root(second)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task InvocationRejectsConflictingLifecycleMutationBeforeNativeEffects()
        {
            HandoffProbe probe = ObserveHandoff();
            await AddAsync("first").ConfigureAwait(false);
            await AddAsync("second").ConfigureAwait(false);
            const string modelUri = "urn:wot:external-publication";
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(modelUri);
            var externalRoot = new NodeId(5000u, ns);
            var external = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            var document = new WotProjectionDocument("external",
                [new WotProjectionSource("external", [modelUri], TestNodeSets.XmlBytes(modelUri))]);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task<WotProjectionHandle>? mutation = null;
            EventHandler<WotRegistryChangedEventArgs> observe = (_, change) =>
            {
                if (change.ProjectionOnly && change.Current.RefreshGeneration == 1 && mutation is null)
                {
                    mutation = external.AddAsync(document, timeout.Token).AsTask();
                }
            };
            probe.BeforeDecisionAsync = async _ =>
            {
                if (probe.DecisionCount == 1)
                {
                    entered.TrySetResult(true);
                    await resume.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            };
            m_registry.Changed += observe;
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerResource }
            }, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(mutation, Is.Not.Null);
                await Assert.ThatAsync(() => mutation!.WaitAsync(timeout.Token),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadServerTooBusy)).ConfigureAwait(false);
                Assert.That((await ReadNodeClassAsync(externalRoot).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            finally
            {
                resume.TrySetResult(true);
                m_registry.Changed -= observe;
                await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            WotProjectionHandle accepted = await external.AddAsync(document, timeout.Token).ConfigureAwait(false);
            Assert.That(accepted.Registration, Is.Not.Null);
            Assert.That((await ReadNodeClassAsync(externalRoot).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task ZeroExpectedGenerationRebuildsChangedCapturedVersionBeforePublication()
        {
            WotResource resource = await AddAsync("first").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveAcquisition(entered, resume);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(
                HandoffRequest("stale-zero"), timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await UpdateHandoffResourceAsync(resource).ConfigureAwait(false);
            }
            finally
            {
                resume.TrySetResult(true);
            }

            WotRefreshResult result = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            Assert.That(result.Results[0].VersionId, Is.EqualTo("v2"));
            Assert.That(result.Results[0].ContentDigest,
                Is.EqualTo(m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.DefaultVersion!.Digest));
            Assert.That(m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.ActiveVersionId,
                Is.EqualTo("v2"));
            Assert.That((await ReadNodeClassAsync(NewHandoffNode(resource)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Platform("Win")]
        public async Task StaleNonzeroGenerationFailsAfterCaptureBeforeAnyInvocationPublication()
        {
            WotResource first = await AddAsync("first").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("initial")).ConfigureAwait(false);
            WotResource external = await AddAsync("external").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObserveAcquisition(entered, resume);
            WotRefreshRequest request = HandoffRequest("stale-nonzero", 1, force: true);
            request.Selection = [UnitSelector(first)];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task<WotRefreshResult> pending = m_coordinator.RefreshAsync(request, timeout.Token).AsTask();
            WotRegistrySnapshot? published = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using var other = new WotMaterializationCoordinator(
                    m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                    documentConverter: m_converter)
                {
                    ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
                };
                WotRefreshRequest separate = HandoffRequest("external-publication", 1);
                separate.Selection = [UnitSelector(external)];
                WotRefreshResult accepted = await other.RefreshAsync(separate, timeout.Token).ConfigureAwait(false);
                Assert.That(accepted.NewGeneration, Is.EqualTo(2u));
                published = m_registry.Current;
            }
            finally
            {
                resume.TrySetResult(true);
            }
            await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
            Assert.That(m_registry.Current, Is.SameAs(published));
            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(2u));
            Assert.That(m_registry.Current.FindResource(first.GroupId, first.ResourceId)!.RefreshGeneration,
                Is.EqualTo(1u));
        }

        private void ObserveAcquisition(TaskCompletionSource<bool> entered, TaskCompletionSource<bool> resume)
        {
            var registry = new Mock<IWotPreparedRegistryPublicationService>(MockBehavior.Strict);
            registry.SetupGet(owner => owner.Current).Returns(() => m_registry.Current);
            registry.SetupGet(owner => owner.SupportsPreparedPublication).Returns(true);
            int reads = 0;
            registry.Setup(owner => owner.ReadContentAsync(
                It.IsAny<WotResourceVersion>(), It.IsAny<CancellationToken>())).Returns(async (
                    WotResourceVersion version, CancellationToken token) =>
                {
                    ByteString bytes = await m_registry.ReadContentAsync(version, token).ConfigureAwait(false);
                    if (Interlocked.Increment(ref reads) == 1)
                    {
                        entered.TrySetResult(true);
                        await resume.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    return bytes;
                });
            registry.Setup(owner => owner.PreparePublicationAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<ArrayOf<WotResourceProjection>>(),
                It.IsAny<uint>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>())).Returns((
                    WotRegistrySnapshot snapshot, ArrayOf<WotResourceProjection> projections, uint generation,
                    ByteString graph, CancellationToken token) =>
                m_registry.PreparePublicationAsync(snapshot, projections, generation, graph, token));
            registry.As<IWotRegistryVersionLeaseProvider>().Setup(owner => owner.AcquireVersionLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WotResourceVersion>(),
                It.IsAny<CancellationToken>())).Returns((
                    string group, string resource, WotResourceVersion version, CancellationToken token) =>
                m_registry.AcquireVersionLeaseAsync(group, resource, version, token));
            registry.As<IWotInvocationRegistryPublicationService>()
                .Setup(owner => owner.BeginPublicationAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => m_registry.BeginPublicationAsync(token));
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                registry.Object, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: m_converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
        }
    }
}
