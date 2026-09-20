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
        public async Task PreparedBatchGracefulBrowseRetainsCrossManagerTargetMetadataAsync(bool replace)
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
                List<ReferenceDescription> oldReferences = [.. page.References];
                TrackingLifecycleNodeManager newSource = null;
                TrackingLifecycleNodeManager newTarget = null;
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
                    ], timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        await newSource.AddContinuationChildrenAsync("New", timeout.Token).ConfigureAwait(false);
                        ConfigureBrowseDependency(newSource, newTarget, "NewTarget", VariableTypeIds.PropertyType);
                    }
                    retired = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                }
                int targetDeletedAtSwitch = target.Manager.DeleteAddressSpaceCount;
                int targetDisposedAtSwitch = target.Manager.DisposeCount;
                Assert.That(unrelated.Manager.DisposeCount, Is.EqualTo(1),
                    "An unrelated generation must not be pinned by a Browse snapshot.");
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

                for (int pages = 0; !page.ContinuationPoint.IsEmpty; pages++)
                {
                    Assert.That(pages, Is.LessThan(5));
                    BrowseNextResponse next = await session.BrowseNextAsync(
                        null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                    Assert.That(next.Results.Count, Is.EqualTo(1));
                    page = next.Results[0];
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                    oldReferences.AddRange(page.References);
                }
                using (Assert.EnterMultipleScope())
                {
                    AssertBrowseDependencyReferences(
                        new BrowseResult { References = [.. oldReferences] },
                        source.NamespaceIndex, target.NamespaceIndex, "Old", VariableTypeIds.BaseDataVariableType);
                    Assert.That(targetDeletedAtSwitch, Is.Zero);
                    Assert.That(targetDisposedAtSwitch, Is.Zero);
                    Assert.That(retired.Retired, Is.EqualTo(1u));
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
                    Assert.That(newSource.DisposeCount, Is.Zero);
                    Assert.That(newTarget.DisposeCount, Is.Zero);
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
        public async Task SharedBrowseDependenciesRetainOnlyExactOwnersUntilEveryPointDrainsAsync(bool sharedNamespace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var first = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddContinuationOwnerAsync(
                kModelNamespaceUri + ":SecondBrowseSource", timeout.Token).ConfigureAwait(false);
            string targetUri = sharedNamespace ? kModelNamespaceUri : kSecondModelNamespaceUri;
            TrackingLifecycleNodeManager unrelated = null;
            TrackingLifecycleNodeManager target = null;
            NodeManagerRegistration unrelatedRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(1, manager => unrelated = manager, targetUri),
                null, timeout.Token).ConfigureAwait(false);
            unrelated.RejectHandleLookupAfterDisposal = true;
            NodeManagerRegistration targetRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(2, manager => target = manager, targetUri),
                null, timeout.Token).ConfigureAwait(false);
            ushort targetNamespace = target.NamespaceIndexes[0];
            NodeId targetId = new(8100u, targetNamespace);
            await target.AddBrowseDependencyTargetAsync(targetId, timeout.Token).ConfigureAwait(false);
            first.Manager.Find(new NodeId(kRootNodeId, first.NamespaceIndex))
                .AddReference(ReferenceTypeIds.HasComponent, false, targetId);
            second.Manager.Find(new NodeId(kRootNodeId, second.NamespaceIndex))
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
                Assert.That(retired.Retired, Is.EqualTo(1u));
                Assert.That(unrelated.DisposeCount, Is.EqualTo(1),
                    "Sharing a namespace with a required target does not establish ownership.");
                Assert.That(target.DisposeCount, Is.Zero);
                await ReleaseContinuationAsync(session, firstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                await ReleaseContinuationAsync(session, firstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(first.Manager.DisposeCount, Is.Zero);
                Assert.That(target.DisposeCount, Is.Zero);
                await ReleaseContinuationAsync(session, anotherFirstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                await first.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.Zero);
                Assert.That(target.DisposeCount, Is.Zero,
                    "A different source still has a continuation requiring the shared target.");
                List<ReferenceDescription> references = await DrainBrowsePointAsync(
                    session, secondPoint, timeout.Token).ConfigureAwait(false);
                Assert.That(references.Select(reference => reference.NodeId), Is.EquivalentTo(new[]
                {
                    new ExpandedNodeId(kValueNodeId, second.NamespaceIndex),
                    new ExpandedNodeId(8010u, second.NamespaceIndex),
                    new ExpandedNodeId(8011u, second.NamespaceIndex),
                    new ExpandedNodeId(targetId)
                }));
                ReferenceDescription reference = references.Single(candidate => candidate.NodeId == targetId);
                Assert.That(reference.BrowseName, Is.EqualTo(new QualifiedName("SharedTarget", targetNamespace)));
                Assert.That(reference.TypeDefinition, Is.EqualTo(new ExpandedNodeId(VariableTypeIds.PropertyType)));
                await second.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.DisposeCount, Is.EqualTo(1));
                Assert.That(target.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(unrelated.RejectedHandleLookups, Is.Zero,
                    "Captured routes must not call an unrelated disposed manager in the same namespace.");
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task RestoredNativeBrowseKeepsDependencyOwnersWhileNextIsInFlightAsync()
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
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                int calls = 0;
                source.Manager.ValidateNodeCallback = async (nodeId, token) =>
                {
                    if (nodeId == new NodeId(kRootNodeId, source.NamespaceIndex) &&
                        Interlocked.Increment(ref calls) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
                        capturedTarget = master.NamespaceManagers[target.NamespaceIndex].Contains(target.Manager);
                    }
                };
                Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                        [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                    Assert.That(retired.Retired, Is.Zero);
                    Assert.That(source.Manager.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    Assert.That(pending.IsCompleted, Is.False);
                    BrowseResult current = await BrowseContinuationRootAsync(
                        session, source.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                    Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
                finally
                {
                    release.TrySetResult(true);
                }
                BrowseNextResponse next = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(next.Results[0].ContinuationPoint.IsEmpty, Is.False);
                Assert.That(capturedTarget, Is.True);
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                List<ReferenceDescription> remaining = await DrainBrowsePointAsync(
                    session, next.Results[0], timeout.Token).ConfigureAwait(false);
                Assert.That(remaining.Single(reference =>
                    reference.NodeId == new ExpandedNodeId(kValueNodeId, target.NamespaceIndex)).BrowseName,
                    Is.EqualTo(new QualifiedName("OldTarget", target.NamespaceIndex)));
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                source.Manager.ValidateNodeCallback = null;
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task FinalNativeBrowseMetadataRetainsDependenciesUntilRequestDrainsAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ConfigureBrowseDependency(
                source.Manager, target.Manager, "OldTarget", VariableTypeIds.BaseDataVariableType);
            source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex)).RemoveReference(
                ReferenceTypeIds.HasComponent, false, new NodeId(kRootNodeId, target.NamespaceIndex));
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                var holder = (ISessionContinuationPointLifecycle)serverSession.ContinuationPoints;
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 3, timeout.Token).ConfigureAwait(false);
                Assert.That(page.References.Count, Is.EqualTo(3));
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                await RetireBrowseDependenciesAsync(
                    [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                bool pointDisposedBeforeMetadata = false;
                target.Manager.ValidateNodeCallback = async (nodeId, token) =>
                {
                    if (nodeId == new NodeId(kValueNodeId, target.NamespaceIndex))
                    {
                        pointDisposedBeforeMetadata =
                            !holder.HasBrowseForManager(source.Manager) && !holder.HasBrowseForManager(target.Manager);
                        if (!pointDisposedBeforeMetadata)
                        {
                            return;
                        }
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                };
                var header = new RequestHeader { RequestHandle = 9937 };
                Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                    header, false, [page.ContinuationPoint], timeout.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(pointDisposedBeforeMetadata, Is.True,
                        "The manager has exhausted its browser, but the dispatcher still needs target metadata.");
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(source.Manager.DisposeCount, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    if (cancel)
                    {
                        CancelResponse cancelled = await session.CancelAsync(
                            null, header.RequestHandle, timeout.Token).ConfigureAwait(false);
                        Assert.That(cancelled.CancelCount, Is.EqualTo(1u));
                        BrowseNextResponse cancelledNext = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(cancelledNext.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
                        Assert.That(cancelledNext.Results[0].ContinuationPoint.IsEmpty, Is.True);
                        Assert.That(cancelledNext.Results[0].References.IsEmpty, Is.True);
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
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                target.Manager.ValidateNodeCallback = null;
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
        public async Task NativeBrowseDependencyTerminalPathsReleaseEveryOwnerAsync(string terminal)
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
            try
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                var holder = (SessionContinuationPoints)serverSession.ContinuationPoints;
                holder.MaxBrowse = 1;
                BrowseResult page = await BrowseContinuationRootAsync(
                    session, source.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
                Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                    [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                Assert.That(retired.Retired, Is.Zero);
                Assert.That(holder.HasBrowseForManager(source.Manager), Is.True);
                Assert.That(holder.HasBrowseForManager(target.Manager), Is.True);
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                switch (terminal)
                {
                    case "Release":
                        await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token)
                            .ConfigureAwait(false);
                        break;
                    case "Eviction":
                        var evicting = await AddContinuationOwnerAsync(
                            kModelNamespaceUri + ":EvictingSource", timeout.Token).ConfigureAwait(false);
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
                        source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex)).RolePermissions =
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
        public async Task ImmediateDependencyRetirementInvalidatesNativeBrowseAsync(bool retireSource)
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
                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(stale.Results[0].References.IsEmpty, Is.True);
                await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
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
        public async Task NativeLazyBrowseRequiresCompleteDependencyContractAsync(bool declaresDependencies)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var source = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            NodeId targetId = new(kValueNodeId, target.NamespaceIndex);
            int disposedBrowsers = 0;
            NodeState root = source.Manager.Find(new NodeId(kRootNodeId, source.NamespaceIndex));
            root.OnCreateBrowser =
                (context, _, view, referenceType, includeSubtypes, direction, name, additional, only) =>
                declaresDependencies
                    ? new DeclaredLazyDependencyBrowser(
                        context, view, referenceType, includeSubtypes, direction, name, additional, only, targetId,
                        () => Interlocked.Increment(ref disposedBrowsers))
                    : new OpaqueLazyDependencyBrowser(
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
                if (declaresDependencies)
                {
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(page.ContinuationPoint.IsEmpty, Is.False);
                    NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                        [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                    Assert.That(retired.Retired, Is.Zero);
                    Assert.That(target.Manager.DisposeCount, Is.Zero);
                    List<ReferenceDescription> references = await DrainBrowsePointAsync(
                        session, page, timeout.Token).ConfigureAwait(false);
                    Assert.That(references, Has.Count.EqualTo(4));
                    Assert.That(references.Single(reference => reference.NodeId == targetId).BrowseName,
                        Is.EqualTo(new QualifiedName(kValueBrowseName, target.NamespaceIndex)));
                }
                else
                {
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                        "An opaque lazy browser cannot promise ownership from its currently buffered references.");
                    Assert.That(page.ContinuationPoint.IsEmpty, Is.True);
                    Assert.That(page.References.IsEmpty, Is.True, "Unsupported pagination is not partial success.");
                    NodeManagerBatchResult retired = await RetireBrowseDependenciesAsync(
                        [source.Registration, target.Registration], false, timeout.Token).ConfigureAwait(false);
                    Assert.That(retired.Retired, Is.EqualTo(2u));
                }
                await source.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(source.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(disposedBrowsers, Is.EqualTo(2));
            }
            finally
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task SynchronousNativeBrowseRetainsItsExternalDependencyAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var target = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            ReferenceSynchronousNodeManager source = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    source = new ReferenceSynchronousNodeManager(server, configuration);
                    return new ValueTask<IAsyncNodeManager>(source.ToAsyncNodeManager());
                });
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, null, timeout.Token).ConfigureAwait(false);
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
                Assert.That(retired.Retired, Is.Zero);
                Assert.That(target.Manager.DisposeCount, Is.Zero);
                List<ReferenceDescription> references = await DrainBrowsePointAsync(
                    session, page, timeout.Token).ConfigureAwait(false);
                Assert.That(references, Has.Count.EqualTo(4));
                Assert.That(references.Single(reference => reference.NodeId == targetId).BrowseName,
                    Is.EqualTo(new QualifiedName(kValueBrowseName, target.NamespaceIndex)));
                await target.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(target.Manager.DisposeCount, Is.EqualTo(1));
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
                [.. changes], cancellationToken).ConfigureAwait(false);
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
            source.ValidateNodeCallback = async (_, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The retained Browse source failed.");
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
                            ? StatusCodes.BadRequestCancelledByRequest
                            : StatusCodes.BadUnexpectedError)).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                source.ValidateNodeCallback = null;
            }
        }

        private static void ConfigureBrowseDependency(
            TrackingLifecycleNodeManager source,
            TrackingLifecycleNodeManager target,
            string targetName,
            NodeId targetType)
        {
            ushort targetNamespace = target.NamespaceIndexes[0];
            var variable = (BaseVariableState)target.Find(new NodeId(kValueNodeId, targetNamespace));
            variable.BrowseName = new QualifiedName(targetName, targetNamespace);
            variable.DisplayName = LocalizedText.From(targetName);
            variable.TypeDefinitionId = targetType;
            NodeState root = source.Find(new NodeId(kRootNodeId, source.NamespaceIndexes[0]));
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
            ReferenceDescription target = references.SingleOrDefault(
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

            public override ValueTask<object> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                if (RejectHandleLookupAfterDisposal && DisposeCount > 0)
                {
                    Interlocked.Increment(ref m_rejectedHandleLookups);
                    throw new ObjectDisposedException(nameof(TrackingLifecycleNodeManager));
                }
                return base.GetManagerHandleAsync(nodeId, cancellationToken);
            }

            private int m_rejectedHandleLookups;
        }

        private sealed partial class ReferenceSynchronousNodeManager
        {
            public void AddBrowseDependencyReferences(NodeId targetId)
            {
                NodeState root = Find(new NodeId(kRootNodeId, NamespaceIndexes[0]));
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

        private class OpaqueLazyDependencyBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection direction,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            NodeId targetId,
            Action disposed)
            : NodeBrowser(context, view, referenceType, includeSubtypes, direction,
                browseName, additionalReferences, internalOnly)
        {
            protected NodeId DependencyTarget { get; } = targetId;

            public override IReference Next()
            {
                IReference reference = base.Next();
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

        private sealed class DeclaredLazyDependencyBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection direction,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            NodeId targetId,
            Action disposed)
            : OpaqueLazyDependencyBrowser(context, view, referenceType, includeSubtypes, direction,
                browseName, additionalReferences, internalOnly, targetId, disposed)
        {
            public override bool TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> targetIds)
            {
                targetIds = [.. GetRemainingReferenceTargets(), DependencyTarget];
                return true;
            }
        }
    }
}
