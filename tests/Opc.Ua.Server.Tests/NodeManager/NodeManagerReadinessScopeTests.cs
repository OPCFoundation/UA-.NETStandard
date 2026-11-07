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
 *
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
        /// Disposing an outer initializer leaves the nested initializer's authority active.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ParentScopeDisposalDoesNotRevokeActiveNestedScope(bool sharedServer)
        {
            ExecutionContext context = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The initializer execution context was not captured.");
            ExecutionContext.Run(context.CreateCopy(), _ =>
            {
                IServerInternal parentServer = Mock.Of<IServerInternal>();
                IServerInternal childServer = sharedServer ? parentServer : Mock.Of<IServerInternal>();
                NodeManagerGeneration parent = CreateGeneration();
                NodeManagerGeneration child = CreateGeneration();
                using var outer = new NodeManagerReadinessScope(parentServer, parent);
                using var inner = new NodeManagerReadinessScope(childServer, child);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(NodeManagerReadinessScope.Allows(parentServer), Is.True);
                    Assert.That(NodeManagerReadinessScope.Allows(parent), Is.True);
                    Assert.That(NodeManagerReadinessScope.Allows(childServer), Is.True);
                    Assert.That(NodeManagerReadinessScope.Allows(child), Is.True);
                }

                outer.Dispose();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(NodeManagerReadinessScope.Allows(parentServer), Is.EqualTo(sharedServer));
                    Assert.That(NodeManagerReadinessScope.Allows(parent), Is.False);
                    Assert.That(NodeManagerReadinessScope.Allows(childServer), Is.True);
                    Assert.That(NodeManagerReadinessScope.Allows(child), Is.True);
                }

                inner.Dispose();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(NodeManagerReadinessScope.Allows(parentServer), Is.False);
                    Assert.That(NodeManagerReadinessScope.Allows(parent), Is.False);
                    Assert.That(NodeManagerReadinessScope.Allows(childServer), Is.False);
                    Assert.That(NodeManagerReadinessScope.Allows(child), Is.False);
                }
            }, null);
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
        /// Capturing nested scopes does not retain either initializer after both scopes are disposed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CapturedDisposedNestedScopesDoNotRetainEitherInitializer(bool parentFirst)
        {
            (ExecutionContext Context, WeakReference[] Owners, NodeManagerReadinessScope[] Scopes) capture =
                CaptureDisposedNestedScopes(parentFirst);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(capture.Owners[0].IsAlive, Is.False, "Parent server");
                Assert.That(capture.Owners[1].IsAlive, Is.False, "Parent generation");
                Assert.That(capture.Owners[2].IsAlive, Is.False, "Parent manager");
                Assert.That(capture.Owners[3].IsAlive, Is.False, "Child server");
                Assert.That(capture.Owners[4].IsAlive, Is.False, "Child generation");
                Assert.That(capture.Owners[5].IsAlive, Is.False, "Child manager");
            }
            GC.KeepAlive(capture.Context);
            GC.KeepAlive(capture.Scopes);
        }

        /// <summary>
        /// A disposed initializer releases its owners while the other nested initializer remains active.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DisposedNestedScopeReleasesOwnersWhileOtherScopeRemainsActive(bool disposeParent)
        {
            (
                ExecutionContext NestedContext,
                ExecutionContext ActiveContext,
                WeakReference[] Owners,
                NodeManagerReadinessScope[] Scopes) capture = CapturePartlyDisposedNestedScopes(disposeParent);
            int disposed = disposeParent ? 0 : 3;
            int active = disposeParent ? 3 : 0;
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(capture.Owners[disposed].IsAlive, Is.False, "Disposed server");
                    Assert.That(capture.Owners[disposed + 1].IsAlive, Is.False, "Disposed generation");
                    Assert.That(capture.Owners[disposed + 2].IsAlive, Is.False, "Disposed manager");
                    Assert.That(capture.Owners[active].IsAlive, Is.True, "Active server");
                    Assert.That(capture.Owners[active + 1].IsAlive, Is.True, "Active generation");
                    Assert.That(capture.Owners[active + 2].IsAlive, Is.True, "Active manager");
                }
                if (capture.Owners[active].Target is IServerInternal activeServer &&
                    capture.Owners[active + 1].Target is NodeManagerGeneration activeGeneration)
                {
                    ExecutionContext.Run(capture.ActiveContext.CreateCopy(), _ =>
                    {
                        Assert.That(NodeManagerReadinessScope.Allows(activeServer), Is.True);
                        Assert.That(NodeManagerReadinessScope.Allows(activeGeneration), Is.True);
                    }, null);
                }
                else
                {
                    Assert.Fail("The active initializer no longer owns its server or generation.");
                }
            }
            finally
            {
                ExecutionContext.Run(capture.ActiveContext.CreateCopy(), _ =>
                    capture.Scopes[disposeParent ? 1 : 0].Dispose(), null);
                GC.KeepAlive(capture.NestedContext);
                GC.KeepAlive(capture.ActiveContext);
                GC.KeepAlive(capture.Scopes);
            }
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
        /// Captures both initializers outside the collection test's stack roots.
        /// </summary>
        /// <param name="parentFirst">Whether the parent is disposed before the child.</param>
        /// <returns>The retained context, scope handles and weak references to both initializers' owners.</returns>
        /// <exception cref="InvalidOperationException">An execution context was not captured.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext Context,
            WeakReference[] Owners,
            NodeManagerReadinessScope[] Scopes) CaptureDisposedNestedScopes(bool parentFirst)
        {
            ExecutionContext caller = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The caller execution context was not captured.");
            (ExecutionContext Context, WeakReference[] Owners, NodeManagerReadinessScope[] Scopes)? capture = null;
            ExecutionContext.Run(caller.CreateCopy(), state =>
            {
                IServerInternal parentServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration parentGeneration = CreateGeneration();
                IServerInternal childServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration childGeneration = CreateGeneration();
                using var parent = new NodeManagerReadinessScope(parentServer, parentGeneration);
                using var child = new NodeManagerReadinessScope(childServer, childGeneration);
                ExecutionContext context = ExecutionContext.Capture() ??
                    throw new InvalidOperationException("The initializer execution context was not captured.");
                ExecutionContext.Run(context.CreateCopy(), _ => AssertAuthority(true), null);
                WeakReference[] owners = CreateOwnerReferences(
                    parentServer, parentGeneration, childServer, childGeneration);
                NodeManagerReadinessScope[] scopes = [parent, child];
                capture = (context, owners, scopes);
                if (parentFirst)
                {
                    parent.Dispose();
                    child.Dispose();
                }
                else
                {
                    child.Dispose();
                    parent.Dispose();
                }
                ExecutionContext.Run(context.CreateCopy(), _ => AssertAuthority(false), null);

                void AssertAuthority(bool active)
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(NodeManagerReadinessScope.Allows(parentServer), Is.EqualTo(active));
                        Assert.That(NodeManagerReadinessScope.Allows(parentGeneration), Is.EqualTo(active));
                        Assert.That(NodeManagerReadinessScope.Allows(childServer), Is.EqualTo(active));
                        Assert.That(NodeManagerReadinessScope.Allows(childGeneration), Is.EqualTo(active));
                    }
                }
            }, null);
            return capture ??
                throw new InvalidOperationException("The initializer execution context was not captured.");
        }

        /// <summary>
        /// Captures nested contexts after one initializer completes and the other remains active.
        /// </summary>
        /// <param name="disposeParent">Whether the parent, rather than the child, completes.</param>
        /// <returns>The contexts, scope handles and weak references to both initializers' owners.</returns>
        /// <exception cref="InvalidOperationException">An execution context was not captured.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext NestedContext,
            ExecutionContext ActiveContext,
            WeakReference[] Owners,
            NodeManagerReadinessScope[] Scopes) CapturePartlyDisposedNestedScopes(bool disposeParent)
        {
            ExecutionContext caller = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The caller execution context was not captured.");
            (
                ExecutionContext NestedContext,
                ExecutionContext ActiveContext,
                WeakReference[] Owners,
                NodeManagerReadinessScope[] Scopes)? capture = null;
            ExecutionContext.Run(caller.CreateCopy(), _ =>
            {
                IServerInternal parentServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration parentGeneration = CreateGeneration();
                IServerInternal childServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration childGeneration = CreateGeneration();
                var parent = new NodeManagerReadinessScope(parentServer, parentGeneration);
                var child = new NodeManagerReadinessScope(childServer, childGeneration);
                bool captured = false;
                try
                {
                    ExecutionContext nested = ExecutionContext.Capture() ??
                        throw new InvalidOperationException("The nested execution context was not captured.");
                    if (disposeParent)
                    {
                        parent.Dispose();
                    }
                    else
                    {
                        child.Dispose();
                    }
                    ExecutionContext active = ExecutionContext.Capture() ??
                        throw new InvalidOperationException("The active execution context was not captured.");
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(NodeManagerReadinessScope.Allows(parentServer), Is.EqualTo(!disposeParent));
                        Assert.That(NodeManagerReadinessScope.Allows(parentGeneration), Is.EqualTo(!disposeParent));
                        Assert.That(NodeManagerReadinessScope.Allows(childServer), Is.EqualTo(disposeParent));
                        Assert.That(NodeManagerReadinessScope.Allows(childGeneration), Is.EqualTo(disposeParent));
                    }
                    WeakReference[] owners = CreateOwnerReferences(
                        parentServer, parentGeneration, childServer, childGeneration);
                    NodeManagerReadinessScope[] scopes = [parent, child];
                    capture = (nested, active, owners, scopes);
                    captured = true;
                }
                finally
                {
                    if (!captured)
                    {
                        child.Dispose();
                        parent.Dispose();
                    }
                }
            }, null);
            return capture ??
                throw new InvalidOperationException("The initializer execution context was not captured.");
        }

        /// <summary>
        /// Creates weak references to the owners of both initializers.
        /// </summary>
        /// <param name="parentServer">The parent initializer's server.</param>
        /// <param name="parentGeneration">The parent initializer's generation.</param>
        /// <param name="childServer">The child initializer's server.</param>
        /// <param name="childGeneration">The child initializer's generation.</param>
        /// <returns>The server, generation and manager references for each initializer.</returns>
        private static WeakReference[] CreateOwnerReferences(
            IServerInternal parentServer,
            NodeManagerGeneration parentGeneration,
            IServerInternal childServer,
            NodeManagerGeneration childGeneration)
        {
            return
            [
                new WeakReference(parentServer),
                new WeakReference(parentGeneration),
                new WeakReference(parentGeneration.NodeManager),
                new WeakReference(childServer),
                new WeakReference(childGeneration),
                new WeakReference(childGeneration.NodeManager)
            ];
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
