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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A candidate getter cannot synchronously re-enter serving namespace admission before the decision starts.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceGetterRejectsServingReentryButKeepsStagedMutationsLocalAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? serving = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, value => serving = value),
                null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(serving);
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            const string AliasUri = kModelNamespaceUri + ":GetterServingAlias";
            const string StagedAliasUri = kSecondModelNamespaceUri + ":GetterStagedAlias";
            master.RegisterNamespaceManager(AliasUri, serving);
            var candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            bool committing = false;
            bool checkedReentry = false;
            candidate.SetupGet(value => value.NamespaceUris).Returns(() =>
            {
                if (committing)
                {
                    InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
                    {
                        if (unregister)
                        {
                            master.UnregisterNamespaceManager(AliasUri, serving);
                        }
                        else
                        {
                            master.RegisterNamespaceManager(AliasUri, serving);
                        }
                    });
                    Assert.That(failure!.Message, Does.Contain("lifecycle publication callback"));
                    master.RegisterNamespaceManager(StagedAliasUri, candidate.Object);
                    Assert.That(master.UnregisterNamespaceManager(StagedAliasUri, candidate.Object), Is.True);
                    checkedReentry = true;
                }
                return new[] { kSecondModelNamespaceUri };
            });
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(candidate.Object));
            NodeManagerRegistration? added = null;
            try
            {
                await using IPreparedNodeManagerBatch prepared =
                    await ((INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                        [NodeManagerBatchChange.Add(factory)], null, timeout.Token).ConfigureAwait(false);
                committing = true;
                NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, timeout.Token)
                    .ConfigureAwait(false);
                added = result.Registrations[0];
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(checkedReentry, Is.True);
            }
            finally
            {
                committing = false;
                master.UnregisterNamespaceManager(AliasUri, serving);
                if (added is not null)
                {
                    await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, added, timeout.Token)
                        .ConfigureAwait(false);
                }
                await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, original, timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Public namespace registration waits for the durable decision instead of failing a reserved publication.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task NamespaceRegistrationWaitsForDurableDecisionAsync(
            bool namespaceAlreadyExists,
            bool unregister,
            bool decisionFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, value => manager = value),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            IServerInternal server = m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            const string AliasUri = kModelNamespaceUri + ":DecisionAlias";
            if (namespaceAlreadyExists)
            {
                server.NamespaceUris.GetIndexOrAppend(AliasUri);
                if (unregister)
                {
                    master.RegisterNamespaceManager(AliasUri, manager);
                }
            }

            var decisionFailure = new InvalidOperationException("The durable decision was rejected.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IPreparedNodeManagerBatch prepared =
                await ((INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    null,
                    timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (decisionFails)
                {
                    throw decisionFailure;
                }
            }, timeout.Token).AsTask();

            Task<InvalidOperationException?>? registration = null;
            bool unregistered = false;
            bool completedBeforeRelease = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                registration = RunWithoutExecutionContext(() =>
                {
                    started.TrySetResult(true);
                    try
                    {
                        if (unregister)
                        {
                            unregistered = master.UnregisterNamespaceManager(AliasUri, manager);
                        }
                        else
                        {
                            master.RegisterNamespaceManager(AliasUri, manager);
                        }
                        return Task.FromResult<InvalidOperationException?>(null);
                    }
                    catch (InvalidOperationException conflict)
                    {
                        return Task.FromResult<InvalidOperationException?>(conflict);
                    }
                });
                await started.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAny(registration, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token))
                    .ConfigureAwait(false);
                completedBeforeRelease = registration.IsCompleted;
                Assert.That(commit.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(decisionFails, Is.False);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                catch (InvalidOperationException decisionError) when (ReferenceEquals(decisionError, decisionFailure))
                {
                    Assert.That(decisionFails, Is.True);
                }
            }

            AssertLifecycleValue(registration);
            InvalidOperationException? failure = await registration.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                Assert.That(completedBeforeRelease, Is.False,
                    "Namespace registration must remain pending until the durable decision releases reservations.");
                Assert.That(failure, Is.Null, "Registration must not fail with a pending-publication exception.");
                int namespaceIndex = server.NamespaceUris.GetIndex(AliasUri);
                if (unregister)
                {
                    Assert.That(unregistered, Is.EqualTo(namespaceAlreadyExists));
                    Assert.That(namespaceIndex < 0 ||
                        !host.RoutingRevision.NamespaceManagers.TryGetValue(namespaceIndex, out var managers) ||
                        !managers.Contains(manager), Is.True);
                }
                else
                {
                    Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(host.RoutingRevision.NamespaceManagers[namespaceIndex], Does.Contain(manager));
                }
            }
            finally
            {
                if (!unregister && failure is null)
                {
                    Assert.That(master.UnregisterNamespaceManager(AliasUri, manager), Is.True);
                }
            }
        }

        /// <summary>
        /// Namespace mutation from a decision rejects its own reservation instead of blocking the decision.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceMutationFromDurableDecisionIsRejectedAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, value => manager = value),
                null,
                timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            const string AliasUri = kModelNamespaceUri + ":OwnDecisionAlias";
            await using IPreparedNodeManagerBatch prepared =
                await ((INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    null,
                    timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ =>
            {
                InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
                {
                    if (unregister)
                    {
                        master.UnregisterNamespaceManager(AliasUri, manager);
                    }
                    else
                    {
                        master.RegisterNamespaceManager(AliasUri, manager);
                    }
                });
                Assert.That(failure!.Message, Does.Contain("durable decision callback"));
                return default;
            }, timeout.Token).ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            master.RegisterNamespaceManager(AliasUri, manager);
            Assert.That(master.UnregisterNamespaceManager(AliasUri, manager), Is.True);
        }

        /// <summary>
        /// Node-management writes reject their own durable decision instead of waiting for that decision to finish.
        /// </summary>
        [TestCase(RequestType.AddNodes)]
        [TestCase(RequestType.DeleteNodes)]
        [TestCase(RequestType.AddReferences)]
        [TestCase(RequestType.DeleteReferences)]
        public async Task NodeManagementFromDurableDecisionIsRejectedAsync(RequestType requestType)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IMasterNodeManager master = m_server.CurrentInstance.NodeManager;
            using var context = new OperationContext(
                new RequestHeader(),
                null,
                requestType,
                RequestLifetime.None);
            await using IPreparedNodeManagerBatch prepared =
                await ((INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle).PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kGeneration2Value)))],
                    null,
                    timeout.Token).ConfigureAwait(false);

            NodeManagerBatchResult result = await prepared.CommitAsync(async _ =>
            {
                Task nodeManagement = requestType switch
                {
                    RequestType.AddNodes => master.AddNodesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.DeleteNodes => master.DeleteNodesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.AddReferences => master.AddReferencesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    RequestType.DeleteReferences => master.DeleteReferencesAsync(
                        context, [], CancellationToken.None).AsTask(),
                    _ => throw new AssertionException($"Unexpected request type {requestType}.")
                };
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await nodeManagement.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Message, Does.Contain("durable decision callback"));
            }, timeout.Token).ConfigureAwait(false);

            Assert.That(result.CleanupFailure, Is.Null);
        }
    }
}
