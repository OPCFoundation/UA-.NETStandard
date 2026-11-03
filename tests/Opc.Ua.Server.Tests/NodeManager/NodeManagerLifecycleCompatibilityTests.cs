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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies compatibility extension retry, replay, cancellation, and abort behavior
    /// against isolated lifecycle mocks without starting a server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerLifecycleCompatibilityTests
    {
        /// <summary>
        /// Factory/preparation exceptions are never mistaken for a commit-time routing conflict.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityPreparationFailureIsNotRetriedAsync(bool routingConflict)
        {
            Exception failure = routingConflict
                ? new NodeManagerRoutingConflictException("Preparation callback failed.")
                : new SentinelException();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                CreateFactory())
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither an invoked decision, a committed batch nor an unrelated provider error permits retry.
        /// </summary>
        [TestCase("Decision")]
        [TestCase("Committed")]
        [TestCase("Provider")]
        public async Task CompatibilityCommitFailureIsNotReplayedAsync(string stage)
        {
            int decisions = 0;
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            Exception failure = stage == "Provider"
                ? new InvalidOperationException("The address space changed while the batch was prepared.")
                : new InvalidOperationException(
                    "Routing conflict after an irreversible stage.",
                    new NodeManagerRoutingConflictException(
                        new NodeManagerRoutingConflictException("Routing conflict after an irreversible stage."),
                        prepared.Object));
            prepared.SetupGet(value => value.IsCommitted).Returns(stage == "Committed");
            prepared.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Func<CancellationToken, ValueTask> decideAsync, CancellationToken token) =>
                {
                    if (stage == "Decision")
                    {
                        decisions++;
                        await decideAsync(token).ConfigureAwait(false);
                    }
                    throw failure;
                });
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                CreateFactory())
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(stage == "Decision" ? 1 : 0));
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Cancellation stops conflict retries after aborting each already-prepared candidate.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictRetriesStopAtCancellationAsync()
        {
            using var cancellation = new CancellationTokenSource();
            int attempts = 0;
            var candidates = new List<Mock<IPreparedNodeManagerBatch>>();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeManagerBatchChange> _, CancellationToken _) =>
                {
                    var prepared = new Mock<IPreparedNodeManagerBatch>();
                    prepared.Setup(value => value.CommitAsync(
                        It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                        .Returns((Func<CancellationToken, ValueTask> _, CancellationToken _) =>
                        {
                            if (++attempts == 2)
                            {
                                cancellation.Cancel();
                            }
                            throw new InvalidOperationException(
                                "Independent routing publication.",
                                new NodeManagerRoutingConflictException(
                                    new NodeManagerRoutingConflictException("Independent routing publication."),
                                    prepared.Object));
                        });
                    candidates.Add(prepared);
                    return new ValueTask<IPreparedNodeManagerBatch>(prepared.Object);
                });
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                CreateFactory(),
                cancellation.Token).AsTask(), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(candidates, Has.Count.EqualTo(2));
            foreach (Mock<IPreparedNodeManagerBatch> candidate in candidates)
            {
                candidate.Verify(value => value.DisposeAsync(), Times.Once);
            }
        }

        /// <summary>
        /// A failed abort is surfaced instead of starting another candidate with unreleased resources.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictDoesNotHideAbortFailureAsync()
        {
            var failure = new SentinelException();
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException(
                    "Independent routing publication.",
                    new NodeManagerRoutingConflictException(
                        new NodeManagerRoutingConflictException("Independent routing publication."), prepared.Object)));
            prepared.Setup(value => value.DisposeAsync()).Returns(new ValueTask(Task.FromException(failure)));
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                CreateFactory())
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Creates a factory passed through compatibility calls but never invoked by the mocked lifecycle.
        /// </summary>
        private static RuntimeNodeSetNodeManagerFactory CreateFactory()
        {
            // The mocked lifecycle never invokes this factory; it only observes compatibility-call preparation.
            return new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "Compatibility",
                        _ => new ValueTask<Stream>(Stream.Null),
                        ["urn:opcfoundation:tests:lifecycle-compatibility"])
                ]
            });
        }

        /// <summary>
        /// Identifies the exact preparation or abort failure that must reach the caller.
        /// </summary>
        private sealed class SentinelException : Exception
        {
            /// <summary>
            /// Creates an identifiable failure without a message.
            /// </summary>
            public SentinelException()
            {
            }

            /// <summary>
            /// Creates an identifiable failure with a message.
            /// </summary>
            public SentinelException(string message)
                : base(message)
            {
            }

            /// <summary>
            /// Creates an identifiable failure with its originating exception.
            /// </summary>
            public SentinelException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
    }
}
