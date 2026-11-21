// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies exact readiness authority and release of captured initializer ownership.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerReadinessScopeTests
    {
        /// <summary>
        /// Readiness grants access only to the exact server and generation, not a shared slot.
        /// </summary>
        [Test]
        public void ActiveScopeAllowsOnlyItsExactServerAndGeneration()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration generation = CreateGeneration();
            var replacement = new NodeManagerGeneration(
                generation.Slot, 2, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Prepared);
            using var scope = new NodeManagerReadinessScope(server, generation);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(NodeManagerReadinessScope.Allows(server), Is.True);
                Assert.That(NodeManagerReadinessScope.Allows(generation), Is.True);
                Assert.That(NodeManagerReadinessScope.Allows(Mock.Of<IServerInternal>()), Is.False);
                Assert.That(NodeManagerReadinessScope.Allows(replacement), Is.False);
            }
            scope.Dispose();
            Assert.That(NodeManagerReadinessScope.Allows(server), Is.False);
            Assert.That(NodeManagerReadinessScope.Allows(generation), Is.False);
        }

        /// <summary>
        /// A nested initializer restores its parent without granting its completed generation.
        /// </summary>
        [Test]
        public void NestedScopeRestoresParentAuthority()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration parent = CreateGeneration();
            NodeManagerGeneration child = CreateGeneration();
            using var outer = new NodeManagerReadinessScope(server, parent);
            using (var inner = new NodeManagerReadinessScope(server, child))
            {
                Assert.That(NodeManagerReadinessScope.Allows(parent), Is.True);
                Assert.That(NodeManagerReadinessScope.Allows(child), Is.True);
            }
            Assert.That(NodeManagerReadinessScope.Allows(parent), Is.True);
            Assert.That(NodeManagerReadinessScope.Allows(child), Is.False);
        }

        /// <summary>
        /// An escaped callback loses its authority when the originating initializer completes.
        /// </summary>
        [Test]
        public async Task DisposedScopeRevokesEscapedExecutionContextAsync()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration generation = CreateGeneration();
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<(bool Server, bool Generation)> escaped;
            using (var scope = new NodeManagerReadinessScope(server, generation))
            {
                escaped = Task.Run(async () =>
                {
                    await released.Task.ConfigureAwait(false);
                    return (NodeManagerReadinessScope.Allows(server), NodeManagerReadinessScope.Allows(generation));
                });
            }
            released.SetResult(true);
            (bool serverAllowed, bool generationAllowed) = await escaped.ConfigureAwait(false);
            Assert.That(serverAllowed, Is.False);
            Assert.That(generationAllowed, Is.False);
        }

        /// <summary>
        /// Repeated disposal cannot replace a later initializer's execution context.
        /// </summary>
        [Test]
        public void RepeatedDisposalDoesNotRemoveAnotherActiveScope()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            var completed = new NodeManagerReadinessScope(server, CreateGeneration());
            completed.Dispose();
            NodeManagerGeneration current = CreateGeneration();
            using var active = new NodeManagerReadinessScope(server, current);
            completed.Dispose();
            Assert.That(NodeManagerReadinessScope.Allows(current), Is.True);
            Assert.That(NodeManagerReadinessScope.Allows(server), Is.True);
        }

        /// <summary>
        /// A retained execution context cannot keep a completed initializer's server or manager alive.
        /// </summary>
        [Test]
        public void CapturedDisposedScopeDoesNotRetainServerOrGeneration()
        {
            (ExecutionContext Context, WeakReference Server, WeakReference Generation, WeakReference Manager) capture =
                CaptureDisposedScope();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(capture.Server.IsAlive, Is.False);
                Assert.That(capture.Generation.IsAlive, Is.False);
                Assert.That(capture.Manager.IsAlive, Is.False);
            }
            GC.KeepAlive(capture.Context);
        }

        /// <summary>
        /// Captures the execution context outside the collection test's stack roots.
        /// </summary>
        /// <returns>The retained context and weak references to its completed owners.</returns>
        /// <exception cref="InvalidOperationException">The initializer execution context was not captured.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext Context,
            WeakReference Server,
            WeakReference Generation,
            WeakReference Manager) CaptureDisposedScope()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration generation = CreateGeneration();
            ExecutionContext context;
            using (var scope = new NodeManagerReadinessScope(server, generation))
            {
                context = ExecutionContext.Capture() ??
                    throw new InvalidOperationException("The initializer execution context was not captured.");
            }
            return (context, new WeakReference(server), new WeakReference(generation),
                new WeakReference(generation.NodeManager));
        }

        /// <summary>
        /// Creates an isolated generation without registering it in a serving routing table.
        /// </summary>
        /// <returns>The generation owned by the test initializer.</returns>
        private static NodeManagerGeneration CreateGeneration()
        {
            return new NodeManagerGeneration(
                new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Prepared);
        }
    }
}
