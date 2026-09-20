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
        public async Task PreparedBatchGracefulContinuationRetainsOwnerUntilFinalPageAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IServerInternal server = m_server.CurrentInstance;
            server.NamespaceUris.GetIndexOrAppend("urn:opcfoundation.org:Tests:ContinuationPadding");
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager),
                null, timeout.Token).ConfigureAwait(false);
            await originalManager.AddContinuationChildrenAsync("Old", timeout.Token).ConfigureAwait(false);
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                BrowseResult page = await BrowseContinuationRootAsync(session, ns, 1, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(page.References.Count, Is.EqualTo(1));
                Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(server.SubscriptionManager.GetSubscriptions(), Is.Empty);
                List<ReferenceDescription> references = [.. page.References];
                TrackingLifecycleNodeManager replacement = null;
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                NodeManagerBatchResult result;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        replace
                            ? NodeManagerBatchChange.Replace(original, CreateTrackingNodeManagementFactory(
                                kGeneration2Value, manager => replacement = manager))
                            : NodeManagerBatchChange.Remove(original)
                    ], timeout.Token).ConfigureAwait(false))
                {
                    if (replace)
                    {
                        await replacement.AddContinuationChildrenAsync("New", timeout.Token).ConfigureAwait(false);
                    }
                    result = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                }
                int deletedAfterCommit = originalManager.DeleteAddressSpaceCount;
                int disposedAfterCommit = originalManager.DisposeCount;
                BrowseResult current = await BrowseContinuationRootAsync(session, ns, 0, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(replace ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (replace)
                {
                    ReferenceDescription[] currentReferences = [.. current.References];
                    Assert.That(currentReferences.Select(reference => reference.BrowseName.Name),
                        Is.EquivalentTo(new[] { kValueBrowseName, "NewFirst", "NewSecond" }));
                }

                for (int pages = 0; page.ContinuationPoint.Length > 0; pages++)
                {
                    Assert.That(pages, Is.LessThan(4), "The old generation has exactly three matching references.");
                    BrowseNextResponse next = await session.BrowseNextAsync(
                        null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                    Assert.That(next.Results.Count, Is.EqualTo(1));
                    page = next.Results[0];
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good),
                            "A saved native continuation must retain its graceful old generation.");
                        Assert.That(deletedAfterCommit, Is.Zero);
                        Assert.That(disposedAfterCommit, Is.Zero);
                        Assert.That(result.Retired, Is.Zero);
                        Assert.That(result.CleanupFailure, Is.Null);
                    }
                    references.AddRange(page.References);
                    if (page.ContinuationPoint.Length > 0)
                    {
                        Assert.That(originalManager.DisposeCount, Is.Zero);
                    }
                }
                Assert.That(references.Select(reference => reference.BrowseName.Name),
                    Is.EquivalentTo(new[] { kValueBrowseName, "OldFirst", "OldSecond" }));
                Assert.That(references.Select(reference => reference.NodeId),
                    Is.EquivalentTo(new[]
                    {
                        new ExpandedNodeId(kValueNodeId, ns),
                        new ExpandedNodeId(8010u, ns),
                        new ExpandedNodeId(8011u, ns)
                    }));
                Assert.That(references.All(reference =>
                    reference.IsForward &&
                    reference.NodeClass == NodeClass.Variable &&
                    reference.ReferenceTypeId == ReferenceTypeIds.HasComponent), Is.True);
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
        public async Task GracefulContinuationsReleaseOnlyTheirExactIndependentOwnersAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var first = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var second = await AddContinuationOwnerAsync(kSecondModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var unowned = await AddContinuationOwnerAsync(
                "urn:opcfoundation.org:Tests:ContinuationUnowned", timeout.Token).ConfigureAwait(false);
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
                Assert.That(firstPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(anotherFirstPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                Assert.That(secondPoint.ContinuationPoint.Length, Is.GreaterThan(0));
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Remove(first.Registration),
                        NodeManagerBatchChange.Remove(second.Registration),
                        NodeManagerBatchChange.Remove(unowned.Registration)
                    ], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Retired, Is.EqualTo(1u));
                }
                Assert.That(unowned.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.Zero);
                Assert.That(second.Manager.DisposeCount, Is.Zero);

                await ReleaseContinuationAsync(session, firstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                await ReleaseContinuationAsync(session, firstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(first.Manager.DisposeCount, Is.Zero,
                    "Releasing one point twice cannot release another point owned by the same generation.");
                Assert.That(second.Manager.DisposeCount, Is.Zero);
                await ReleaseContinuationAsync(session, secondPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                await second.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(second.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(second.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.Zero);
                await ReleaseContinuationAsync(session, anotherFirstPoint.ContinuationPoint, timeout.Token)
                    .ConfigureAwait(false);
                await first.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(first.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(first.Manager.DisposeCount, Is.EqualTo(1));
                Assert.That(unowned.Manager.DisposeCount, Is.EqualTo(1));
                BrowseNextResponse stale = await session.BrowseNextAsync(
                    null, false, [anotherFirstPoint.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
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
        public async Task GracefulContinuationSessionTeardownReleasesOwnerAsync(bool dispose)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            NodeManagerBatchResult result = await RetireContinuationOwnerAsync(
                owner.Registration, false, timeout.Token).ConfigureAwait(false);
            Assert.That(result.Retired, Is.Zero);
            Assert.That(owner.Manager.DisposeCount, Is.Zero);
            if (dispose)
            {
                ISession serverSession = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(candidate => candidate.Id == session.SessionId);
                serverSession.Dispose();
                serverSession.Dispose();
            }
            else
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            if (dispose)
            {
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task GracefulContinuationKeepsCheckedOutOwnerAndCapturedRoutesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            bool capturedRoutes = false;
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            owner.Manager.ValidateNodeCallback = async (nodeId, token) =>
            {
                if (nodeId == new NodeId(kRootNodeId, owner.NamespaceIndex) &&
                    Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    capturedRoutes = master.NamespaceManagers[owner.NamespaceIndex].Contains(owner.Manager);
                }
            };
            Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await RetireContinuationOwnerAsync(
                    owner.Registration, false, timeout.Token).ConfigureAwait(false);
                Assert.That(result.Retired, Is.Zero);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                BrowseResult fresh = await BrowseContinuationRootAsync(
                    session, owner.NamespaceIndex, 0, timeout.Token).ConfigureAwait(false);
                Assert.That(fresh.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            finally
            {
                release.TrySetResult(true);
            }
            BrowseNextResponse next = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(next.Results[0].ContinuationPoint.Length, Is.GreaterThan(0));
            Assert.That(capturedRoutes, Is.True);
            Assert.That(owner.Manager.DisposeCount, Is.Zero);
            await ReleaseContinuationAsync(session, next.Results[0].ContinuationPoint, timeout.Token)
                .ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task GracefulContinuationFailedOrCancelledNextReleasesOwnerAsync(bool releasePoint, bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
            owner.Manager.ValidateNodeCallback = async (_, token) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new IOException("The old source failed while validating BrowseNext.");
            };
            var header = new RequestHeader { RequestHandle = 9917 };
            Task<BrowseNextResponse> pending = session.BrowseNextAsync(
                header, releasePoint, [page.ContinuationPoint], timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.Manager.DisposeCount, Is.Zero);
                if (cancel)
                {
                    CancelResponse cancelled = await session.CancelAsync(null, header.RequestHandle, timeout.Token)
                        .ConfigureAwait(false);
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
                owner.Manager.ValidateNodeCallback = null;
            }
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task GracefulContinuationPermissionDenialReleasesOwnerAsync(bool releasePoint)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
            NodeState root = owner.Manager.Find(new NodeId(kRootNodeId, owner.NamespaceIndex));
            root.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)PermissionType.Read
                }
            ];
            BrowseNextResponse denied = await session.BrowseNextAsync(
                null, releasePoint, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(denied.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.Results[0].ContinuationPoint.IsEmpty, Is.True);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchImmediateRetirementInvalidatesNativeContinuationAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    replace
                        ? NodeManagerBatchChange.Replace(owner.Registration,
                            CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), true)
                        : NodeManagerBatchChange.Remove(owner.Registration, true)
                ], timeout.Token).ConfigureAwait(false))
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.Retired, Is.EqualTo(1u));
                Assert.That(result.CleanupFailure, Is.Null);
            }
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            BrowseNextResponse stale = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(stale.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            await ReleaseContinuationAsync(session, page.ContinuationPoint, timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ShadowReloadRetainsNativeContinuationWithoutMonitoredItemsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            await m_server.NodeManagerLifecycle.ShadowReloadAsync(
                owner.Registration, CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }),
                ct: timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.Zero);
            BrowseNextResponse next = await session.BrowseNextAsync(
                null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
            Assert.That(next.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(next.Results[0].ContinuationPoint.Length, Is.GreaterThan(0));
            await ReleaseContinuationAsync(session, next.Results[0].ContinuationPoint, timeout.Token)
                .ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task ShutdownDisposesGracefulOwnerWithOutstandingNativeContinuationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var owner = await AddContinuationOwnerAsync(kModelNamespaceUri, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResult page = await BrowseContinuationRootAsync(
                session, owner.NamespaceIndex, 1, timeout.Token).ConfigureAwait(false);
            Assert.That(page.ContinuationPoint.Length, Is.GreaterThan(0));
            await RetireContinuationOwnerAsync(owner.Registration, false, timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DisposeCount, Is.Zero);
            await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
            await owner.Manager.DisposalCompleted.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(owner.Manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(owner.Manager.DisposeCount, Is.EqualTo(1));
            AssertServerInternalsDisposed();
            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        private async Task<(NodeManagerRegistration Registration, TrackingLifecycleNodeManager Manager,
            ushort NamespaceIndex)> AddContinuationOwnerAsync(string namespaceUri, CancellationToken cancellationToken)
        {
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, created => manager = created, namespaceUri),
                null, cancellationToken).ConfigureAwait(false);
            await manager.AddContinuationChildrenAsync("Old", cancellationToken).ConfigureAwait(false);
            return (registration, manager, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri));
        }

        private async Task<NodeManagerBatchResult> RetireContinuationOwnerAsync(
            NodeManagerRegistration registration,
            bool immediate,
            CancellationToken cancellationToken)
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Remove(registration, immediate)], cancellationToken).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(m_server.CurrentInstance.SubscriptionManager.GetSubscriptions(), Is.Empty);
            return result;
        }

        private static async Task ReleaseContinuationAsync(
            Opc.Ua.Client.ISession session,
            ByteString continuationPoint,
            CancellationToken cancellationToken)
        {
            BrowseNextResponse response = await session.BrowseNextAsync(
                null, true, [continuationPoint], cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(response.Results[0].References.IsEmpty, Is.True);
        }

        private static async Task<BrowseResult> BrowseContinuationRootAsync(
            Opc.Ua.Client.ISession session,
            ushort namespaceIndex,
            uint maximumReferences,
            CancellationToken cancellationToken)
        {
            BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), maximumReferences,
                [
                    new BrowseDescription
                    {
                        NodeId = new NodeId(kRootNodeId, namespaceIndex),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
