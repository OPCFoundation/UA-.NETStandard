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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task GracefulRetirementInvalidatesBrowseContinuationFromOldGenerationAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var unrelated = await AddContinuationOwnerAsync(
                "urn:opcfoundation.org:Tests:BrowseDependencyUnrelated", timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult baseline = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                AssertBrowseDependencyReferences(
                    baseline, source.NamespaceIndex, target.NamespaceIndex,
                    "Old", VariableTypeIds.BaseDataVariableType);
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(page.References.Count, Is.EqualTo(1));
                Assert.That(page.References[0].NodeId.NamespaceIndex, Is.EqualTo(source.NamespaceIndex));
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(m_server.CurrentInstance.SubscriptionManager.GetSubscriptions(), Is.Empty);
                TrackingLifecycleNodeManager? newSource = null;
                TrackingLifecycleNodeManager? newTarget = null;
                NodeManagerBatchResult retired;
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        replace
                            ? NodeManagerBatchChange.Replace(source.Registration, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => newSource = manager))
                            : NodeManagerBatchChange.Remove(source.Registration),
                        replace
                            ? NodeManagerBatchChange.Replace(target.Registration, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => newTarget = manager, kSecondModelNamespaceUri))
                            : NodeManagerBatchChange.Remove(target.Registration),
                        NodeManagerBatchChange.Remove(unrelated.Registration)
                    ], null, timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        AssertLifecycleValue(newSource);
                        AssertLifecycleValue(newTarget);
                        await newSource.AddContinuationChildrenAsync("New", timeout.Token).ConfigureAwait(false);
                        ConfigureBrowseDependency(newSource, newTarget, "NewTarget", VariableTypeIds.PropertyType);
                    }
                    retired = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                }
                Assert.That(unrelated.Manager.DisposeCount, Is.EqualTo(1),
                    "A saved continuation must not pin an unrelated generation.");
                BrowseResult current = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                if (replace)
                {
                    AssertBrowseDependencyReferences(
                        current, source.NamespaceIndex, target.NamespaceIndex, "New", VariableTypeIds.PropertyType);
                }
                else
                {
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }

                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.Results[0].References, Is.Empty);
                    Assert.That(retired.Retired, Is.EqualTo(3u));
                    Assert.That(retired.CleanupFailure, Is.Null);
                }
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                if (replace)
                {
                    TrackingLifecycleNodeManager sourceGeneration = RequireLifecycleValue(newSource);
                    TrackingLifecycleNodeManager targetGeneration = RequireLifecycleValue(newTarget);
                    Assert.That(sourceGeneration.DisposeCount, Is.Zero);
                    Assert.That(targetGeneration.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task SavedBrowsePointsDoNotRetainAnyGenerationAsync(bool sharedNamespace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var first = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddContinuationOwnerAsync(
                kModelNamespaceUri + ":SecondBrowseSource", timeout.Token).ConfigureAwait(false);
            string targetUri = sharedNamespace ? kModelNamespaceUri : kSecondModelNamespaceUri;
            TrackingLifecycleNodeManager? unrelated = null;
            TrackingLifecycleNodeManager? target = null;
            NodeManagerRegistration unrelatedRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(1, manager => unrelated = manager, targetUri),
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(unrelated);
            unrelated.RejectHandleLookupAfterDisposal = true;
            NodeManagerRegistration targetRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(2, manager => target = manager, targetUri),
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(target);
            ushort targetNamespace = target.NamespaceIndexes[0];
            NodeId targetId = new(8100u, targetNamespace);
            await target.AddBrowseDependencyTargetAsync(targetId, timeout.Token).ConfigureAwait(false);
            RequireLifecycleValue(first.Manager.Find(new NodeId(kRootNodeId, first.NamespaceIndex)))
                .AddReference(ReferenceTypeIds.HasComponent, false, targetId);
            RequireLifecycleValue(second.Manager.Find(new NodeId(kRootNodeId, second.NamespaceIndex)))
                .AddReference(ReferenceTypeIds.HasComponent, false, targetId);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult firstPoint = await BrowseContinuationRootAsync(
                    session, first.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                BrowseResult anotherFirstPoint = await BrowseContinuationRootAsync(
                    session, first.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                BrowseResult secondPoint = await BrowseContinuationRootAsync(
                    session, second.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(firstPoint.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(anotherFirstPoint.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(secondPoint.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [first.Registration, second.Registration, targetRegistration, unrelatedRegistration],
                    false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(4u));
                Assert.That(retired.CleanupFailure, Is.Null);
                Assert.That(unrelated.DisposeCount, Is.EqualTo(1));
                Assert.That(target.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                await first.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await second.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(target.DisposeCount, Is.EqualTo(1));
                Assert.That(target.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(unrelated.RejectedHandleLookups, Is.Zero,
                    "A stale continuation must not call an unrelated disposed manager.");
                foreach (ByteString token in new[]
                {
                    firstPoint.ContinuationPoint,
                    anotherFirstPoint.ContinuationPoint,
                    secondPoint.ContinuationPoint
                })
                {
                    BrowseNextResponse stale = await session.BrowseNextAsync(
                        null, false, [token], timeout.Token).ConfigureAwait(false);
                    Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.Results[0].References, Is.Empty);
                }
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task InFlightBrowsePinsItsViewButNotItsSuccessorContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool capturedTarget = false;
            bool hasRequestPin = false;
            bool requestPinMatchesView = false;
            AddressSpaceEpoch? requestEpoch = null;
            OperationContext? blockedOperationContext = null;
            RequestType callbackRequestType = RequestType.Unknown;
            NodeManagerGeneration? requestGeneration = null;
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                int calls = 0;
                source.Manager.ValidateNodeCallback = async (context, nodeId, token) =>
                {
                    if (nodeId == new NodeId(kRootNodeId, source.NamespaceIndex) &&
                        Interlocked.Increment(ref calls) == 1)
                    {
                        hasRequestPin = context.OperationContext!.HasRoutingPin;
                        requestPinMatchesView = context.OperationContext.RoutingPinMatchesSnapshot;
                        blockedOperationContext = context.OperationContext;
                        callbackRequestType = context.OperationContext.RequestType;
                        NodeManagerRoutingTable.RoutingSnapshot snapshot =
                            context.OperationContext.RoutingSnapshot!;
                        requestEpoch = snapshot.Epoch;
                        requestGeneration = snapshot.GetGeneration(source.Manager);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        capturedTarget = context.OperationContext!.RoutingSnapshot!
                            .VisibleNamespaceManagers[target.NamespaceIndex].Contains(target.Manager);
                    }
                };
                var requestHeader = new RequestHeader
                {
                    RequestHandle = 9936,
                    TimeoutHint = 60_000
                };
                Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                    requestHeader, false, [page.ContinuationPoint], timeout.Token).AsTask();
                Task<NodeManagerBatchResult>? retirement = null;
                NodeManagerBatchResult? retiredResult = null;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(callbackRequestType, Is.EqualTo(RequestType.BrowseNext));
                    Assert.That(hasRequestPin, Is.True);
                    Assert.That(requestPinMatchesView, Is.True);
                    Assert.That(requestGeneration!.Gate.ActiveCount, Is.GreaterThan(0));
                    retirement = RetireBrowseDependenciesAsync(
                        [source.Registration, target.Registration], false, timeout.Token);
                    BrowseResult current = default!;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        current = await BrowseContinuationRootAsync(
                            session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                        if (current.StatusCode == StatusCodes.BadNodeIdUnknown)
                        {
                            break;
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token).ConfigureAwait(false);
                    }
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    // The removal publishes without waiting for in-flight requests; only disposal waits for them.
                    retiredResult = await retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(blockedOperationContext!.HasRoutingPin, Is.True);
                    Assert.That(blockedOperationContext.RoutingPinMatchesSnapshot, Is.True);
                    Assert.That(requestEpoch!.ActiveCount, Is.GreaterThan(0));
                    Assert.That(source.Manager.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                }
                finally
                {
                    release.TrySetResult(true);
                }
                // The in-flight request keeps resolving against its pinned view, but retirement invalidated the
                // issuing generation's continuation points, so no successor point outlives the retired generation.
                BrowseNextResponse next = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(capturedTarget, Is.True);
                Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(next.Results[0].ContinuationPoint.IsEmpty, Is.True);
                await retiredResult!.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                source.Manager.ValidateNodeCallback = (_, _, _) => default;
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task InFlightBrowseMetadataUsesItsPinnedViewDuringRetirementAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            RequireLifecycleValue(source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex))).RemoveReference(
                ReferenceTypeIds.HasComponent, false, new NodeId(kRootNodeId, target.NamespaceIndex));
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool hasRequestPin = false;
            bool requestPinMatchesView = false;
            NodeManagerGeneration? requestGeneration = null;
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 3, timeout.Token).ConfigureAwait(false);
                Assert.That(page.References.Count, Is.EqualTo(3));
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                target.Manager.ValidateNodeCallback = async (context, nodeId, token) =>
                {
                    if (nodeId == new NodeId(kValueNodeId, target.NamespaceIndex))
                    {
                        hasRequestPin = context.OperationContext!.HasRoutingPin;
                        requestPinMatchesView = context.OperationContext.RoutingPinMatchesSnapshot;
                        NodeManagerRoutingTable.RoutingSnapshot snapshot =
                            context.OperationContext.RoutingSnapshot!;
                        requestGeneration = snapshot.GetGeneration(source.Manager);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                };
                var header = new RequestHeader { RequestHandle = 9937, TimeoutHint = 60_000 };
                Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                    header, false, [page.ContinuationPoint], timeout.Token).AsTask();
                Task<NodeManagerBatchResult>? retirement = null;
                NodeManagerBatchResult? retiredResult = null;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(hasRequestPin, Is.True);
                    Assert.That(requestPinMatchesView, Is.True);
                    Assert.That(requestGeneration!.Gate.ActiveCount, Is.GreaterThan(0));
                    Assert.That(pending.IsCompleted, Is.False);
                    retirement = RetireBrowseDependenciesAsync(
                        [source.Registration, target.Registration], false, timeout.Token);
                    BrowseResult current = default!;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        current = await BrowseContinuationRootAsync(
                            session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                        if (current.StatusCode == StatusCodes.BadNodeIdUnknown)
                        {
                            break;
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token).ConfigureAwait(false);
                    }
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    // The removal publishes without waiting for in-flight requests; only disposal waits for them.
                    retiredResult = await retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(requestGeneration!.Gate.ActiveCount, Is.GreaterThan(0));
                    Assert.That(source.Manager.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    if (cancel)
                    {
                        CancelResponse cancelled = await session.CancelAsync(
                            null, header.RequestHandle, timeout.Token).ConfigureAwait(false);
                        Assert.That(cancelled.CancelCount, Is.EqualTo(1u));
                        await Assert.ThatAsync(() => pending,
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode))
                                .EqualTo(StatusCodes.BadRequestCancelledByClient)).ConfigureAwait(false);
                    }
                    else
                    {
                        release.TrySetResult(true);
                        BrowseNextResponse next = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                        BrowseResult final = next.Results[0];
                        Assert.That(final.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(final.ContinuationPoint.IsEmpty, Is.True);
                        Assert.That(final.References.Count, Is.EqualTo(1));
                        ReferenceDescription reference = final.References[0];
                        Assert.That(reference.NodeId,
                            Is.EqualTo(new ExpandedNodeId(kValueNodeId, target.NamespaceIndex)));
                        Assert.That(reference.BrowseName,
                            Is.EqualTo(new QualifiedName("OldTarget", target.NamespaceIndex)));
                        Assert.That(reference.TypeDefinition,
                            Is.EqualTo(new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                }
                await retiredResult!.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                target.Manager.ValidateNodeCallback = (_, _, _) => default;
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase("Release")]
        [TestCase("Eviction")]
        [TestCase("Failure")]
        [TestCase("Cancellation")]
        [TestCase("PermissionDenied")]
        [TestCase("SessionClose")]
        [TestCase("SessionDispose")]
        [TestCase("Shutdown")]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task NativeBrowseTerminalPathsDoNotRetainRetiredGenerationsAsync(string terminal)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            bool shutdown = false;
            NodeManagerRegistration? evictingRegistration = null;
            try
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                var holder = (SessionContinuationPoints)serverSession.ContinuationPoints;
                holder.MaxBrowse = 1;
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(holder.HasBrowseForManager(source.Manager), Is.True);
                Assert.That(holder.HasBrowseForManager(target.Manager), Is.False);
                switch (terminal)
                {
                    case "Release":
                        await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "Eviction":
                        var evicting = await AddContinuationOwnerAsync(
                            kModelNamespaceUri + ":EvictingSource", timeout.Token).ConfigureAwait(false);
                        evictingRegistration = evicting.Registration;
                        BrowseResult fresh = await BrowseContinuationRootAsync(
                            session, evicting.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                        Assert.That(fresh.ContinuationPoint.IsEmpty, Is.False);
                        Assert.That(evicting.Manager.DisposeCount, Is.Zero);
                        await ReleaseContinuationAsync(session, fresh.ContinuationPoint, timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "Failure":
                    case "Cancellation":
                        await FailBrowseDependencyNextAsync(
                            session, page.ContinuationPoint, source.Manager, terminal == "Cancellation", timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "PermissionDenied":
                        RequireLifecycleValue(source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex)))
                            .RolePermissions =
                        [
                            new RolePermissionType
                            {
                                RoleId = ObjectIds.WellKnownRole_Anonymous,
                                Permissions = (uint)PermissionType.Read
                            }
                        ];
                        BrowseNextResponse denied = await session.BrowseNextAsync(
                            null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                        Assert.That(denied.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(denied.Results[0].ContinuationPoint.IsEmpty, Is.True);
                        break;
                    case "SessionClose":
                        await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                        break;
                    case "SessionDispose":
                        serverSession.Dispose();
                        serverSession.Dispose();
                        break;
                    case "Shutdown":
                        await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                        shutdown = true;
                        AssertServerInternalsDisposed();
                        await FinishShutdownTestAsync().ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(terminal));
                }
                Assert.That(holder.HasBrowseForManager(source.Manager), Is.False);
                Assert.That(holder.HasBrowseForManager(target.Manager), Is.False);
                if (!shutdown)
                {
                    NodeManagerRegistration[] registrations = evictingRegistration is { } evicting
                        ? [source.Registration, target.Registration, evicting]
                        : [source.Registration, target.Registration];
                    await RetireBrowseDependenciesAsync(registrations, false, timeout.Token).ConfigureAwait(false);
                }
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(holder.HasBrowseForManager(source.Manager), Is.False);
                Assert.That(holder.HasBrowseForManager(target.Manager), Is.False);
            }
            finally
            {
                if (!shutdown)
                {
                    await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiringBrowseIssuerInvalidatesContinuationButRetiringTargetDoesNotAsync(bool retireSource)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            target.Manager.RejectHandleLookupAfterDisposal = true;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    retireSource ? [source.Registration, target.Registration] : [target.Registration],
                    true, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(retireSource ? 2u : 1u));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(retireSource ? 1 : 0));
                BrowseNextResponse resumed = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                if (retireSource)
                {
                    Assert.That(resumed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(resumed.Results[0].References.IsEmpty, Is.True);
                    await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    Assert.That(resumed.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    List<ReferenceDescription> references = await DrainBrowsePointAsync(
                        session, resumed.Results[0], timeout.Token).ConfigureAwait(false);
                    Assert.That(references, Has.Count.EqualTo(2));
                    Assert.That(references.Any(reference =>
                        reference.NodeId == new ExpandedNodeId(kValueNodeId, target.NamespaceIndex)), Is.False);
                    Assert.That(source.Manager.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.RejectedHandleLookups, Is.Zero,
                        "Later pages must resolve targets through the current routing view.");
                }
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredNativeLazyBrowseInvalidatesOpaqueContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            target.Manager.RejectHandleLookupAfterDisposal = true;
            NodeId targetId = new(kValueNodeId, target.NamespaceIndex);
            int disposedBrowsers = 0;
            NodeState root = RequireLifecycleValue(source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex)));
            root.OnCreateBrowser =
                (context, _, view, referenceType, includeSubtypes, direction, name, additional, only) =>
                    new OpaqueLazyDependencyBrowser(
                        context, view, referenceType, includeSubtypes, direction, name, additional, only, targetId,
                        () => Interlocked.Increment(ref disposedBrowsers));
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult unpaged = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                Assert.That(unpaged.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(unpaged.References.Count, Is.EqualTo(4));
                Assert.That(unpaged.References.Contains(reference => reference.NodeId == targetId), Is.True);
                Assert.That(disposedBrowsers, Is.EqualTo(1));
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(2u));
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(disposedBrowsers, Is.EqualTo(2));
                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.Results[0].References, Is.Empty);
                Assert.That(disposedBrowsers, Is.EqualTo(2));
                Assert.That(target.Manager.RejectedHandleLookups, Is.Zero);
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task SynchronousNativeBrowseUsesCurrentTargetViewWithoutRetentionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            target.Manager.RejectHandleLookupAfterDisposal = true;
            ReferenceSynchronousNodeManager? source = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    source = new ReferenceSynchronousNodeManager(server, configuration);
                    return new ValueTask<IAsyncNodeManager>(source.ToAsyncNodeManager());
                });
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(source);
            NodeId targetId = new(kValueNodeId, target.NamespaceIndex);
            source.AddBrowseDependencyReferences(targetId);
            ushort sourceNamespace = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, sourceNamespace, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [target.Registration], false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(1u));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                List<ReferenceDescription> references = await DrainBrowsePointAsync(
                    session, page, timeout.Token).ConfigureAwait(false);
                Assert.That(references, Has.Count.EqualTo(3));
                Assert.That(references.Any(reference => reference.NodeId == targetId), Is.False);
                Assert.That(target.Manager.RejectedHandleLookups, Is.Zero);
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        private async Task<NodeManagerBatchResult> RetireBrowseDependenciesAsync(
            ArrayOf<NodeManagerRegistration> registrations,
            bool immediate,
            CancellationToken cancellationToken)
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            var changes = new List<NodeManagerBatchChange>();
            foreach (NodeManagerRegistration registration in registrations)
            {
                changes.Add(NodeManagerBatchChange.Remove(registration, immediate));
            }
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [.. changes], null, cancellationToken).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            return result;
        }

        private static async Task<List<ReferenceDescription>> DrainBrowsePointAsync(
            Opc.Ua.Client.ISession session,
            BrowseResult page,
            CancellationToken cancellationToken)
        {
            List<ReferenceDescription> references = [.. page.References];
            for (int pages = 0; !page.ContinuationPoint.IsEmpty; pages++)
            {
                Assert.That(pages, Is.LessThan(6));
                BrowseNextResponse next = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], cancellationToken).ConfigureAwait(false);
                Assert.That(next.Results.Count, Is.EqualTo(1));
                page = next.Results[0];
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                references.AddRange(page.References);
            }
            return references;
        }

        private static async Task FailBrowseDependencyNextAsync(
            Opc.Ua.Client.ISession session,
            ByteString continuationPoint,
            TrackingLifecycleNodeManager source,
            bool cancel,
            CancellationToken cancellationToken)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.ValidateNodeCallback = async (_, _, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The Browse source failed.");
            };
            try
            {
                var header = new RequestHeader { RequestHandle = 9927 };
                Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                    header, false, [continuationPoint], cancellationToken).AsTask();
                await entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (cancel)
                {
                    CancelResponse cancelled = await session.CancelAsync(
                        null, header.RequestHandle, cancellationToken).ConfigureAwait(false);
                    Assert.That(cancelled.CancelCount, Is.EqualTo(1u));
                }
                else
                {
                    release.TrySetResult(true);
                }
                await Assert.ThatAsync(() => pending,
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(cancel
                            ? StatusCodes.BadRequestCancelledByClient
                            : StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                source.ValidateNodeCallback = (_, _, _) => default;
            }
        }

        private static void ConfigureBrowseDependency(
            TrackingLifecycleNodeManager source,
            TrackingLifecycleNodeManager target,
            string targetName,
            NodeId targetType)
        {
            ushort targetNamespace = target.NamespaceIndexes[0];
            BaseVariableState variable = RequireLifecycleValue(
                target.Find(new NodeId(kValueNodeId, targetNamespace)) as BaseVariableState);
            variable.BrowseName = new QualifiedName(targetName, targetNamespace);
            variable.DisplayName = LocalizedText.From(targetName);
            variable.TypeDefinitionId = targetType;
            NodeState root = RequireLifecycleValue(source.Find(new NodeId(kRootNodeId, source.NamespaceIndexes[0])));
            root.AddReference(ReferenceTypeIds.HasComponent, false, variable.NodeId);
            root.AddReference(ReferenceTypeIds.HasComponent, false, new NodeId(kRootNodeId, targetNamespace));
        }

        private static void AssertBrowseDependencyReferences(
            BrowseResult result,
            ushort sourceNamespace,
            ushort targetNamespace,
            string prefix,
            NodeId targetType)
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            ReferenceDescription[] references = [.. result.References];
            Assert.That(references.Select(reference => reference.NodeId), Is.EquivalentTo(new[]
            {
                new ExpandedNodeId(kValueNodeId, sourceNamespace),
                new ExpandedNodeId(8010u, sourceNamespace),
                new ExpandedNodeId(8011u, sourceNamespace),
                new ExpandedNodeId(kValueNodeId, targetNamespace)
            }), "The later page must keep the exact external target, not silently omit it.");
            Assert.That(references.Select(reference => reference.BrowseName), Is.EquivalentTo(new[]
            {
                new QualifiedName(kValueBrowseName, sourceNamespace),
                new QualifiedName(prefix + "First", sourceNamespace),
                new QualifiedName(prefix + "Second", sourceNamespace),
                new QualifiedName(prefix + "Target", targetNamespace)
            }));
            Assert.That(references.All(reference =>
                reference.NodeClass == NodeClass.Variable &&
                reference.IsForward &&
                reference.ReferenceTypeId == ReferenceTypeIds.HasComponent), Is.True,
                "The external Object target must still be filtered out.");
            ReferenceDescription? target = references.SingleOrDefault(
                reference => reference.NodeId == new ExpandedNodeId(kValueNodeId, targetNamespace));
            Assert.That(target, Is.Not.Null);
            if (target is not null)
            {
                Assert.That(target.DisplayName, Is.EqualTo(LocalizedText.From(prefix + "Target")));
                Assert.That(target.TypeDefinition, Is.EqualTo(new ExpandedNodeId(targetType)));
            }
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            public bool RejectHandleLookupAfterDisposal { get; set; }

            public int RejectedHandleLookups => Volatile.Read(ref m_rejectedHandleLookups);

            public async ValueTask AddBrowseDependencyTargetAsync(NodeId nodeId, CancellationToken cancellationToken)
            {
                var target = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("SharedTarget", nodeId.NamespaceIndex),
                    DisplayName = LocalizedText.From("SharedTarget"),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    Value = 17
                };
                await AddPredefinedNodeAsync(SystemContext, target, cancellationToken).ConfigureAwait(false);
            }

            public override async ValueTask<object> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                if (RejectHandleLookupAfterDisposal && DisposeCount > 0)
                {
                    Interlocked.Increment(ref m_rejectedHandleLookups);
                    throw new ObjectDisposedException(nameof(TrackingLifecycleNodeManager));
                }
                if (ManagerHandleCallback is not null)
                {
                    await ManagerHandleCallback(nodeId, cancellationToken).ConfigureAwait(false);
                }
                return await base.GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }

            private int m_rejectedHandleLookups;
        }

        private sealed partial class ReferenceSynchronousNodeManager
        {
            public void AddBrowseDependencyReferences(NodeId targetId)
            {
                NodeState root = RequireLifecycleValue(Find(new NodeId(kRootNodeId, NamespaceIndexes[0])));
                for (uint index = 0; index < 3; index++)
                {
                    var variable = new BaseDataVariableState(root)
                    {
                        NodeId = new NodeId(8200 + index, NamespaceIndexes[0]),
                        BrowseName = new QualifiedName("Synchronous" + index, NamespaceIndexes[0]),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Value = 1
                    };
                    root.AddChild(variable);
                    AddPredefinedNode(SystemContext, variable);
                }
                root.AddReference(ReferenceTypeIds.HasComponent, false, targetId);
            }
        }

        private sealed class OpaqueLazyDependencyBrowser(
            ISystemContext context,
            ViewDescription? view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection direction,
            QualifiedName browseName,
            IEnumerable<IReference>? additionalReferences,
            bool internalOnly,
            NodeId targetId,
            Action disposed)
            : NodeBrowser(context, view, referenceType, includeSubtypes, direction,
                browseName, additionalReferences, internalOnly)
        {
            private NodeId DependencyTarget { get; } = targetId;

            public override IReference? Next()
            {
                IReference? reference = base.Next();
                if (reference is not null)
                {
                    return reference;
                }
                if (!m_returned && IsRequired(ReferenceTypeIds.HasComponent, false))
                {
                    m_returned = true;
                    return new NodeStateReference(ReferenceTypeIds.HasComponent, false, DependencyTarget);
                }
                return null;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    disposed();
                }
                base.Dispose(disposing);
            }

            private bool m_returned;
        }

    }
}
