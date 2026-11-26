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
        /// A custom lifecycle that manages its own readiness can capture publication at startup.
        /// </summary>
        [Test]
        public void ReadinessCaptureSupportsPrimaryOnlyLifecycle()
        {
            using var scope = new NodeManagerReadinessScope(Mock.Of<IServerInternal>(), CreateGeneration());
            var readiness = new NodeManagerReadinessContext(scope);
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            ILifecycleCapture capture = Mock.Of<ILifecycleCapture>();
            lifecycle.Setup(owner => owner.CaptureChanges()).Returns(capture);

            Assert.That(readiness.CaptureChanges(lifecycle.Object), Is.SameAs(capture));
            lifecycle.Verify(owner => owner.CaptureChanges(), Times.Once);
        }

        /// <summary>
        /// Readiness-aware providers receive the exact live context, never a context-free retry.
        /// </summary>
        [Test]
        public void ReadinessCaptureForwardsOnlyAnActiveContext()
        {
            using var scope = new NodeManagerReadinessScope(Mock.Of<IServerInternal>(), CreateGeneration());
            var readiness = new NodeManagerReadinessContext(scope);
            var lifecycle = new Mock<INodeManagerCallbackLifecycle>(MockBehavior.Strict);
            ILifecycleCapture capture = Mock.Of<ILifecycleCapture>();
            lifecycle.Setup(owner => owner.CaptureFromReadiness(readiness)).Returns(capture);

            Assert.That(readiness.CaptureChanges(lifecycle.Object), Is.SameAs(capture));
            scope.Dispose();
            Assert.That(() => readiness.CaptureChanges(lifecycle.Object), Throws.InvalidOperationException);
            lifecycle.Verify(owner => owner.CaptureFromReadiness(readiness), Times.Once);
        }

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
                Assert.That(scope.Includes(server), Is.True);
                Assert.That(scope.Includes(generation), Is.True);
                Assert.That(scope.Includes(Mock.Of<IServerInternal>()), Is.False);
                Assert.That(scope.Includes(replacement), Is.False);
            }
            scope.Dispose();
            Assert.That(scope.Includes(server), Is.False);
            Assert.That(scope.Includes(generation), Is.False);
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
            using (var inner = new NodeManagerReadinessScope(server, child, outer))
            {
                Assert.That(inner.Includes(parent), Is.True);
                Assert.That(inner.Includes(child), Is.True);
            }
            Assert.That(outer.Includes(parent), Is.True);
            Assert.That(outer.Includes(child), Is.False);
        }

        [Test]
        public void ExplicitChildCannotReachGrandparentThroughRevokedMiddleScope()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration original = CreateGeneration();
            NodeManagerGeneration pending = CreateGeneration();
            using var grandparent = new NodeManagerReadinessScope(server, original);
            using var middle = new NodeManagerReadinessScope(server, CreateGeneration(), grandparent);
            using var child = new NodeManagerReadinessScope(server, pending, middle);

            Assert.That(child.Includes(original), Is.True);
            middle.Dispose();

            Assert.That(child.Includes(original), Is.False);
            Assert.That(child.Includes(pending), Is.True);
            Assert.That(grandparent.Includes(original), Is.True);
        }

        /// <summary>
        /// Disposing the middle scope severs a grandchild from its active grandparent.
        /// </summary>
        /// <exception cref="InvalidOperationException">The caller execution context could not be captured.</exception>
        [Test]
        public void DisposedMiddleScopeSeversGrandchildAuthorityFromGrandparent()
        {
            ExecutionContext caller = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The caller execution context was not captured.");
            ExecutionContext.Run(caller.CreateCopy(), _ =>
            {
                IServerInternal grandparentServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration grandparentGeneration = CreateGeneration();
                IServerInternal middleServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration middleGeneration = CreateGeneration();
                IServerInternal grandchildServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration grandchildGeneration = CreateGeneration();
                using var grandparent = new NodeManagerReadinessScope(grandparentServer, grandparentGeneration);
                using var middle = new NodeManagerReadinessScope(middleServer, middleGeneration, grandparent);
                using var grandchild = new NodeManagerReadinessScope(grandchildServer, grandchildGeneration, middle);

                AssertNestedAuthority(
                    grandchild,
                    grandparentServer,
                    grandparentGeneration,
                    middleServer,
                    middleGeneration,
                    grandchildServer,
                    grandchildGeneration,
                    grandparent: true,
                    middle: true,
                    grandchild: true);

                middle.Dispose();
                AssertNestedAuthority(
                    grandchild,
                    grandparentServer,
                    grandparentGeneration,
                    middleServer,
                    middleGeneration,
                    grandchildServer,
                    grandchildGeneration,
                    grandparent: false,
                    middle: false,
                    grandchild: true);

                grandchild.Dispose();
                AssertNestedAuthority(
                    grandchild,
                    grandparentServer,
                    grandparentGeneration,
                    middleServer,
                    middleGeneration,
                    grandchildServer,
                    grandchildGeneration,
                    grandparent: false,
                    middle: false,
                    grandchild: false);
            }, null);
        }

        /// <summary>
        /// A captured grandchild context does not retain a disposed middle scope's parent.
        /// </summary>
        [Test]
        public void CapturedGrandchildDoesNotRetainGrandparentAcrossDisposedMiddleScope()
        {
            (
                ExecutionContext Context,
                WeakReference[] Owners,
                WeakReference ParentScope,
                NodeManagerReadinessScope[] Scopes) capture = CaptureGrandchildAcrossDisposedMiddleScope();
            try
            {
                CollectReadinessOwners();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(capture.ParentScope.IsAlive, Is.False, "Grandparent scope");
                    Assert.That(capture.Owners[0].IsAlive, Is.False, "Grandparent server");
                    Assert.That(capture.Owners[1].IsAlive, Is.False, "Grandparent generation");
                    Assert.That(capture.Owners[2].IsAlive, Is.False, "Grandparent manager");
                    Assert.That(capture.Owners[3].IsAlive, Is.False, "Disposed middle server");
                    Assert.That(capture.Owners[4].IsAlive, Is.False, "Disposed middle generation");
                    Assert.That(capture.Owners[5].IsAlive, Is.False, "Disposed middle manager");
                    Assert.That(capture.Owners[6].IsAlive, Is.True, "Active grandchild server");
                    Assert.That(capture.Owners[7].IsAlive, Is.True, "Active grandchild generation");
                    Assert.That(capture.Owners[8].IsAlive, Is.True, "Active grandchild manager");
                }

                if (capture.Owners[6].Target is IServerInternal server &&
                    capture.Owners[7].Target is NodeManagerGeneration generation)
                {
                    ExecutionContext.Run(capture.Context.CreateCopy(), _ =>
                    {
                        Assert.That(capture.Scopes[1].Includes(server), Is.True);
                        Assert.That(capture.Scopes[1].Includes(generation), Is.True);
                    }, null);
                }
                else
                {
                    Assert.Fail("The active grandchild no longer owns its server or generation.");
                }
            }
            finally
            {
                capture.Scopes[1].Dispose();
                GC.KeepAlive(capture.Context);
                GC.KeepAlive(capture.Scopes);
            }
        }

        /// <summary>
        /// Disposing an outer initializer leaves the nested initializer's authority active.
        /// </summary>
        /// <exception cref="InvalidOperationException">The initializer execution context could not be captured.</exception>
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
                using var inner = new NodeManagerReadinessScope(childServer, child, outer);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(inner.Includes(parentServer), Is.True);
                    Assert.That(inner.Includes(parent), Is.True);
                    Assert.That(inner.Includes(childServer), Is.True);
                    Assert.That(inner.Includes(child), Is.True);
                }

                outer.Dispose();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(inner.Includes(parentServer), Is.EqualTo(sharedServer));
                    Assert.That(inner.Includes(parent), Is.False);
                    Assert.That(inner.Includes(childServer), Is.True);
                    Assert.That(inner.Includes(child), Is.True);
                }

                inner.Dispose();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(inner.Includes(parentServer), Is.False);
                    Assert.That(inner.Includes(parent), Is.False);
                    Assert.That(inner.Includes(childServer), Is.False);
                    Assert.That(inner.Includes(child), Is.False);
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
                    return (scope.Includes(server), scope.Includes(generation));
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
            Assert.That(active.Includes(current), Is.True);
            Assert.That(active.Includes(server), Is.True);
        }

        /// <summary>
        /// A retained execution context cannot keep a completed initializer's server or manager alive.
        /// </summary>
        [Test]
        public void CapturedDisposedScopeDoesNotRetainServerOrGeneration()
        {
            (NodeManagerReadinessScope Context, WeakReference Server, WeakReference Generation,
                WeakReference Manager) capture =
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
                        NodeManagerReadinessScope activeScope = capture.Scopes[disposeParent ? 1 : 0];
                        Assert.That(activeScope.Includes(activeServer), Is.True);
                        Assert.That(activeScope.Includes(activeGeneration), Is.True);
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
            NodeManagerReadinessScope Context,
            WeakReference Server,
            WeakReference Generation,
            WeakReference Manager) CaptureDisposedScope()
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            NodeManagerGeneration generation = CreateGeneration();
            using var scope = new NodeManagerReadinessScope(server, generation);
            return (scope, new WeakReference(server), new WeakReference(generation),
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
                using var child = new NodeManagerReadinessScope(childServer, childGeneration, parent);
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
                        Assert.That(child.Includes(parentServer), Is.EqualTo(active));
                        Assert.That(child.Includes(parentGeneration), Is.EqualTo(active));
                        Assert.That(child.Includes(childServer), Is.EqualTo(active));
                        Assert.That(child.Includes(childGeneration), Is.EqualTo(active));
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
                var child = new NodeManagerReadinessScope(childServer, childGeneration, parent);
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
                        NodeManagerReadinessScope surviving = disposeParent ? child : parent;
                        Assert.That(surviving.Includes(parentServer), Is.EqualTo(!disposeParent));
                        Assert.That(surviving.Includes(parentGeneration), Is.EqualTo(!disposeParent));
                        Assert.That(surviving.Includes(childServer), Is.EqualTo(disposeParent));
                        Assert.That(surviving.Includes(childGeneration), Is.EqualTo(disposeParent));
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
        /// Captures a grandchild context after disposing its middle scope and parent.
        /// </summary>
        /// <returns>The retained context, weak owner references and active scope handles.</returns>
        /// <exception cref="InvalidOperationException">An execution context was not captured.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext Context,
            WeakReference[] Owners,
            WeakReference ParentScope,
            NodeManagerReadinessScope[] Scopes) CaptureGrandchildAcrossDisposedMiddleScope()
        {
            ExecutionContext caller = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The caller execution context was not captured.");
            (
                ExecutionContext Context,
                WeakReference[] Owners,
                WeakReference ParentScope,
                NodeManagerReadinessScope[] Scopes)? capture = null;
            ExecutionContext.Run(caller.CreateCopy(), _ =>
            {
                IServerInternal grandparentServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration grandparentGeneration = CreateGeneration();
                IServerInternal middleServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration middleGeneration = CreateGeneration();
                IServerInternal grandchildServer = Mock.Of<IServerInternal>();
                NodeManagerGeneration grandchildGeneration = CreateGeneration();
                var grandparent = new NodeManagerReadinessScope(grandparentServer, grandparentGeneration);
                var middle = new NodeManagerReadinessScope(middleServer, middleGeneration, grandparent);
                var grandchild = new NodeManagerReadinessScope(grandchildServer, grandchildGeneration, middle);
                ExecutionContext context = ExecutionContext.Capture() ??
                    throw new InvalidOperationException("The grandchild execution context was not captured.");
                WeakReference[] owners =
                [
                    new WeakReference(grandparentServer),
                    new WeakReference(grandparentGeneration),
                    new WeakReference(grandparentGeneration.NodeManager),
                    new WeakReference(middleServer),
                    new WeakReference(middleGeneration),
                    new WeakReference(middleGeneration.NodeManager),
                    new WeakReference(grandchildServer),
                    new WeakReference(grandchildGeneration),
                    new WeakReference(grandchildGeneration.NodeManager)
                ];
                var parentScope = new WeakReference(grandparent);
                NodeManagerReadinessScope[] scopes = [middle, grandchild];
                middle.Dispose();
                grandparent.Dispose();
                capture = (context, owners, parentScope, scopes);
            }, null);
            return capture ??
                throw new InvalidOperationException("The grandchild execution context was not captured.");
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
        /// Collects owners after the scope-capture helper has returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectReadinessOwners()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// Checks the authority of each exact server and generation in a nested scope chain.
        /// </summary>
        /// <param name="grandparentServer">The grandparent initializer's server.</param>
        /// <param name="grandparentGeneration">The grandparent initializer's generation.</param>
        /// <param name="middleServer">The middle initializer's server.</param>
        /// <param name="middleGeneration">The middle initializer's generation.</param>
        /// <param name="grandchildServer">The grandchild initializer's server.</param>
        /// <param name="grandchildGeneration">The grandchild initializer's generation.</param>
        /// <param name="grandparent">Whether grandparent authority is available.</param>
        /// <param name="middle">Whether middle-scope authority is available.</param>
        /// <param name="grandchild">Whether grandchild authority is available.</param>
        private static void AssertNestedAuthority(
            NodeManagerReadinessScope scope,
            IServerInternal grandparentServer,
            NodeManagerGeneration grandparentGeneration,
            IServerInternal middleServer,
            NodeManagerGeneration middleGeneration,
            IServerInternal grandchildServer,
            NodeManagerGeneration grandchildGeneration,
            bool grandparent,
            bool middle,
            bool grandchild)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(scope.Includes(grandparentServer), Is.EqualTo(grandparent));
                Assert.That(scope.Includes(grandparentGeneration), Is.EqualTo(grandparent));
                Assert.That(scope.Includes(middleServer), Is.EqualTo(middle));
                Assert.That(scope.Includes(middleGeneration), Is.EqualTo(middle));
                Assert.That(scope.Includes(grandchildServer), Is.EqualTo(grandchild));
                Assert.That(scope.Includes(grandchildGeneration), Is.EqualTo(grandchild));
            }
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
