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
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for the internal <c>PathCache</c> LRU. The class is
    /// internal but visible to this assembly via the
    /// <c>InternalsVisibleTo</c> declaration in
    /// <c>Opc.Ua.Client.csproj</c>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class PathCacheTests
    {
        [Test]
        public void DisabledCacheReturnsNullAndDoesNotStore()
        {
            // ARRANGE
            var cache = new PathCache(0);
            var parent = new NodeId(1);
            var name = new QualifiedName("foo");
            var child = new NodeId(2);

            // ACT
            cache.Put(parent, name, child);

            // ASSERT
            Assert.That(cache.TryGet(parent, name), Is.Null);
            Assert.That(cache.Count, Is.Zero);
        }

        [Test]
        public void PutThenTryGetReturnsTheStoredValue()
        {
            var cache = new PathCache(8);
            var parent = new NodeId(1);
            var name = new QualifiedName("foo");
            var child = new NodeId(2);

            cache.Put(parent, name, child);

            NodeId? got = cache.TryGet(parent, name);
            Assert.That(got, Is.Not.Null);
            Assert.That(got!.Value, Is.EqualTo(child));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void DifferentParentsAreIndependent()
        {
            var cache = new PathCache(8);
            var nameA = new QualifiedName("foo");
            var nameB = new QualifiedName("foo");

            cache.Put(new NodeId(1), nameA, new NodeId(10));
            cache.Put(new NodeId(2), nameB, new NodeId(20));

            Assert.That(cache.TryGet(new NodeId(1), nameA)!.Value, Is.EqualTo(new NodeId(10)));
            Assert.That(cache.TryGet(new NodeId(2), nameB)!.Value, Is.EqualTo(new NodeId(20)));
        }

        [Test]
        public void NamespacedSiblingsAreDistinct()
        {
            var cache = new PathCache(8);
            var parent = new NodeId(1);
            cache.Put(parent, new QualifiedName("foo", 1), new NodeId(10));
            cache.Put(parent, new QualifiedName("foo", 2), new NodeId(20));

            Assert.That(cache.TryGet(parent, new QualifiedName("foo", 1))!.Value, Is.EqualTo(new NodeId(10)));
            Assert.That(cache.TryGet(parent, new QualifiedName("foo", 2))!.Value, Is.EqualTo(new NodeId(20)));
        }

        [Test]
        public void PutOverwritesExistingEntry()
        {
            var cache = new PathCache(8);
            var parent = new NodeId(1);
            var name = new QualifiedName("foo");

            cache.Put(parent, name, new NodeId(10));
            cache.Put(parent, name, new NodeId(99));

            Assert.That(cache.TryGet(parent, name)!.Value, Is.EqualTo(new NodeId(99)));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void LruEvictsOldestWhenCapacityExceeded()
        {
            var cache = new PathCache(2);
            var parent = new NodeId(1);

            cache.Put(parent, new QualifiedName("a"), new NodeId(10));
            cache.Put(parent, new QualifiedName("b"), new NodeId(20));
            cache.Put(parent, new QualifiedName("c"), new NodeId(30));

            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGet(parent, new QualifiedName("a")), Is.Null);
            Assert.That(cache.TryGet(parent, new QualifiedName("b"))!.Value, Is.EqualTo(new NodeId(20)));
            Assert.That(cache.TryGet(parent, new QualifiedName("c"))!.Value, Is.EqualTo(new NodeId(30)));
        }

        [Test]
        public void TryGetMovesEntryToFrontOfLru()
        {
            var cache = new PathCache(2);
            var parent = new NodeId(1);

            cache.Put(parent, new QualifiedName("a"), new NodeId(10));
            cache.Put(parent, new QualifiedName("b"), new NodeId(20));
            // Touch 'a' so it becomes most recently used.
            _ = cache.TryGet(parent, new QualifiedName("a"));
            // Insert 'c' — 'b' should be evicted, not 'a'.
            cache.Put(parent, new QualifiedName("c"), new NodeId(30));

            Assert.That(cache.TryGet(parent, new QualifiedName("a"))!.Value, Is.EqualTo(new NodeId(10)));
            Assert.That(cache.TryGet(parent, new QualifiedName("b")), Is.Null);
            Assert.That(cache.TryGet(parent, new QualifiedName("c"))!.Value, Is.EqualTo(new NodeId(30)));
        }

        [Test]
        public void InvalidateRemovesNamedEntry()
        {
            var cache = new PathCache(8);
            var parent = new NodeId(1);
            cache.Put(parent, new QualifiedName("a"), new NodeId(10));
            cache.Put(parent, new QualifiedName("b"), new NodeId(20));

            cache.Invalidate(parent, new QualifiedName("a"));

            Assert.That(cache.TryGet(parent, new QualifiedName("a")), Is.Null);
            Assert.That(cache.TryGet(parent, new QualifiedName("b"))!.Value, Is.EqualTo(new NodeId(20)));
        }

        [Test]
        public void InvalidateChildrenOfRemovesAllEntriesForParent()
        {
            var cache = new PathCache(8);
            var parentA = new NodeId(1);
            var parentB = new NodeId(2);
            cache.Put(parentA, new QualifiedName("x"), new NodeId(10));
            cache.Put(parentA, new QualifiedName("y"), new NodeId(11));
            cache.Put(parentB, new QualifiedName("z"), new NodeId(20));

            cache.InvalidateChildrenOf(parentA);

            Assert.That(cache.TryGet(parentA, new QualifiedName("x")), Is.Null);
            Assert.That(cache.TryGet(parentA, new QualifiedName("y")), Is.Null);
            Assert.That(cache.TryGet(parentB, new QualifiedName("z"))!.Value, Is.EqualTo(new NodeId(20)));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void ClearRemovesEverything()
        {
            var cache = new PathCache(8);
            var parent = new NodeId(1);
            cache.Put(parent, new QualifiedName("a"), new NodeId(10));
            cache.Put(parent, new QualifiedName("b"), new NodeId(20));

            cache.Clear();

            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.TryGet(parent, new QualifiedName("a")), Is.Null);
            Assert.That(cache.TryGet(parent, new QualifiedName("b")), Is.Null);
        }

        [Test]
        public void NegativeCapacityThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PathCache(-1));
        }
    }
}
