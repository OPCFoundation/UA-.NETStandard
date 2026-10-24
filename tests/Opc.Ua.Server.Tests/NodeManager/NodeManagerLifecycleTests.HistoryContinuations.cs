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
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiredHistoryGenerationInvalidatesSavedContinuationAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var oldProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var newProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager =>
                {
                    originalManager = manager;
                    manager.HistoryProvider = oldProvider;
                }), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(originalManager);
            NodeId nodeId = new(kValueNodeId, originalManager.NamespaceIndexes[0]);
            await SeedLifecycleHistoryAsync(originalManager, oldProvider, 101, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult first = await ReadLifecycleHistoryAsync(
                    session, nodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(first, 101, 0, StatusCodes.Good);
                Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(m_server.CurrentInstance.SubscriptionManager.GetSubscriptions(), Is.Empty);
                TrackingLifecycleNodeManager? replacement = null;
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                NodeManagerBatchResult retired;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        replace
                            ? NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager =>
                                {
                                    replacement = manager;
                                    manager.HistoryProvider = newProvider;
                                }))
                            : NodeManagerBatchChange.Remove(original)
                    ], null, timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        AssertLifecycleValue(replacement);
                        await SeedLifecycleHistoryAsync(replacement, newProvider, 201, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    retired = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                }
                HistoryReadResult current = await ReadLifecycleHistoryAsync(
                    session, nodeId, ByteString.Empty, 0, false, timeout.Token).ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (replace)
                {
                    Assert.That(current.HistoryData.TryGetValue(out HistoryData? currentData), Is.True);
                    AssertLifecycleValue(currentData);
                    Assert.That(currentData.DataValues.Count, Is.EqualTo(2));
                    Assert.That(currentData.DataValues[0].WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(201));
                    Assert.That(current.ContinuationPoint.IsEmpty, Is.True);
                }
                HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                    session, nodeId, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.HistoryData.IsNull, Is.True);
                    Assert.That(retired.Retired, Is.EqualTo(1u));
                    Assert.That(retired.CleanupFailure, Is.Null);
                }
                await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (replace)
                {
                    TrackingLifecycleNodeManager replacementGeneration = RequireLifecycleValue(replacement);
                    Assert.That(replacementGeneration.DisposeCount, Is.Zero);
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
        public async Task HistoryContinuationsDoNotRetainTheirIssuingOrTargetGenerationsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var unrelated = await AddContinuationOwnerAsync(
                kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            unrelated.Manager.RejectHandleLookupAfterDisposal = true;
            using var firstData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var secondData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var first = await AddHistoryOwnerAsync(firstData,
                CreateHistoryProbe(firstData), kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddHistoryOwnerAsync(secondData,
                CreateHistoryProbe(secondData), kModelNamespaceUri + ":Second", timeout.Token)
                .ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult firstPoint = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResult secondPoint = await ReadLifecycleHistoryAsync(
                    session, second.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(firstPoint.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(secondPoint.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [first.Registration, second.Registration, target.Registration, unrelated.Registration],
                    false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(4u));
                Assert.That(retired.CleanupFailure, Is.Null);
                HistoryReadResult firstStale = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, firstPoint.ContinuationPoint, 1, false, timeout.Token)
                    .ConfigureAwait(false);
                HistoryReadResult secondStale = await ReadLifecycleHistoryAsync(
                    session, second.NodeId, secondPoint.ContinuationPoint, 1, false, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(firstStale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(firstStale.HistoryData.IsNull, Is.True);
                Assert.That(secondStale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(secondStale.HistoryData.IsNull, Is.True);
                await first.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await second.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await unrelated.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(unrelated.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(unrelated.Manager.RejectedHandleLookups, Is.Zero);
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
        public async Task InFlightHistoryReadCompletesAgainstPinnedRequestViewAsync(bool retireBeforeRead)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            NodeId targetId = new(kValueNodeId, target.NamespaceIndex);
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            HistorianOperationContext? observed = null;
            bool capturedSource = false;
            bool capturedTarget = false;
            TrackingLifecycleNodeManager? source = null;
            IHistorianProvider provider = CreateHistoryProbe(data, async (context, _, token, ct) =>
            {
                if (token.IsEmpty)
                {
                    return;
                }
                observed = context;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                AssertLifecycleValue(source);
                var routes = context.OperationContext.RoutingSnapshot!.VisibleNamespaceManagers;
                capturedSource = routes[source.NamespaceIndexes[0]].Contains(source);
                capturedTarget = routes[target.NamespaceIndex].Contains(target.Manager);
            });
            var owner = await AddHistoryOwnerAsync(data, provider, kModelNamespaceUri, timeout.Token)
                .ConfigureAwait(false);
            source = owner.Manager;
            NodeState originalNode = RequireLifecycleValue(source.Find(owner.NodeId));
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                if (retireBeforeRead)
                {
                    await RetireBrowseDependenciesAsync(
                        [owner.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                    HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                        session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(stale.HistoryData.IsNull, Is.True);
                    await source.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(source.DisposeCount, Is.EqualTo(1));
                    Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                    return;
                }
                Task<NodeManagerBatchResult>? retirement = null;
                NodeManagerBatchResult? retirementResult = null;
                Task<HistoryReadResponse> pending = session.HistoryReadAsync(
                    new RequestHeader { TimeoutHint = 60_000 },
                    new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                    [new HistoryReadValueId { NodeId = owner.NodeId, ContinuationPoint = page.ContinuationPoint }],
                    timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    retirement = RetireBrowseDependenciesAsync(
                        [owner.Registration, target.Registration], false, timeout.Token);
                    retirementResult = await retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                    BrowseResult current = default!;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        current = await BrowseContinuationRootAsync(
                            session, source.NamespaceIndexes[0], 0, timeout.Token).ConfigureAwait(false);
                        if (current.StatusCode == StatusCodes.BadNodeIdUnknown)
                        {
                            break;
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token).ConfigureAwait(false);
                    }
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(retirementResult.Retirement.IsCompleted, Is.False);
                    Assert.That(retirementResult.Revision, Is.GreaterThan(0));
                    Assert.That(retirementResult.Revision, Is.EqualTo(m_server.NodeManagerLifecycle.Revision));
                    NodeManagerSlot[] resultSlots = retirementResult.Slots.Memory.ToArray();
                    NodeManagerSlot[] currentSlots = m_server.NodeManagerLifecycle.Slots.Memory.ToArray();
                    Assert.That(
                        resultSlots,
                        Is.EquivalentTo(currentSlots));
                    Assert.That(
                        resultSlots.Any(slot => ReferenceEquals(slot, owner.Registration.Slot)),
                        Is.False);
                    Assert.That(
                        resultSlots.Any(slot => ReferenceEquals(slot, target.Registration.Slot)),
                        Is.False);
                    var holder = (SessionContinuationPoints)m_server.CurrentInstance.SessionManager
                        .GetSessions().Single(candidate => candidate.Id == session.SessionId).ContinuationPoints;
                    Assert.That(holder.HasHistoryForManager(source), Is.True);
                    Assert.That(holder.HasHistoryForManager(target.Manager), Is.False);
                    Assert.That(source.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    AssertLifecycleValue(observed);
                    Assert.That(observed.Node, Is.SameAs(originalNode));
                    Assert.That(RequireLifecycleValue(observed.OperationContext.Session).Id,
                        Is.EqualTo(session.SessionId));
                }
                finally
                {
                    release.TrySetResult(true);
                    await pending.ConfigureAwait(false);
                }
                HistoryReadResponse response = await pending.ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                AssertHistorySample(response.Results[0], 102, 1, StatusCodes.UncertainDataSubNormal);
                Assert.That(capturedSource, Is.True);
                Assert.That(capturedTarget, Is.True);
                Assert.That(retirementResult!.Retired, Is.Zero);
                await retirementResult!.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(retirementResult.Retirement.IsCompleted, Is.True);
                await source.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase("Release")]
        [TestCase("Failure")]
        [TestCase("Cancellation")]
        [TestCase("PermissionDenied")]
        [TestCase("SessionClose")]
        [TestCase("SessionDispose")]
        [TestCase("Shutdown")]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task NativeHistoryTerminalPathsReleaseSourceAndDependenciesAsync(string terminal)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IHistorianProvider provider = CreateHistoryProbe(
                data, async (_, _, token, ct) =>
                {
                    if (!token.IsEmpty && terminal is "Failure" or "Cancellation")
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        throw new IOException("History source failed while reading the continuation page.");
                    }
                });
            var owner = await AddHistoryOwnerAsync(data, provider, kModelNamespaceUri, timeout.Token)
                .ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            bool shutdown = false;
            try
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                var holder = (SessionContinuationPoints)serverSession.ContinuationPoints;
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(holder.HasHistoryForManager(owner.Manager), Is.True);
                Assert.That(holder.HasHistoryForManager(target.Manager), Is.False);
                switch (terminal)
                {
                    case "Release":
                        HistoryReadResult released = await ReadLifecycleHistoryAsync(
                            session, owner.NodeId, page.ContinuationPoint, 1, true, timeout.Token)
                            .ConfigureAwait(false);
                        Assert.That(released.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(released.ContinuationPoint.IsEmpty, Is.True);
                        break;
                    case "Failure":
                    case "Cancellation":
                        var header = new RequestHeader { RequestHandle = 9941 };
                        Task<HistoryReadResponse> pending = session.HistoryReadAsync(
                            header, new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                            [
                                new HistoryReadValueId
                                {
                                    NodeId = owner.NodeId,
                                    ContinuationPoint = page.ContinuationPoint
                                }
                            ],
                            timeout.Token).AsTask();
                        Task completed = await Task.WhenAny(entered.Task, pending).WaitAsync(timeout.Token)
                            .ConfigureAwait(false);
                        if (ReferenceEquals(completed, pending))
                        {
                            HistoryReadResponse premature = await pending.ConfigureAwait(false);
                            Assert.Fail(
                                $"The continuation page completed before entering its provider: " +
                                $"{premature.Results[0].StatusCode}.");
                        }
                        await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(holder.HasHistoryForManager(owner.Manager), Is.True);
                        Assert.That(owner.Manager.DisposeCount, Is.Zero);
                        if (terminal == "Cancellation")
                        {
                            CancelResponse cancelled = await session.CancelAsync(
                                null, header.RequestHandle, timeout.Token).ConfigureAwait(false);
                            Assert.That(cancelled.CancelCount, Is.EqualTo(1u));
                        }
                        else
                        {
                            release.TrySetResult(true);
                        }
                        await Assert.ThatAsync(() => pending,
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(
                                    terminal == "Cancellation"
                                        ? StatusCodes.BadRequestCancelledByClient
                                        : StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
                        break;
                    case "PermissionDenied":
                        RequireLifecycleValue(owner.Manager.Find(owner.NodeId)).RolePermissions =
                        [
                            new RolePermissionType
                            {
                                RoleId = ObjectIds.WellKnownRole_Anonymous,
                                Permissions = (uint)PermissionType.Read
                            }
                        ];
                        HistoryReadResult denied = await ReadLifecycleHistoryAsync(
                            session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token)
                            .ConfigureAwait(false);
                        Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(denied.HistoryData.IsNull, Is.True);
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
                if (!shutdown)
                {
                    await RetireBrowseDependenciesAsync(
                        [owner.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                }
                Assert.That(holder.HasHistoryForManager(owner.Manager), Is.False);
                Assert.That(holder.HasHistoryForManager(target.Manager), Is.False);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
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
        public async Task ImmediateHistoryRetirementInvalidatesSavedAccessAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        replace
                            ? NodeManagerBatchChange.Replace(owner.Registration,
                                CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), true)
                            : NodeManagerBatchChange.Remove(owner.Registration, true)
                    ], null, timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult retired = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(retired.CleanupFailure, Is.Null);
                    Assert.That(retired.Retired, Is.EqualTo(1u));
                }
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.HistoryData.IsNull, Is.True);
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RetiringUnrelatedTargetDoesNotInvalidateSourceContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data),
                kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [target.Registration], true, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(1u));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                Assert.That(final.ContinuationPoint.IsEmpty, Is.True);
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task DynamicHistoryPaginationDoesNotRequireDependencyDeclarationsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data), kModelNamespaceUri, timeout.Token)
                .ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult unpaged = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 0, false, timeout.Token).ConfigureAwait(false);
                Assert.That(unpaged.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(unpaged.HistoryData.TryGetValue(out HistoryData? all), Is.True);
                AssertLifecycleValue(all);
                Assert.That(all.DataValues.Count, Is.EqualTo(2));
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(page, 101, 0, StatusCodes.Good);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                Assert.That(final.ContinuationPoint.IsEmpty, Is.True);
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that dynamic history pagination can use a replacement continuation cache.
        /// </summary>
        [Category("Integration")]
        public async Task DynamicHistoryPaginationUsesReplacementCacheAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(
                data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var cache = new Mock<ISessionContinuationPoints>(MockBehavior.Strict);
            IHistoryContinuationPoint? savedContinuation = null;
            cache.Setup(value => value.SaveHistoryAsync(
                    It.IsAny<IHistoryContinuationPoint>(), It.IsAny<CancellationToken>()))
                .Callback<IHistoryContinuationPoint, CancellationToken>((point, _) => savedContinuation = point)
                .Returns(default(ValueTask));
            var session = new Mock<ISession>();
            session.SetupGet(value => value.ContinuationPoints).Returns(cache.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            using var operation = new OperationContext(session.Object, DiagnosticsMasks.None);
            var context = new ServerSystemContext(m_server.CurrentInstance, operation);
            NodeState node = RequireLifecycleValue(owner.Manager.Find(owner.NodeId));
            var unpaged = new HistoryReadResult();

            ServiceResult unpagedError = await HistorianDispatcher.DispatchRawReadAsync(
                context, data, node, new HistoryReadValueId { NodeId = owner.NodeId },
                CreateHistoryDetails(0), TimestampsToReturn.Both, unpaged, timeout.Token).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(unpagedError), Is.True);
            Assert.That(unpaged.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(unpaged.HistoryData.TryGetValue(out HistoryData? values), Is.True);
            AssertLifecycleValue(values);
            Assert.That(values.DataValues.Count, Is.EqualTo(2));
            Assert.That(unpaged.ContinuationPoint.IsEmpty, Is.True);

            var paged = new HistoryReadResult();
            await HistorianDispatcher.DispatchRawReadAsync(
                context, data, node, new HistoryReadValueId { NodeId = owner.NodeId },
                CreateHistoryDetails(1), TimestampsToReturn.Both, paged, timeout.Token).ConfigureAwait(false);

            Assert.That(paged.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(paged.HistoryData.TryGetValue(out HistoryData? firstPage), Is.True);
            AssertLifecycleValue(firstPage);
            Assert.That(firstPage.DataValues.Count, Is.EqualTo(1));
            Assert.That(paged.ContinuationPoint.IsEmpty, Is.False);
            cache.Verify(value => value.SaveHistoryAsync(
                It.IsAny<IHistoryContinuationPoint>(), It.IsAny<CancellationToken>()), Times.Once);
            cache.VerifyNoOtherCalls();
            savedContinuation?.Dispose();
            NodeManagerBatchResult retired = await RetireContinuationOwnerAsync(
                owner.Registration, false, timeout.Token).ConfigureAwait(false);
            Assert.That(retired.Retired, Is.EqualTo(1u));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task HistoryContinuationRejectsForeignSessionAndWrongOriginWithoutReroutingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var foreignClient = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await foreignClient.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession foreign = await foreignClient.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                HistoryReadResult foreignResult = await ReadLifecycleHistoryAsync(
                    foreign, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(foreignResult.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(foreignResult.HistoryData.IsNull, Is.True);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResult wrongOrigin = await ReadLifecycleHistoryAsync(
                    session, ObjectIds.Server, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(wrongOrigin.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(wrongOrigin.HistoryData.IsNull, Is.True);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.HistoryData.IsNull, Is.True);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await foreign.CloseAsync(timeout.Token).ConfigureAwait(false);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task FailedHistoryRequestReleasesEveryUndeliveredContinuationAsync(bool resumeFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var firstData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var secondData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var first = await AddHistoryOwnerAsync(firstData, firstData, kModelNamespaceUri, timeout.Token)
                .ConfigureAwait(false);
            var second = await AddHistoryOwnerAsync(secondData,
                CreateHistoryProbe(
                    secondData, (_, _, _, _) => throw new IOException("Second history read failed.")),
                kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var context = new HistorianOperationContext(
                new ServerSystemContext(m_server.CurrentInstance, operation), operation,
                RequireLifecycleValue(first.Manager.Find(first.NodeId)),
                HistoryUpdateType.Insert);
            await firstData.InsertAsync(context, first.NodeId,
                [new DataValue(new Variant(103), StatusCodes.Good, s_historyTime.AddMilliseconds(1500),
                    s_historyTime.AddMilliseconds(11500))], timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                ByteString token = ByteString.Empty;
                if (resumeFirst)
                {
                    HistoryReadResult page = await ReadLifecycleHistoryAsync(
                        session, first.NodeId, token, 1, false, timeout.Token).ConfigureAwait(false);
                    Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                    token = page.ContinuationPoint;
                }
                await Assert.ThatAsync(async () =>
                    await session.HistoryReadAsync(
                        null, new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                        [
                            new HistoryReadValueId { NodeId = first.NodeId, ContinuationPoint = token },
                            new HistoryReadValueId { NodeId = second.NodeId }
                        ], timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadUnexpectedError))
                    .ConfigureAwait(false);
                var holder = (SessionContinuationPoints)m_server.CurrentInstance.SessionManager
                    .GetSessions().Single(candidate => candidate.Id == session.SessionId).ContinuationPoints;
                Assert.That(holder.HasHistoryForManager(first.Manager), Is.False,
                    "Undelivered pages must not retain owners after a failed service.");
                Assert.That(holder.HasHistoryForManager(second.Manager), Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [first.Registration, second.Registration], false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(2u));
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task FirstHistoryPageInFlightAtRetirementStillRejectsItsSavedContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data, async (_, _, token, ct) =>
                {
                    if (token.IsEmpty)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                }), kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(owner.Registration)], null, timeout.Token)
                .ConfigureAwait(false);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Task<HistoryReadResponse> pending = session.HistoryReadAsync(
                    null, new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                    [new HistoryReadValueId { NodeId = owner.NodeId }], timeout.Token).AsTask();
                Task<NodeManagerBatchResult>? committing = null;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    committing = prepared.CommitAsync(
                        _ => default, () => published.TrySetResult(true), timeout.Token).AsTask();
                    await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(owner.Manager.DisposeCount, Is.Zero);
                }
                finally
                {
                    release.TrySetResult(true);
                    await pending.ConfigureAwait(false);
                    if (committing is not null)
                    {
                        await committing.ConfigureAwait(false);
                    }
                }
                Assert.That(committing, Is.Not.Null);
                NodeManagerBatchResult retired = await committing.ConfigureAwait(false);
                Assert.That(retired.CleanupFailure, Is.Null);
                Assert.That(retired.Retired, Is.EqualTo(1u));
                HistoryReadResponse response = await pending.ConfigureAwait(false);
                HistoryReadResult page = response.Results[0];
                AssertHistorySample(page, 101, 0, StatusCodes.Good);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(final.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(final.HistoryData.IsNull, Is.True);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task NativeEventHistoryContinuationIsInvalidAfterGenerationRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            owner.Manager.HistoryProvider = CreatePagedEventHistoryProbe(data);
            BaseObjectState notifier = RequireLifecycleValue(owner.Manager.Find(
                new NodeId(kRootNodeId, owner.Manager.NamespaceIndexes[0])) as BaseObjectState);
            notifier.EventNotifier |= EventNotifiers.HistoryRead;
            data.Register(notifier.NodeId);
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var context = new HistorianOperationContext(
                new ServerSystemContext(m_server.CurrentInstance, operation), operation, notifier,
                HistoryUpdateType.Insert);
            ByteString firstId = new("old-history-first"u8.ToArray());
            ByteString secondId = new("old-history-second"u8.ToArray());
            await data.InsertEventsAsync(context, notifier.NodeId,
                [
                    CreateHistoryEvent(firstId, "first", s_historyTime),
                    CreateHistoryEvent(secondId, "second", s_historyTime.AddSeconds(1))
                ], timeout.Token).ConfigureAwait(false);
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Time, Attributes.Value);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResponse first = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadEventDetails
                    {
                        StartTime = s_historyTime.AddSeconds(-1),
                        EndTime = s_historyTime.AddSeconds(2),
                        NumValuesPerNode = 1,
                        Filter = filter
                    }), TimestampsToReturn.Both, false, [new HistoryReadValueId { NodeId = notifier.NodeId }],
                    timeout.Token).ConfigureAwait(false);
                Assert.That(first.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.Results[0].ContinuationPoint.IsEmpty, Is.False);
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResponse resumed = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadEventDetails { Filter = filter }),
                    TimestampsToReturn.Source, false,
                    [new HistoryReadValueId
                    {
                        NodeId = notifier.NodeId,
                        ContinuationPoint = first.Results[0].ContinuationPoint
                    }], timeout.Token).ConfigureAwait(false);
                Assert.That(resumed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(resumed.Results[0].HistoryData.IsNull, Is.True);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task NativeAnnotationHistoryContinuationIsInvalidAfterGenerationRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            NodeId annotations = await owner.Manager.AddHistoryAnnotationsAsync(timeout.Token).ConfigureAwait(false);
            await SeedHistoryAnnotationsAsync(owner.Manager, data, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult first = await ReadLifecycleHistoryAsync(
                    session, annotations, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                    session, annotations, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.HistoryData.IsNull, Is.True);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
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
        public async Task NativeBufferedProcessedHistoryInvalidatesEveryPointOnGenerationRetirementAsync(
            bool twoPoints)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await SeedHistoryAnnotationsAsync(owner.Manager, data, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                ArrayOf<NodeId> aggregates = twoPoints
                    ? [ObjectIds.AggregateFunction_AnnotationCount, ObjectIds.AggregateFunction_AnnotationCount]
                    : [ObjectIds.AggregateFunction_AnnotationCount];
                ArrayOf<HistoryReadValueId> reads = twoPoints
                    ? [
                        new HistoryReadValueId { NodeId = owner.NodeId },
                        new HistoryReadValueId { NodeId = owner.NodeId }
                    ]
                    : [new HistoryReadValueId { NodeId = owner.NodeId }];
                HistoryReadResponse first = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadProcessedDetails
                    {
                        StartTime = s_historyTime,
                        EndTime = s_historyTime.AddMilliseconds(1001),
                        ProcessingInterval = 1,
                        AggregateType = aggregates
                    }), TimestampsToReturn.Both, false, reads, timeout.Token).ConfigureAwait(false);
                Assert.That(first.Results.Count, Is.EqualTo(twoPoints ? 2 : 1));
                for (int index = 0; index < first.Results.Count; index++)
                {
                    Assert.That(first.Results[index].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(first.Results[index].ContinuationPoint.IsEmpty, Is.False);
                    Assert.That(first.Results[index].HistoryData.TryGetValue(out HistoryData? page), Is.True);
                    AssertLifecycleValue(page);
                    Assert.That(page.DataValues.Count, Is.EqualTo(1000));
                    reads[index].ContinuationPoint = first.Results[index].ContinuationPoint;
                }
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                HistoryReadResponse resumed = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadProcessedDetails { AggregateType = aggregates }),
                    TimestampsToReturn.Server, false, reads, timeout.Token).ConfigureAwait(false);
                Assert.That(resumed.Results.Count, Is.EqualTo(twoPoints ? 2 : 1));
                foreach (HistoryReadResult result in resumed.Results)
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(result.HistoryData.IsNull, Is.True);
                }
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task NativeHistoryContinuationIsInvalidAfterUnbindingAndRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                HistoryReadResult first = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
                owner.Manager.HistoryProvider = null;
                IHistorianProviderRegistry registry =
                    ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
                registry.UnregisterForNode(owner.NodeId);
                registry.UnregisterForNamespace(kModelNamespaceUri);
                registry.ClearDefault();
                Assert.That(registry.Resolve(owner.NodeId), Is.Null);
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResult stale = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.HistoryData.IsNull, Is.True);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        private static IHistorianProvider CreatePagedEventHistoryProbe(InMemoryHistorianProvider data)
        {
            var provider = new Mock<IHistorianProvider>();
            provider.Setup(value => value.IsHistorizingAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken ct) => data.IsHistorizingAsync(nodeId, ct));
            provider.Setup(value => value.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(async (NodeId nodeId, CancellationToken ct) =>
                    (await data.GetCapabilitiesAsync(nodeId, ct).ConfigureAwait(false)) with
                    {
                        ReadEventHistory = true
                    });
            provider.As<IHistorianEventProvider>().Setup(value => value.ReadEventsAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianEventReadRequest>(),
                It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .Returns(async (HistorianOperationContext context, HistorianEventReadRequest request,
                    HistorianResumeToken token, CancellationToken ct) =>
                {
                    HistorianPage<HistorianEventRecord> all = await data.ReadEventsAsync(
                        context, request with { MaxValues = 0 }, default, ct).ConfigureAwait(false);
                    Assert.That(all.Values.ToList(), Has.Count.EqualTo(2));
                    if (token.IsEmpty)
                    {
                        return new HistorianPage<HistorianEventRecord>(
                            [all.Values[0]], new HistorianResumeToken(ByteString.From("second"u8)));
                    }
                    Assert.That(token.State.Span.SequenceEqual("second"u8), Is.True);
                    return new HistorianPage<HistorianEventRecord>([all.Values[1]]);
                });
            return provider.Object;
        }

        private static HistorianEventRecord CreateHistoryEvent(ByteString id, string message, DateTime time)
        {
            return new HistorianEventRecord(id, ObjectTypeIds.BaseEventType, time,
                [
                    new(BrowseNames.EventId, new Variant(id)),
                    new(BrowseNames.EventType, new Variant(ObjectTypeIds.BaseEventType)),
                    new(BrowseNames.Message, new Variant(new LocalizedText(message))),
                    new(BrowseNames.Time, new Variant((DateTimeUtc)time))
                ]);
        }

        private async Task SeedHistoryAnnotationsAsync(
            TrackingLifecycleNodeManager manager,
            InMemoryHistorianProvider provider,
            CancellationToken cancellationToken)
        {
            NodeState node = RequireLifecycleValue(manager.Find(new NodeId(kValueNodeId, manager.NamespaceIndexes[0])));
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var context = new HistorianOperationContext(
                new ServerSystemContext(m_server.CurrentInstance, operation), operation, node,
                HistoryUpdateType.Insert);
            await provider.InsertAnnotationsAsync(context, node.NodeId,
                [
                    new Annotation
                    {
                        AnnotationTime = s_historyTime,
                        Message = "first",
                        UserName = "history-owner"
                    },
                    new Annotation
                    {
                        AnnotationTime = s_historyTime.AddSeconds(1),
                        Message = "second",
                        UserName = "history-owner"
                    }
                ], cancellationToken).ConfigureAwait(false);
        }

        private async Task<(NodeManagerRegistration Registration, TrackingLifecycleNodeManager Manager, NodeId NodeId)>
            AddHistoryOwnerAsync(
                InMemoryHistorianProvider data,
                IHistorianProvider provider,
                string namespaceUri,
                CancellationToken cancellationToken)
        {
            TrackingLifecycleNodeManager? manager = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, created =>
                {
                    manager = created;
                    created.HistoryProvider = provider;
                }, namespaceUri), null, cancellationToken).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            await SeedLifecycleHistoryAsync(manager, data, 101, cancellationToken).ConfigureAwait(false);
            return (registration, manager, new NodeId(kValueNodeId, manager.NamespaceIndexes[0]));
        }

        private static IHistorianProvider CreateHistoryProbe(
            InMemoryHistorianProvider data,
            Func<HistorianOperationContext, HistorianRawReadRequest, HistorianResumeToken, CancellationToken,
                ValueTask>? onRead = null)
        {
            var provider = new Mock<IHistorianProvider>();
            provider.Setup(value => value.IsHistorizingAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken ct) => data.IsHistorizingAsync(nodeId, ct));
            provider.Setup(value => value.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken ct) => data.GetCapabilitiesAsync(nodeId, ct));
            provider.As<IHistorianDataProvider>().Setup(value => value.ReadRawAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianRawReadRequest>(),
                It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .Returns(async (HistorianOperationContext context, HistorianRawReadRequest request,
                    HistorianResumeToken token, CancellationToken ct) =>
                {
                    if (onRead is not null)
                    {
                        await onRead(context, request, token, ct).ConfigureAwait(false);
                    }
                    return await data.ReadRawAsync(context, request, token, ct).ConfigureAwait(false);
                });
            return provider.Object;
        }

        private static ReadRawModifiedDetails CreateHistoryDetails(uint maximumValues)
        {
            return new ReadRawModifiedDetails
            {
                StartTime = s_historyTime.AddSeconds(-1),
                EndTime = s_historyTime.AddSeconds(2),
                NumValuesPerNode = maximumValues
            };
        }

        private async Task SeedLifecycleHistoryAsync(
            TrackingLifecycleNodeManager manager,
            InMemoryHistorianProvider provider,
            int firstValue,
            CancellationToken cancellationToken)
        {
            BaseDataVariableState variable = RequireLifecycleValue(
                manager.Find(new NodeId(kValueNodeId, manager.NamespaceIndexes[0])) as BaseDataVariableState);
            variable.AccessLevel |= AccessLevels.HistoryRead;
            variable.UserAccessLevel |= AccessLevels.HistoryRead;
            variable.Historizing = true;
            provider.Register(variable.NodeId);
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var context = new HistorianOperationContext(
                new ServerSystemContext(m_server.CurrentInstance, operation), operation, variable,
                HistoryUpdateType.Insert);
            var statuses = await provider.InsertAsync(context, variable.NodeId,
                [
                    new DataValue(
                        new Variant(firstValue), StatusCodes.Good, s_historyTime, s_historyTime.AddSeconds(10)),
                    new DataValue(new Variant(firstValue + 1), StatusCodes.UncertainDataSubNormal,
                        s_historyTime.AddSeconds(1), s_historyTime.AddSeconds(11))
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(statuses.OperationResults.ToList(), Is.All.EqualTo(StatusCodes.GoodEntryInserted));
        }

        private static async Task<HistoryReadResult> ReadLifecycleHistoryAsync(
            Opc.Ua.Client.ISession session,
            NodeId nodeId,
            ByteString continuationPoint,
            uint maximumValues,
            bool release,
            CancellationToken cancellationToken)
        {
            HistoryReadResponse response = await session.HistoryReadAsync(null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = s_historyTime.AddSeconds(-1),
                    EndTime = s_historyTime.AddSeconds(2),
                    NumValuesPerNode = maximumValues
                }), TimestampsToReturn.Both, release,
                [new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint }],
                cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private static void AssertHistorySample(HistoryReadResult result, int value, int offset, StatusCode status)
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            AssertLifecycleValue(data);
            Assert.That(data.DataValues.Count, Is.EqualTo(1));
            DataValue sample = data.DataValues[0];
            Assert.That(sample.WrappedValue.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(value));
            Assert.That(sample.StatusCode, Is.EqualTo(status));
            Assert.That(sample.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_historyTime.AddSeconds(offset)));
            Assert.That(sample.ServerTimestamp, Is.EqualTo((DateTimeUtc)s_historyTime.AddSeconds(offset + 10)));
        }

        private sealed partial class TrackingLifecycleNodeManager
        {
            public IHistorianProvider? HistoryProvider { get; set; }

            public async ValueTask<NodeId> AddHistoryAnnotationsAsync(CancellationToken cancellationToken)
            {
                BaseVariableState parent = RequireLifecycleValue(
                    Find(new NodeId(kValueNodeId, NamespaceIndexes[0])) as BaseVariableState);
                var property = new PropertyState(parent)
                {
                    NodeId = new NodeId(8121u, NamespaceIndexes[0]),
                    BrowseName = new QualifiedName(BrowseNames.Annotations),
                    DisplayName = LocalizedText.From(BrowseNames.Annotations),
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Annotation,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.HistoryRead,
                    UserAccessLevel = AccessLevels.HistoryRead
                };
                parent.AddChild(property);
                await AddPredefinedNodeAsync(SystemContext, property, cancellationToken).ConfigureAwait(false);
                return property.NodeId;
            }

            protected override IHistorianProvider? GetHistorianProvider(NodeState node)
            {
                return HistoryProvider ?? base.GetHistorianProvider(node);
            }
        }

        private static readonly DateTime s_historyTime = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
    }
}
