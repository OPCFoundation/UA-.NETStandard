/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Audit finding #52: the per-request permission attribute cache of Read, Write and
    /// Call is keyed by NodeIds the client chooses.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    [Parallelizable]
    public sealed class RandomizedNodeIdComparerTests
    {
        /// <summary>
        /// A numeric NodeId hashes to its identifier, so ids that are multiples of the
        /// bucket count share one bucket of a dictionary with the default comparer.
        /// </summary>
        [Test]
        public void NumericNodeIdsThatShareABucketAreSpreadOut()
        {
            const int count = 4096;
            const uint buckets = 4099; // a prime, like the bucket counts of Dictionary
            var defaultBuckets = new HashSet<uint>();
            var randomizedBuckets = new HashSet<uint>();
            for (uint ii = 1; ii <= count; ii++)
            {
                var nodeId = new NodeId(ii * buckets, 0);
                defaultBuckets.Add((uint)EqualityComparer<NodeId>.Default.GetHashCode(nodeId) % buckets);
                randomizedBuckets.Add((uint)RandomizedNodeIdComparer.Instance.GetHashCode(nodeId) % buckets);
            }

            Assert.That(defaultBuckets, Has.Count.EqualTo(1), "the ids must collide with the default hash");
            Assert.That(randomizedBuckets, Has.Count.GreaterThan(count / 2));
        }

        /// <summary>
        /// Numeric ids of the form (ns &lt;&lt; 16) ^ 1 share hash code 1 in every namespace, so
        /// the comparer must mix the namespace in on its own to tell them apart.
        /// </summary>
        [Test]
        public void NumericNodeIdsThatShareAHashAcrossNamespacesAreSpreadOut()
        {
            const int count = 4096;
            var defaultHashes = new HashSet<int>();
            var randomizedHashes = new HashSet<int>();
            for (uint ns = 1; ns <= count; ns++)
            {
                var nodeId = new NodeId((ns << 16) ^ 1u, (ushort)ns);
                defaultHashes.Add(EqualityComparer<NodeId>.Default.GetHashCode(nodeId));
                randomizedHashes.Add(RandomizedNodeIdComparer.Instance.GetHashCode(nodeId));
            }

            Assert.That(defaultHashes, Has.Count.EqualTo(1), "the ids must collide with the default hash");
            Assert.That(randomizedHashes, Has.Count.GreaterThan(count / 2));
        }

        /// <summary>
        /// A request large enough for hash flooding to matter gets the randomized comparer;
        /// a small one keeps the cheaper default comparer.
        /// </summary>
        [TestCase(NodeManagerServiceDispatcher.MaxValidationCacheDefaultComparerCount, false)]
        [TestCase(NodeManagerServiceDispatcher.MaxValidationCacheDefaultComparerCount + 1, true)]
        [TestCase(65535, true)]
        public void TheValidationCacheOfALargeRequestIsRandomized(int count, bool randomized)
        {
            var nodes = new ReadValueId[count];
            var calls = new CallMethodRequest[count];
            for (int ii = 0; ii < count; ii++)
            {
                var nodeId = new NodeId((uint)(ii + 1) * 65537u, 0);
                nodes[ii] = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
                calls[ii] = new CallMethodRequest { ObjectId = nodeId, MethodId = nodeId };
            }

            NodeManagerServiceDispatcher.PrepareValidationCache<ReadValueId>(
                nodes, out Dictionary<NodeId, Variant[]> readCache);
            NodeManagerServiceDispatcher.PrepareValidationCache<CallMethodRequest>(
                calls, out Dictionary<NodeId, Variant[]> callCache);

            foreach (Dictionary<NodeId, Variant[]> cache in new[] { readCache, callCache })
            {
                Assert.That(cache, Has.Count.EqualTo(count));
                Assert.That(
                    cache.Comparer,
                    randomized
                        ? Is.SameAs(RandomizedNodeIdComparer.Instance)
                        : Is.Not.SameAs(RandomizedNodeIdComparer.Instance));
            }
        }

        [Test]
        public void EqualNodeIdsHashAlikeAndCompareEqual()
        {
            var guid = Guid.NewGuid();
            NodeId[] ids =
            [
                new NodeId(42u, 3),
                new NodeId("Name", 3),
                new NodeId(guid, 3),
                new NodeId(new ByteString(new byte[] { 1, 2, 3 }), 3)
            ];
            NodeId[] copies =
            [
                new NodeId(42u, 3),
                new NodeId("Name", 3),
                new NodeId(guid, 3),
                new NodeId(new ByteString(new byte[] { 1, 2, 3 }), 3)
            ];
            for (int ii = 0; ii < ids.Length; ii++)
            {
                Assert.That(RandomizedNodeIdComparer.Instance.Equals(ids[ii], copies[ii]), Is.True);
                Assert.That(
                    RandomizedNodeIdComparer.Instance.GetHashCode(ids[ii]),
                    Is.EqualTo(RandomizedNodeIdComparer.Instance.GetHashCode(copies[ii])));
            }
            Assert.That(RandomizedNodeIdComparer.Instance.Equals(ids[0], new NodeId(42u, 4)), Is.False);
        }
    }
}
