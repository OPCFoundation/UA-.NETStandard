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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchDoesNotRevertAcceptedNamespaceRemovalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration survivor = RequireRegistration(publicationResult1.Registrations[0]);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            NodeId valueId = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            DataValue original = await ReadAsync().ConfigureAwait(false);
            Assert.That(original.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(original.WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
            BrowseResult originalRoute = await BrowseAsync().ConfigureAwait(false);
            Assert.That(originalRoute.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(originalRoute.References.Contains(reference =>
                reference.NodeId == new ExpandedNodeId(valueId)),
                Is.True);

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            Task<bool>? removal = null;
            BrowseResult during;
            bool routedDuring;
            LifecycleResult result;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                removal = Task.Run(() => master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager));
                Assert.That(await removal.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false),
                    Is.True, "An unrelated surviving route must make progress while the decision is held.");
                Assert.That(commit.IsCompleted, Is.False);
                routedDuring = master.NamespaceManagers.ContainsKey(valueId.NamespaceIndex);
                during = await BrowseAsync().ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            bool accepted = await removal!.WaitAsync(timeout.Token).ConfigureAwait(false);
            BrowseResult after = await BrowseAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(accepted, Is.True, "The surviving manager owns this namespace route.");
                Assert.That(routedDuring, Is.False);
                Assert.That(master.NamespaceManagers.ContainsKey(valueId.NamespaceIndex),
                    Is.False, "The exact surviving namespace route must not be resurrected.");
                Assert.That(during.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                    "The unrelated route removal is authoritative before the other unit commits.");
                Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                    "A successful namespace removal must not be silently undone by prepared publication.");
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async Task<DataValue> ReadAsync()
            {
                ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = valueId, AttributeId = Attributes.Value }], timeout.Token)
                    .ConfigureAwait(false);
                return response.Results[0];
            }

            async Task<BrowseResult> BrowseAsync()
            {
                BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = new NodeId(kRootNodeId, valueId.NamespaceIndex),
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], timeout.Token).ConfigureAwait(false);
                return response.Results[0];
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchRejectsNamespaceRoutingChangesBeforeDecisionAsync(bool register)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration survivor = RequireRegistration(publicationResult2.Registrations[0]);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            if (register)
            {
                Assert.That(master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager), Is.True);
            }
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            if (register)
            {
                master.RegisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager);
            }
            else
            {
                Assert.That(master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager), Is.True);
            }
            int decisions = 0;
            long revision = lifecycle.Revision;
            await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(1));
            NodeId root = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            (_, IAsyncNodeManager? owner) = await master.GetManagerHandleAsync(root, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(owner, register ? Is.SameAs(survivor.NodeManager) : Is.Null);
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            master.RegisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager);
            Assert.That(master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager), Is.True);
            master.RegisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager);
        }

        [TestCase(false, "Commit")]
        [TestCase(true, "Commit")]
        [TestCase(false, "Reject")]
        [TestCase(true, "Reject")]
        [TestCase(false, "Cancel")]
        [TestCase(true, "Cancel")]
        public async Task PreparedBatchSerializesNamespaceWritesUntilDecisionCompletesAsync(
            bool register, string outcome)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var decisionCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration survivor = RequireRegistration(publicationResult3.Registrations[0]);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            const string conflictingUri = kSecondModelNamespaceUri;
            m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(conflictingUri);
            if (!register)
            {
                master.RegisterNamespaceManager(conflictingUri, survivor.NodeManager);
            }
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            int namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(conflictingUri);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (outcome == "Reject")
                {
                    throw new IOException("Confirmed routing noncommit.");
                }
            }, decisionCancellation.Token).AsTask();
            Task? pendingMutation = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                pendingMutation = Task.Run(Mutate);
                await Task.Delay(250, timeout.Token).ConfigureAwait(false);
                Assert.That(pendingMutation.IsCompleted, Is.False,
                    "A namespace write waits for the pending publication decision.");
                Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.EqualTo(!register));
                if (!register)
                {
                    Assert.That(master.NamespaceManagers[namespaceIndex], Is.EqualTo(new[] { survivor.NodeManager }));
                }
            }
            finally
            {
                if (outcome == "Cancel")
                {
                    decisionCancellation.Cancel();
                }
                release.TrySetResult(true);
                if (outcome == "Reject")
                {
                    await Assert.ThatAsync(() => commit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                else if (outcome == "Cancel")
                {
                    await Assert.ThatAsync(() => commit, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    LifecycleResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            Assert.That(prepared.IsCommitted, Is.EqualTo(outcome == "Commit"));
            await pendingMutation!.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(HasSurvivorRoute(), Is.EqualTo(register));
            if (register)
            {
                Assert.That(master.NamespaceManagers[namespaceIndex], Does.Contain(survivor.NodeManager));
            }
            await prepared.DisposeAsync().ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(HasSurvivorRoute(), Is.EqualTo(register),
                "Aborted or committed owner cleanup must not undo a later successful namespace write.");
            master.RegisterNamespaceManager(conflictingUri, survivor.NodeManager);
            Assert.That(master.UnregisterNamespaceManager(conflictingUri, survivor.NodeManager), Is.True);
            master.RegisterNamespaceManager(conflictingUri, survivor.NodeManager);

            bool HasSurvivorRoute()
            {
                return master.NamespaceManagers.TryGetValue(namespaceIndex, out var owners) &&
                    owners.Contains(survivor.NodeManager);
            }

            void Mutate()
            {
                if (register)
                {
                    master.RegisterNamespaceManager(conflictingUri, survivor.NodeManager);
                }
                else
                {
                    Assert.That(master.UnregisterNamespaceManager(conflictingUri, survivor.NodeManager), Is.True);
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchAllowsNamespaceWritesInCommittedCallbacksAsync(bool reconcile, bool register)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            LifecycleResult publicationResult4 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration survivor = RequireRegistration(publicationResult4.Registrations[0]);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            if (register)
            {
                Assert.That(master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager), Is.True);
            }
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? candidate = null;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, manager => candidate = manager, kSecondModelNamespaceUri))],
                        timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager candidateManager = candidate ??
                throw new AssertionException("The prepared callback generation was not created.");
            int mutations = 0;
            if (reconcile)
            {
                candidateManager.SessionActivatedCallback = _ =>
                {
                    MutateOnce();
                    return default;
                };
            }
            LifecycleResult result = await prepared.CommitAsync(_ => default, () =>
            {
                if (!reconcile)
                {
                    MutateOnce();
                }
            }, timeout.Token).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.CleanupFailure, Is.Null,
                "The routing reservation must release before committed-state and binding callbacks.");
            Assert.That(mutations, Is.EqualTo(1));
            ushort namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            BrowseResponse browse = await session.BrowseAsync(null, new ViewDescription(), 0,
                [
                    new BrowseDescription
                    {
                        NodeId = new NodeId(kRootNodeId, namespaceIndex),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], timeout.Token).ConfigureAwait(false);
            Assert.That(browse.Results[0].StatusCode,
                Is.EqualTo(register ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown),
                "An accepted callback write must remain effective through the rest of the commit.");
            if (register)
            {
                Assert.That(browse.Results[0].References.Contains(reference =>
                    reference.NodeId == new ExpandedNodeId(kValueNodeId, namespaceIndex)), Is.True);
                Assert.That(master.NamespaceManagers[namespaceIndex], Is.EqualTo(new[] { survivor.NodeManager }));
            }
            else
            {
                Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);
            }
            NodeId candidateValue = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = candidateValue, AttributeId = Attributes.Value }], timeout.Token)
                .ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
            candidateManager.SessionActivatedCallback = _ => default;
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            void MutateOnce()
            {
                if (Interlocked.CompareExchange(ref mutations, 1, 0) != 0)
                {
                    return;
                }
                Assert.That(prepared.IsCommitted, Is.True);
                if (register)
                {
                    master.RegisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager);
                }
                else
                {
                    Assert.That(master.UnregisterNamespaceManager(kModelNamespaceUri, survivor.NodeManager), Is.True);
                }
            }
        }
    }
}
