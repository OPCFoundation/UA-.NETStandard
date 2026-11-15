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
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class PredefinedNodeDictionaryTests
    {
        [Test]
        public void ConditionalRemovalPreservesDistinctEqualReplacement()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };
            nodes[original.NodeId] = original;
            nodes[replacement.NodeId] = replacement;

            Assert.That(original, Is.EqualTo(replacement));
            Assert.That(nodes.TryRemove(original.NodeId, original), Is.False);
            Assert.That(nodes[original.NodeId], Is.SameAs(replacement));
        }

        [Test]
        public void ConditionalRemovalRemovesExactNode()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            nodes[original.NodeId] = original;

            Assert.That(nodes.TryRemove(original.NodeId, original), Is.True);
            Assert.That(nodes.ContainsKey(original.NodeId), Is.False);
        }

        /// <summary>
        /// Collection-interface membership and removal reject an equal entry for a different node instance.
        /// </summary>
        // TODO: Remove this suppression when RCS1151 recognizes intentional interface-dispatch controls.
        [SuppressMessage(
            "Style",
            "RCS1151:Remove redundant cast",
            Justification = "Both assertions deliberately exercise collection-interface dispatch.")]
        [Test]
        public void CollectionRemovalPreservesDistinctEqualReplacement()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };
            nodes[original.NodeId] = replacement;
            var staleEntry = new KeyValuePair<NodeId, NodeState>(original.NodeId, original);

            Assert.That(((ICollection<KeyValuePair<NodeId, NodeState>>)nodes).Contains(staleEntry), Is.False);
            Assert.That(((ICollection<KeyValuePair<NodeId, NodeState>>)nodes).Remove(staleEntry), Is.False);
            Assert.That(nodes[original.NodeId], Is.SameAs(replacement));
        }

        [Test]
        public void ConditionalUpdatePreservesDistinctEqualReplacement()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };
            var proposed = new EqualObjectState { NodeId = original.NodeId };
            nodes[original.NodeId] = replacement;

            Assert.That(nodes.TryUpdate(original.NodeId, proposed, original), Is.False);
            Assert.That(nodes[original.NodeId], Is.SameAs(replacement));
            Assert.That(nodes.TryUpdate(original.NodeId, proposed, replacement), Is.True);
            Assert.That(nodes[original.NodeId], Is.SameAs(proposed));
        }

        [Test]
        public async Task ConcurrentDirectReplacementSurvivesStaleRemovalAsync()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };
            nodes[original.NodeId] = original;
            var captured = new TaskCompletionSource<NodeState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueRemoval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> removal = Task.Run(async () =>
            {
                NodeState staleNode = nodes[original.NodeId];
                captured.TrySetResult(staleNode);
                await continueRemoval.Task.ConfigureAwait(false);
                return nodes.TryRemove(original.NodeId, staleNode);
            });

            try
            {
                NodeState capturedNode = await captured.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(capturedNode, Is.SameAs(original));
                nodes[replacement.NodeId] = replacement;
            }
            finally
            {
                continueRemoval.TrySetResult(true);
            }

            Assert.That(await removal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.False);
            Assert.That(nodes[original.NodeId], Is.SameAs(replacement));
        }

        [Test]
        public void ValuesSnapshotIsIndependentOfLaterReplacement()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };
            nodes[original.NodeId] = original;
            ICollection<NodeState> snapshot = nodes.Values;
            nodes[original.NodeId] = replacement;
            var captured = new NodeState[1];
            var current = new NodeState[1];
            snapshot.CopyTo(captured, 0);
            nodes.Values.CopyTo(current, 0);

            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(captured[0], Is.SameAs(original));
            Assert.That(current[0], Is.SameAs(replacement));
        }

        [Test]
        public void DirectAndConditionalWritesShareTheSameIndex()
        {
            var nodes = new PredefinedNodeDictionary();
            var original = new EqualObjectState();
            var replacement = new EqualObjectState { NodeId = original.NodeId };

            Assert.That(nodes.TryAdd(original.NodeId, original), Is.True);
            Assert.That(nodes.TryAdd(original.NodeId, replacement), Is.False);
            Assert.That(nodes.GetOrAdd(original.NodeId, replacement), Is.SameAs(original));
            Assert.That(
                nodes.AddOrUpdate(original.NodeId, replacement, (_, current) =>
                    ReferenceEquals(current, original) ? replacement : current),
                Is.SameAs(replacement));
            Assert.That(nodes.TryRemove(original.NodeId, out NodeState? removed), Is.True);
            Assert.That(removed, Is.SameAs(replacement));
            Assert.That(nodes.IsEmpty, Is.True);
        }

        private sealed class EqualObjectState : BaseObjectState
        {
            public EqualObjectState()
                : base(null)
            {
                NodeId = new NodeId("EqualNode", 2);
            }

            public override bool Equals(object? obj)
            {
                return obj is EqualObjectState;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
