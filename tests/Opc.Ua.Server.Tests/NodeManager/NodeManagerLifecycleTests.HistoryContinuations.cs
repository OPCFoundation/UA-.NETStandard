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
        public async Task GracefulHistoryContinuationRetainsOriginalFinalPageAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var oldProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var newProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager =>
                {
                    originalManager = manager;
                    manager.HistoryProvider = oldProvider;
                }), null, timeout.Token).ConfigureAwait(false);
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
                TrackingLifecycleNodeManager replacement = null;
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
                    ], timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        await SeedLifecycleHistoryAsync(replacement, newProvider, 201, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    retired = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                }
                int deletedAtSwitch = originalManager.DeleteAddressSpaceCount;
                int disposedAtSwitch = originalManager.DisposeCount;
                HistoryReadResult current = await ReadLifecycleHistoryAsync(
                    session, nodeId, ByteString.Empty, 0, false, timeout.Token).ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (replace)
                {
                    Assert.That(current.HistoryData.TryGetValue(out HistoryData currentData), Is.True);
                    Assert.That(currentData.DataValues.Count, Is.EqualTo(2));
                    Assert.That(currentData.DataValues[0].WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(201));
                    Assert.That(current.ContinuationPoint.IsEmpty, Is.True);
                }
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, nodeId, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(final.StatusCode, Is.EqualTo(StatusCodes.Good),
                        "A native HistoryRead continuation must retain the exact gracefully retired generation.");
                    Assert.That(deletedAtSwitch, Is.Zero);
                    Assert.That(disposedAtSwitch, Is.Zero);
                    Assert.That(retired.Retired, Is.Zero);
                    Assert.That(retired.CleanupFailure, Is.Null);
                }
                AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                Assert.That(final.ContinuationPoint.IsEmpty, Is.True);
                await originalManager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (replace)
                {
                    Assert.That(replacement.DisposeCount, Is.Zero);
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
        public async Task SharedHistoryDependenciesDrainOnlyAfterEveryExactPointAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var unrelated = await AddContinuationOwnerAsync(
                kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            unrelated.Manager.RejectHandleLookupAfterDisposal = true;
            NodeId targetId = new(8100u, target.NamespaceIndex);
            await target.Manager.AddBrowseDependencyTargetAsync(targetId, timeout.Token).ConfigureAwait(false);
            using var firstData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var secondData = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var first = await AddHistoryOwnerAsync(firstData,
                CreateHistoryProbe(firstData, [targetId]), kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddHistoryOwnerAsync(secondData,
                CreateHistoryProbe(secondData, [targetId]), kModelNamespaceUri + ":Second", timeout.Token)
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
                HistoryReadResult anotherFirstPoint = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResult secondPoint = await ReadLifecycleHistoryAsync(
                    session, second.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(firstPoint.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(anotherFirstPoint.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(secondPoint.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [first.Registration, second.Registration, target.Registration, unrelated.Registration],
                    false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.EqualTo(1u));
                Assert.That(unrelated.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.Zero);
                Assert.That(second.Manager.DisposeCount, Is.Zero);
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                HistoryReadResult released = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, firstPoint.ContinuationPoint, 1, true, timeout.Token).ConfigureAwait(false);
                Assert.That(released.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(released.ContinuationPoint.IsEmpty, Is.True);
                HistoryReadResult duplicate = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, firstPoint.ContinuationPoint, 1, true, timeout.Token).ConfigureAwait(false);
                Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(first.Manager.DisposeCount, Is.Zero);
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                HistoryReadResult firstFinal = await ReadLifecycleHistoryAsync(
                    session, first.NodeId, anotherFirstPoint.ContinuationPoint, 1, false, timeout.Token)
                    .ConfigureAwait(false);
                AssertHistorySample(firstFinal, 102, 1, StatusCodes.UncertainDataSubNormal);
                await first.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                HistoryReadResult secondFinal = await ReadLifecycleHistoryAsync(
                    session, second.NodeId, secondPoint.ContinuationPoint, 1, false, timeout.Token)
                    .ConfigureAwait(false);
                AssertHistorySample(secondFinal, 102, 1, StatusCodes.UncertainDataSubNormal);
                await second.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
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
        public async Task CheckedOutHistoryKeepsOriginalSourceDependenciesAndRoutingAsync(bool retireBeforeRead)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            NodeId targetId = new(kValueNodeId, target.NamespaceIndex);
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            HistorianOperationContext observed = null;
            bool capturedSource = false;
            bool capturedTarget = false;
            TrackingLifecycleNodeManager source = null;
            IHistorianProvider provider = CreateHistoryProbe(data, [targetId], async (context, _, token, ct) =>
            {
                if (token.IsEmpty)
                {
                    return;
                }
                observed = context;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
                capturedSource = master.NamespaceManagers[source.NamespaceIndexes[0]].Contains(source);
                capturedTarget = master.NamespaceManagers[target.NamespaceIndex].Contains(target.Manager);
            });
            var owner = await AddHistoryOwnerAsync(data, provider, kModelNamespaceUri, timeout.Token)
                .ConfigureAwait(false);
            source = owner.Manager;
            NodeState originalNode = source.Find(owner.NodeId);
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
                }
                Task<HistoryReadResponse> pending = session.HistoryReadAsync(
                    null, new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                    [new HistoryReadValueId { NodeId = owner.NodeId, ContinuationPoint = page.ContinuationPoint }],
                    timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    if (!retireBeforeRead)
                    {
                        await RetireBrowseDependenciesAsync(
                            [owner.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                    }
                    var holder = (ISessionHistoryContinuationPointLifecycle)m_server.CurrentInstance.SessionManager
                        .GetSessions().Single(candidate => candidate.Id == session.SessionId).ContinuationPoints;
                    Assert.That(holder.HasHistoryForManager(source), Is.True);
                    Assert.That(holder.HasHistoryForManager(target.Manager), Is.True);
                    Assert.That(source.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    Assert.That(observed.Node, Is.SameAs(originalNode));
                    Assert.That(observed.OperationContext.Session.Id, Is.EqualTo(session.SessionId));
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
                data, [new NodeId(kValueNodeId, target.NamespaceIndex)], async (_, _, token, ct) =>
                {
                    if (!token.IsEmpty && terminal is "Failure" or "Cancellation")
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        throw new IOException("History source failed while reading the retained page.");
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
                var holder = (ISessionHistoryContinuationPointLifecycle)serverSession.ContinuationPoints;
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [owner.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.Zero);
                Assert.That(holder.HasHistoryForManager(owner.Manager), Is.True);
                Assert.That(holder.HasHistoryForManager(target.Manager), Is.True);
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
                                        ? StatusCodes.BadRequestCancelledByRequest
                                        : StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
                        break;
                    case "PermissionDenied":
                        owner.Manager.Find(owner.NodeId).RolePermissions =
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
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(holder.HasHistoryForManager(owner.Manager), Is.False);
                Assert.That(holder.HasHistoryForManager(target.Manager), Is.False);
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
                    ], timeout.Token).ConfigureAwait(false))
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
        public async Task ImmediateHistoryDependencyRetirementInvalidatesSurvivingSourcePointAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data, [new NodeId(kValueNodeId, target.NamespaceIndex)]),
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

        [TestCase("Missing")]
        [TestCase("Incomplete")]
        [TestCase("Declared")]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task DynamicHistoryPaginationRequiresTruthfulDependencyCapabilityAsync(string capability)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data, [], capability: capability), kModelNamespaceUri, timeout.Token)
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
                Assert.That(unpaged.HistoryData.TryGetValue(out HistoryData all), Is.True);
                Assert.That(all.DataValues.Count, Is.EqualTo(2));
                HistoryReadResult page = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, ByteString.Empty, 1, false, timeout.Token).ConfigureAwait(false);
                if (capability == "Declared")
                {
                    AssertHistorySample(page, 101, 0, StatusCodes.Good);
                    Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                    await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                    Assert.That(owner.Manager.DisposeCount, Is.Zero);
                    HistoryReadResult final = await ReadLifecycleHistoryAsync(
                        session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                    AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                    await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(page.HistoryData.IsNull, Is.True);
                    Assert.That(page.ContinuationPoint.IsEmpty, Is.True);
                    NodeManagerBatchResult retired = await RetireContinuationOwnerAsync(
                        owner.Registration, false, timeout.Token).ConfigureAwait(false);
                    Assert.That(retired.Retired, Is.EqualTo(1u));
                }
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
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                HistoryReadResult foreignResult = await ReadLifecycleHistoryAsync(
                    foreign, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(foreignResult.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(foreignResult.HistoryData.IsNull, Is.True);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResult wrongOrigin = await ReadLifecycleHistoryAsync(
                    session, ObjectIds.Server, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(wrongOrigin.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(wrongOrigin.HistoryData.IsNull, Is.True);
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
                    secondData, [], (_, _, _, _) => throw new IOException("Second history read failed.")),
                kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var context = new HistorianOperationContext(
                new ServerSystemContext(m_server.CurrentInstance, operation), operation,
                first.Manager.Find(first.NodeId),
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
                var holder = (ISessionHistoryContinuationPointLifecycle)m_server.CurrentInstance.SessionManager
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
        public async Task FirstHistoryPageInFlightAtRetirementAcquiresItsOldOwnersAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var owner = await AddHistoryOwnerAsync(data,
                CreateHistoryProbe(data, [], async (_, _, token, ct) =>
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
                [NodeManagerBatchChange.Remove(owner.Registration)], timeout.Token).ConfigureAwait(false);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Task<HistoryReadResponse> pending = session.HistoryReadAsync(
                    null, new ExtensionObject(CreateHistoryDetails(1)), TimestampsToReturn.Both, false,
                    [new HistoryReadValueId { NodeId = owner.NodeId }], timeout.Token).AsTask();
                Task<NodeManagerBatchResult> committing = null;
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
                Assert.That(retired.Retired, Is.Zero);
                HistoryReadResponse response = await pending.ConfigureAwait(false);
                HistoryReadResult page = response.Results[0];
                AssertHistorySample(page, 101, 0, StatusCodes.Good);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, page.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
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
        public async Task NativeEventHistoryRetainsOldFilterSourceAndValuesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var data = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            var owner = await AddHistoryOwnerAsync(data, data, kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            owner.Manager.HistoryProvider = CreatePagedEventHistoryProbe(data);
            var notifier = (BaseObjectState)owner.Manager.Find(
                new NodeId(kRootNodeId, owner.Manager.NamespaceIndexes[0]));
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
                var altered = new EventFilter
                {
                    WhereClause = new ContentFilter
                    {
                        Elements =
                        [
                            new ContentFilterElement
                            {
                                FilterOperator = FilterOperator.Equals,
                                FilterOperands =
                                [
                                    new ExtensionObject(new LiteralOperand { Value = new Variant(true) }),
                                    new ExtensionObject(new LiteralOperand { Value = new Variant(false) })
                                ]
                            }
                        ]
                    }
                };
                altered.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventType, Attributes.Value);
                HistoryReadResponse resumed = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadEventDetails { Filter = altered }),
                    TimestampsToReturn.Source, false,
                    [new HistoryReadValueId
                    {
                        NodeId = notifier.NodeId,
                        ContinuationPoint = first.Results[0].ContinuationPoint
                    }], timeout.Token).ConfigureAwait(false);
                Assert.That(resumed.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(resumed.Results[0].HistoryData.TryGetValue(out HistoryEvent history), Is.True);
                Assert.That(history.Events.Count, Is.EqualTo(1));
                ArrayOf<Variant> fields = history.Events[0].EventFields;
                Assert.That(fields.Count, Is.EqualTo(3));
                Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                Assert.That(eventId, Is.EqualTo(secondId));
                Assert.That(fields[1].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("second"));
                Assert.That(fields[2].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo((DateTimeUtc)s_historyTime.AddSeconds(1)));
                Assert.That(resumed.Results[0].ContinuationPoint.IsEmpty, Is.True);
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
        public async Task NativeAnnotationHistoryKeepsOriginalPropertyAndParentSourceAsync()
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
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, annotations, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(final.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(final.HistoryData.TryGetValue(out HistoryData values), Is.True);
                Assert.That(values.DataValues.Count, Is.EqualTo(1));
                Assert.That(values.DataValues[0].WrappedValue.TryGetValue(out ExtensionObject encoded), Is.True);
                Assert.That(encoded.TryGetValue(out Annotation annotation), Is.True);
                Assert.That(annotation.Message, Is.EqualTo("second"));
                Assert.That(annotation.UserName, Is.EqualTo("history-owner"));
                Assert.That(annotation.AnnotationTime, Is.EqualTo((DateTimeUtc)s_historyTime.AddSeconds(1)));
                Assert.That(final.ContinuationPoint.IsEmpty, Is.False,
                    "The stock annotation provider issues a terminal empty page after a full page.");
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResult drained = await ReadLifecycleHistoryAsync(
                    session, annotations, final.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                Assert.That(drained.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(drained.ContinuationPoint.IsEmpty, Is.True);
                Assert.That(drained.HistoryData.TryGetValue(out HistoryData empty), Is.True);
                Assert.That(empty.DataValues.IsEmpty, Is.True);
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
        public async Task NativeBufferedProcessedHistoryKeepsEachOldPointAndOriginalRequestAsync(bool twoPoints)
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
                    Assert.That(first.Results[index].HistoryData.TryGetValue(out HistoryData page), Is.True);
                    Assert.That(page.DataValues.Count, Is.EqualTo(1000));
                    reads[index].ContinuationPoint = first.Results[index].ContinuationPoint;
                }
                await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                HistoryReadResponse resumed = await session.HistoryReadAsync(
                    null, new ExtensionObject(new ReadProcessedDetails { AggregateType = aggregates }),
                    TimestampsToReturn.Server, false, reads, timeout.Token).ConfigureAwait(false);
                Assert.That(resumed.Results.Count, Is.EqualTo(twoPoints ? 2 : 1));
                foreach (HistoryReadResult result in resumed.Results)
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
                    Assert.That(result.HistoryData.TryGetValue(out HistoryData final), Is.True);
                    Assert.That(final.DataValues.Count, Is.EqualTo(1));
                    Assert.That(final.DataValues[0].WrappedValue.TryGetValue(out int count), Is.True);
                    Assert.That(count, Is.EqualTo(1));
                    Assert.That(final.DataValues[0].SourceTimestamp,
                        Is.EqualTo((DateTimeUtc)s_historyTime.AddSeconds(1)));
                    Assert.That(final.DataValues[0].StatusCode, Is.EqualTo(StatusCodes.Good));
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
        public async Task NativeHistoryContinuationKeepsCapturedProviderAfterUnbindingAsync()
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
                HistoryReadResult final = await ReadLifecycleHistoryAsync(
                    session, owner.NodeId, first.ContinuationPoint, 1, false, timeout.Token).ConfigureAwait(false);
                AssertHistorySample(final, 102, 1, StatusCodes.UncertainDataSubNormal);
                Assert.That(final.ContinuationPoint.IsEmpty, Is.True);
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
                .Returns((NodeId nodeId, CancellationToken ct) => data.GetCapabilitiesAsync(nodeId, ct));
            ArrayOf<NodeId> dependencies = [];
            provider.As<IHistorianContinuationDependencies>().Setup(value => value.TryGetContinuationDependencies(
                It.IsAny<NodeId>(), It.IsAny<HistorianResumeToken>(), out dependencies)).Returns(true);
            provider.As<IHistorianEventProvider>().Setup(value => value.ReadEventsAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianEventReadRequest>(),
                It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .Returns(async (HistorianOperationContext context, HistorianEventReadRequest request,
                    HistorianResumeToken token, CancellationToken ct) =>
                {
                    HistorianPage<HistorianEventRecord> all = await data.ReadEventsAsync(
                        context, request with { MaxValues = 0 }, default, ct).ConfigureAwait(false);
                    Assert.That(all.Values, Has.Count.EqualTo(2));
                    if (token.IsEmpty)
                    {
                        return new HistorianPage<HistorianEventRecord>(
                            [all.Values[0]], new HistorianResumeToken("second"u8.ToArray()));
                    }
                    Assert.That(token.State.Span.SequenceEqual("second"u8), Is.True);
                    return new HistorianPage<HistorianEventRecord>([all.Values[1]]);
                });
            return provider.Object;
        }

        private static HistorianEventRecord CreateHistoryEvent(ByteString id, string message, DateTime time)
        {
            return new HistorianEventRecord(id, ObjectTypeIds.BaseEventType, time,
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(id),
                    [BrowseNames.EventType] = new Variant(ObjectTypeIds.BaseEventType),
                    [BrowseNames.Message] = new Variant(new LocalizedText(message)),
                    [BrowseNames.Time] = new Variant((DateTimeUtc)time)
                });
        }

        private async Task SeedHistoryAnnotationsAsync(
            TrackingLifecycleNodeManager manager,
            InMemoryHistorianProvider provider,
            CancellationToken cancellationToken)
        {
            NodeState node = manager.Find(new NodeId(kValueNodeId, manager.NamespaceIndexes[0]));
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
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, created =>
                {
                    manager = created;
                    created.HistoryProvider = provider;
                }, namespaceUri), null, cancellationToken).ConfigureAwait(false);
            await SeedLifecycleHistoryAsync(manager, data, 101, cancellationToken).ConfigureAwait(false);
            return (registration, manager, new NodeId(kValueNodeId, manager.NamespaceIndexes[0]));
        }

        private static IHistorianProvider CreateHistoryProbe(
            InMemoryHistorianProvider data,
            ArrayOf<NodeId> dependencies,
            Func<HistorianOperationContext, HistorianRawReadRequest, HistorianResumeToken, CancellationToken,
                ValueTask> onRead = null,
            string capability = "Declared")
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
            if (capability != "Missing")
            {
                provider.As<IHistorianContinuationDependencies>().Setup(value => value.TryGetContinuationDependencies(
                    It.IsAny<NodeId>(), It.IsAny<HistorianResumeToken>(), out dependencies))
                    .Returns(capability == "Declared");
            }
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
            var variable = (BaseDataVariableState)manager.Find(new NodeId(kValueNodeId, manager.NamespaceIndexes[0]));
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
            Assert.That(statuses, Is.All.EqualTo(StatusCodes.GoodEntryInserted));
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
            Assert.That(result.HistoryData.TryGetValue(out HistoryData data), Is.True);
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
            public IHistorianProvider HistoryProvider { get; set; }

            public async ValueTask<NodeId> AddHistoryAnnotationsAsync(CancellationToken cancellationToken)
            {
                var parent = (BaseVariableState)Find(new NodeId(kValueNodeId, NamespaceIndexes[0]));
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

            protected override IHistorianProvider GetHistorianProvider(NodeState node)
            {
                return HistoryProvider ?? base.GetHistorianProvider(node);
            }
        }

        private static readonly DateTime s_historyTime = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
    }
}
