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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises public per-root cancellation, registration rollback and sibling isolation.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSystemLifetimeTests
    {
        /// <summary>
        /// Direct shared-owner deletion cancels and joins a pending native acquisition, without stopping siblings.
        /// </summary>
        [Test]
        public async Task ActualRootDeletionDrainsProviderAndPreservesSiblingAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var provider = new BlockingProvider();
            await using GmsHandle first = await GmsSystemTests.Builder(context, "DrainedGms", provider)
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            await using GmsHandle sibling = await GmsSystemTests.Builder(context, "SiblingGms",
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Task pending = first.RefreshAsync().AsTask();
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, first.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(() => pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(provider.Finished.Task.IsCompleted, Is.True);
            Assert.That(fixture.Manager.FindPredefinedNode(first.NodeId), Is.Null);
            Assert.That(first.ConformanceUnits, Is.Empty);
            Assert.That(fixture.Manager.FindPredefinedNode(sibling.NodeId), Is.SameAs(sibling.State));
            await sibling.RefreshAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await first.RefreshAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// A provider cannot await its own composite acquisition or deletion.
        /// </summary>
        [Test]
        public async Task ProviderReentryFailsPromptlyWithoutDisposingItsOwnRootAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var provider = new CallbackProvider();
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "ReentryGms", provider)
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            provider.Callback = async ct =>
            {
                ServiceResultException update = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await handle.RefreshAsync(ct).ConfigureAwait(false)))!;
                Assert.That(update.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException dispose = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await handle.DisposeAsync().ConfigureAwait(false)))!;
                Assert.That(dispose.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                return new GmsSystemSnapshot
                {
                    MachineTool = new Opc.Ua.MachineTool.MachineToolSnapshot { Revision = 1 }
                };
            };
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode(handle.NodeId), Is.SameAs(handle.State));
        }

        /// <summary>
        /// Shared registration resources are all attempted even when an independent binding's cleanup fails.
        /// </summary>
        [Test]
        public async Task DisposalAttemptsAllSharedBindingsAndRemovesNativeRootAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var failed = new Resource(true);
            var completed = new Resource(false);
            GmsHandle handle = await GmsSystemTests.Builder(context, "FailingResourceGms",
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .WithMachinery(builder =>
                {
                    builder.WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(failed));
                    builder.WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(completed));
                }).BuildAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<AggregateException>(async () => await handle.DisposeAsync().ConfigureAwait(false));
            Assert.That(failed.Calls, Is.EqualTo(1));
            Assert.That(completed.Calls, Is.EqualTo(1));
            Assert.That(fixture.Manager.FindPredefinedNode(handle.NodeId), Is.Null);
            Assert.That(handle.ConformanceUnits, Is.Empty);
        }

        /// <summary>
        /// Supplies mandatory defaults initially and then waits for the actual runtime cancellation token.
        /// </summary>
        private sealed class BlockingProvider : IGmsProvider
        {
            /// <summary>
            /// Signals admission of the pending provider callback.
            /// </summary>
            internal TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Signals the completion which root deletion must join.
            /// </summary>
            internal TaskCompletionSource<bool> Finished { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public async ValueTask<GmsSystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref m_reads) > 1)
                {
                    Entered.TrySetResult(true);
                    try
                    {
                        await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        Finished.TrySetResult(true);
                    }
                }
                return new GmsSystemSnapshot();
            }

            /// <summary>The intentionally pending provider operation, bounded by runtime cancellation.</summary>
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The number of acquisitions.</summary>
            private int m_reads;
        }

        /// <summary>
        /// Injects a callback through the public provider seam without replacing any runtime implementation.
        /// </summary>
        private sealed class CallbackProvider : IGmsProvider
        {
            /// <summary>
            /// Gets or sets the next equipment-acquisition callback.
            /// </summary>
            internal Func<CancellationToken, ValueTask<GmsSystemSnapshot>>? Callback { get; set; }

            /// <inheritdoc/>
            public ValueTask<GmsSystemSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
                Callback == null
                    ? new ValueTask<GmsSystemSnapshot>(new GmsSystemSnapshot()) : Callback(cancellationToken);
        }

        /// <summary>
        /// Represents an independent application binding enlisted through the real Machinery registration seam.
        /// </summary>
        private sealed class Resource(bool fail) : IAsyncDisposable
        {
            /// <summary>
            /// Gets the number of actual cleanup attempts.
            /// </summary>
            internal int Calls { get; private set; }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Calls++;
                if (fail)
                {
                    throw new InvalidOperationException("Expected independent binding cleanup failure.");
                }
                return default;
            }
        }
    }
}
