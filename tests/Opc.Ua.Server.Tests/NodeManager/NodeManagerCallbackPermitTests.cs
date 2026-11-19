/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies callback capabilities and their lifetime in captured execution contexts.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerCallbackPermitTests
    {
        /// <summary>
        /// Nested callbacks restore the earlier active permit after the inner callback ends.
        /// </summary>
        [Test]
        public async Task NestedPermitRestoresParentCapabilitiesAsync()
        {
            var current = new AsyncLocal<NodeManagerCallbackPermit?>();
            var parentScope = new LegacyNodeManagerCallbackScope(
                current, NodeManagerAdmissionCapability.ServerLifecycle);
            NodeManagerCallbackPermit parent = parentScope.Permit;
            try
            {
                var childScope = new LegacyNodeManagerCallbackScope(
                    current, NodeManagerAdmissionCapability.AddressSpaceMutation);
                NodeManagerCallbackPermit child = childScope.Permit;
                try
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(current.Value, Is.SameAs(child));
                        Assert.That(child.Previous, Is.SameAs(parent));
                        Assert.That(
                            child.Capabilities,
                            Is.EqualTo(NodeManagerAdmissionCapability.AddressSpaceMutation));
                        Assert.That(child.IsActive, Is.True);
                        Assert.That(parent.IsActive, Is.True);
                    }
                }
                finally
                {
                    await childScope.DisposeAsync().ConfigureAwait(false);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(current.Value, Is.SameAs(parent));
                    Assert.That(parent.Capabilities, Is.EqualTo(NodeManagerAdmissionCapability.ServerLifecycle));
                    Assert.That(parent.IsActive, Is.True);
                    Assert.That(child.IsActive, Is.False);
                }
            }
            finally
            {
                await parentScope.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(current.Value, Is.Null);
            Assert.That(parent.IsActive, Is.False);
        }

        /// <summary>
        /// An execution context captured by a callback cannot retain its authority after disposal.
        /// </summary>
        [Test]
        public async Task EscapedExecutionContextLosesPermitAuthorityAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            CancellationToken cancellationToken = timeout.Token;
            var current = new AsyncLocal<NodeManagerCallbackPermit?>();
            var scope = new LegacyNodeManagerCallbackScope(
                current,
                NodeManagerAdmissionCapability.ServerLifecycle | NodeManagerAdmissionCapability.AddressSpaceMutation);
            NodeManagerCallbackPermit permit = scope.Permit;
            var captured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<(NodeManagerCallbackPermit? Permit, bool ActiveBefore, bool ActiveAfter)> escaped =
                Task.Run(async () =>
                {
                    NodeManagerCallbackPermit? inheritedPermit = current.Value;
                    bool observedActive = inheritedPermit is { IsActive: true };
                    captured.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return (current.Value, observedActive, inheritedPermit is { IsActive: true });
                }, cancellationToken);
            try
            {
                await captured.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(current.Value, Is.SameAs(permit));
                await scope.DisposeAsync().ConfigureAwait(false);
                Assert.That(current.Value, Is.Null);
                Assert.That(permit.IsActive, Is.False);
            }
            finally
            {
                try
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult(true);
                }
            }
            (NodeManagerCallbackPermit? inherited, bool activeBefore, bool activeAfter) =
                await escaped.WaitAsync(cancellationToken).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(inherited, Is.SameAs(permit));
                Assert.That(activeBefore, Is.True);
                Assert.That(activeAfter, Is.False);
            }
        }

        /// <summary>
        /// A child can revoke a shared permit without changing the parent's execution-context slot.
        /// </summary>
        [Test]
        public async Task ChildContextDisposalRevokesSharedPermitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var current = new AsyncLocal<NodeManagerCallbackPermit?>();
            var scope = new LegacyNodeManagerCallbackScope(current, NodeManagerAdmissionCapability.ServerLifecycle);
            NodeManagerCallbackPermit permit = scope.Permit;
            try
            {
                var child = Task.Run(async () =>
                {
                    Assert.That(current.Value, Is.SameAs(permit));
                    await scope.DisposeAsync().ConfigureAwait(false);
                    Assert.That(current.Value, Is.Null);
                }, timeout.Token);
                await child.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(current.Value, Is.SameAs(permit));
                Assert.That(permit.IsActive, Is.False);
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(current.Value, Is.Null);
        }

        /// <summary>
        /// Disposing an outer permit cannot revoke a still-active inner callback or restore expired authority.
        /// </summary>
        [Test]
        public async Task OutOfOrderDisposalPreservesOnlyActivePermitAsync()
        {
            var current = new AsyncLocal<NodeManagerCallbackPermit?>();
            var parentScope = new LegacyNodeManagerCallbackScope(
                current, NodeManagerAdmissionCapability.ServerLifecycle);
            var childScope = new LegacyNodeManagerCallbackScope(
                current, NodeManagerAdmissionCapability.AddressSpaceMutation);
            NodeManagerCallbackPermit parent = parentScope.Permit;
            NodeManagerCallbackPermit child = childScope.Permit;
            try
            {
                await parentScope.DisposeAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(current.Value, Is.SameAs(child));
                    Assert.That(child.IsActive, Is.True);
                    Assert.That(parent.IsActive, Is.False);
                }
                await childScope.DisposeAsync().ConfigureAwait(false);
                Assert.That(current.Value?.IsActive, Is.Not.True);
            }
            finally
            {
                await childScope.DisposeAsync().ConfigureAwait(false);
                await parentScope.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(current.Value?.IsActive, Is.Not.True);
        }

        /// <summary>
        /// Closing a callback revokes new borrows but drains all capabilities already admitted.
        /// </summary>
        [Test]
        public async Task ClosingPermitDrainsPreviouslyBorrowedCapabilitiesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var permit = new NodeManagerCallbackPermit(
                NodeManagerAdmissionCapability.ServerLifecycle | NodeManagerAdmissionCapability.AddressSpaceMutation);
            NodeManagerCallbackPermit.Borrow? borrowed = null;
            NodeManagerCallbackPermit.Borrow? late = null;
            try
            {
                borrowed = permit.TryBorrow(
                    NodeManagerAdmissionCapability.AddressSpaceMutation |
                    NodeManagerAdmissionCapability.ServerLifecycle);
                Assert.That(borrowed, Is.Not.Null);

                ValueTask closing = permit.DisposeAsync();
                Assert.That(permit.IsActive, Is.False);
                Assert.That(closing.IsCompleted, Is.False);
                late = permit.TryBorrow(NodeManagerAdmissionCapability.AddressSpaceMutation);
                Assert.That(late, Is.Null);

                borrowed!.Dispose();
                borrowed.Dispose();
                await closing.AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(permit.IsActive, Is.False);
            }
            finally
            {
                borrowed?.Dispose();
                late?.Dispose();
                await permit.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sibling operations take turns borrowing the callback's complete admission.
        /// </summary>
        [Test]
        public async Task SiblingBorrowsAreSerializedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var permit = new NodeManagerCallbackPermit(
                NodeManagerAdmissionCapability.ServerLifecycle | NodeManagerAdmissionCapability.AddressSpaceMutation);
            NodeManagerCallbackPermit.Borrow? first = null;
            Task<NodeManagerCallbackPermit.Borrow?>? pending = null;
            try
            {
                first = permit.TryBorrow(NodeManagerAdmissionCapability.AddressSpaceMutation);
                Assert.That(first, Is.Not.Null);
                Assert.That(first!.Capabilities, Is.EqualTo(permit.Capabilities));
                pending = permit.BorrowAsync(
                    NodeManagerAdmissionCapability.ServerLifecycle, timeout.Token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);

                first.Dispose();
                first = null;
                using NodeManagerCallbackPermit.Borrow? second =
                    await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                pending = null;
                Assert.That(second, Is.Not.Null);
                Assert.That(second!.Capabilities, Is.EqualTo(permit.Capabilities));
            }
            finally
            {
                await DisposePendingBorrowAsync(first, pending).ConfigureAwait(false);
                await permit.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A queued sibling cannot enter after the callback revokes its admission.
        /// </summary>
        [Test]
        public async Task ClosingPermitRejectsQueuedBorrowAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var permit = new NodeManagerCallbackPermit(NodeManagerAdmissionCapability.AddressSpaceMutation);
            NodeManagerCallbackPermit.Borrow? first = null;
            Task<NodeManagerCallbackPermit.Borrow?>? pending = null;
            try
            {
                first = permit.TryBorrow(NodeManagerAdmissionCapability.AddressSpaceMutation);
                Assert.That(first, Is.Not.Null);
                pending = permit.BorrowAsync(
                    NodeManagerAdmissionCapability.AddressSpaceMutation, timeout.Token).AsTask();
                Assert.That(pending.IsCompleted, Is.False);
                ValueTask closing = permit.DisposeAsync();
                Assert.That(closing.IsCompleted, Is.False);

                first!.Dispose();
                first = null;
                await closing.AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                using NodeManagerCallbackPermit.Borrow? rejected =
                    await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                pending = null;
                Assert.That(rejected, Is.Null);
            }
            finally
            {
                await DisposePendingBorrowAsync(first, pending).ConfigureAwait(false);
                await permit.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases borrows left by an interrupted assertion before closing their callback permit.
        /// </summary>
        private static async Task DisposePendingBorrowAsync(
            NodeManagerCallbackPermit.Borrow? first,
            Task<NodeManagerCallbackPermit.Borrow?>? pending)
        {
            first?.Dispose();
            if (pending is not null)
            {
                using NodeManagerCallbackPermit.Borrow? remaining =
                    await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
    }
}
