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
            var parent = new NodeManagerCallbackPermit(current, NodeManagerAdmissionCapability.ServerLifecycle);
            try
            {
                var child = new NodeManagerCallbackPermit(current, NodeManagerAdmissionCapability.AddressSpaceMutation);
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
                    await child.DisposeAsync().ConfigureAwait(false);
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
                await parent.DisposeAsync().ConfigureAwait(false);
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
            var permit = new NodeManagerCallbackPermit(
                current,
                NodeManagerAdmissionCapability.ServerLifecycle | NodeManagerAdmissionCapability.AddressSpaceMutation);
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
                await permit.DisposeAsync().ConfigureAwait(false);
                Assert.That(current.Value, Is.Null);
                Assert.That(permit.IsActive, Is.False);
            }
            finally
            {
                try
                {
                    await permit.DisposeAsync().ConfigureAwait(false);
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
            var permit = new NodeManagerCallbackPermit(current, NodeManagerAdmissionCapability.ServerLifecycle);
            try
            {
                Task child = Task.Run(async () =>
                {
                    Assert.That(current.Value, Is.SameAs(permit));
                    await permit.DisposeAsync().ConfigureAwait(false);
                    Assert.That(current.Value, Is.Null);
                }, timeout.Token);
                await child.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(current.Value, Is.SameAs(permit));
                Assert.That(permit.IsActive, Is.False);
            }
            finally
            {
                await permit.DisposeAsync().ConfigureAwait(false);
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
            var parent = new NodeManagerCallbackPermit(current, NodeManagerAdmissionCapability.ServerLifecycle);
            var child = new NodeManagerCallbackPermit(current, NodeManagerAdmissionCapability.AddressSpaceMutation);
            try
            {
                await parent.DisposeAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(current.Value, Is.SameAs(child));
                    Assert.That(child.IsActive, Is.True);
                    Assert.That(parent.IsActive, Is.False);
                }
                await child.DisposeAsync().ConfigureAwait(false);
                Assert.That(current.Value is { IsActive: true }, Is.False);
            }
            finally
            {
                await child.DisposeAsync().ConfigureAwait(false);
                await parent.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(current.Value is { IsActive: true }, Is.False);
        }
    }
}
