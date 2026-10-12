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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT JSON element comparer.
    /// </summary>
    [TestFixture]
    public sealed class WotJsonElementComparerTests
    {
        /// <summary>
        /// Verifies that identical sibling values retain distinct occurrence identity.
        /// </summary>
        [Test]
        public void IdenticalSiblingValuesRetainDistinctOccurrenceIdentity()
        {
            using var document = JsonDocument.Parse("""{"a":{},"b":{}}""");
            var comparer = new WotJsonElementComparer([document.RootElement]);
            JsonElement first = document.RootElement.GetProperty("a");
            JsonElement reread = document.RootElement.GetProperty("a");
            JsonElement second = document.RootElement.GetProperty("b");
            var values = new Dictionary<JsonElement, string>(comparer)
            {
                [first] = "first",
                [second] = "second"
            };

            Assert.Multiple(() =>
            {
                Assert.That(comparer.Equals(first, reread), Is.True);
                Assert.That(comparer.GetHashCode(first), Is.EqualTo(comparer.GetHashCode(reread)));
                Assert.That(comparer.Equals(first, second), Is.False);
                Assert.That(values[reread], Is.EqualTo("first"));
                Assert.That(values[second], Is.EqualTo("second"));
                Assert.That(values, Has.Count.EqualTo(2));
            });
        }

        /// <summary>
        /// Verifies that distinct roots and detached clones never alias an occurrence.
        /// </summary>
        [Test]
        public void DistinctRootsAndDetachedClonesNeverAliasAnOccurrence()
        {
            using var first = JsonDocument.Parse("""{"value":{}}""");
            using var second = JsonDocument.Parse("""{"value":{}}""");
            var comparer = new WotJsonElementComparer([first.RootElement, second.RootElement]);
            JsonElement original = first.RootElement.GetProperty("value");
            JsonElement other = second.RootElement.GetProperty("value");
            JsonElement clone = original.Clone();
            var elements = new HashSet<JsonElement>(comparer) { original, other, clone };

            Assert.Multiple(() =>
            {
                Assert.That(comparer.Equals(original, other), Is.False);
                Assert.That(comparer.Equals(original, clone), Is.False);
                Assert.That(comparer.Equals(clone, clone), Is.True);
                Assert.That(comparer.GetHashCode(clone), Is.EqualTo(clone.GetHashCode()));
                Assert.That(elements, Has.Count.EqualTo(3));
                Assert.That(elements.Contains(second.RootElement.GetProperty("value")), Is.True);
            });
        }

        /// <summary>
        /// Verifies that equal content at many offsets does not collapse into one hash bucket.
        /// </summary>
        [Test]
        public void EqualContentAtManyOffsetsDoesNotCollapseIntoOneHashBucket()
        {
            using var document = JsonDocument.Parse("[" + string.Join(",", Enumerable.Repeat("{}", 1000)) + "]");
            var comparer = new WotJsonElementComparer([document.RootElement]);
            var elements = new HashSet<JsonElement>(comparer);
            var hashes = new HashSet<int>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                elements.Add(element);
                hashes.Add(comparer.GetHashCode(element));
            }

            Assert.Multiple(() =>
            {
                Assert.That(elements, Has.Count.EqualTo(1000));
                Assert.That(hashes, Has.Count.GreaterThan(900));
                Assert.That(elements.Contains(document.RootElement[500]), Is.True);
            });
        }

        /// <summary>
        /// Verifies that empty context retains default foreign element hashing.
        /// </summary>
        [Test]
        public void EmptyContextRetainsDefaultForeignElementHashing()
        {
            using var document = JsonDocument.Parse("""{"value":42}""");
            var comparer = new WotJsonElementComparer([]);
            JsonElement value = document.RootElement.GetProperty("value");

            Assert.Multiple(() =>
            {
                Assert.That(comparer.GetHashCode(value), Is.EqualTo(value.GetHashCode()));
                Assert.That(comparer.GetHashCode(default), Is.Zero);
                Assert.That(comparer.Equals(default, default), Is.True);
                Assert.That(comparer.Equals(default, value), Is.False);
            });
        }

        /// <summary>
        /// Verifies that hashing rejects disposed owner rather than using returned buffer.
        /// </summary>
        [Test]
        public void HashingRejectsDisposedOwnerRatherThanUsingReturnedBuffer()
        {
            var document = JsonDocument.Parse("""{"value":{}}""");
            var comparer = new WotJsonElementComparer([document.RootElement]);
            JsonElement value = document.RootElement.GetProperty("value");
            document.Dispose();

            Assert.That(() => comparer.GetHashCode(value), Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
