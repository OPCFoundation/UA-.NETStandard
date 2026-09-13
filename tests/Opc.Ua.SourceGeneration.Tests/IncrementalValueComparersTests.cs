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

using System.Collections.Generic;
using System.Collections.Immutable;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for the value comparers the incremental pipeline uses to keep its
    /// compilation-derived stages cache-equal.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class IncrementalValueComparersTests
    {
        /// <summary>
        /// Regression: ImmutableArray compares by reference, so a stage derived
        /// from the compilation produced an "unequal" result on every keystroke
        /// and the whole model generation ran again.
        /// </summary>
        [Test]
        public void ArrayComparerComparesByContentNotByReference()
        {
            ImmutableArray<string> first = ["a", "b"];
            ImmutableArray<string> second = ["a", "b"];

            IEqualityComparer<ImmutableArray<string>> comparer =
                IncrementalValueComparers.ForArray<string>();

            Assert.That(
                EqualityComparer<ImmutableArray<string>>.Default.Equals(first, second),
                Is.False,
                "the default comparison is by reference - this is the bug");
            Assert.That(comparer.Equals(first, second), Is.True);
            Assert.That(
                comparer.GetHashCode(first),
                Is.EqualTo(comparer.GetHashCode(second)));
        }

        [Test]
        public void ArrayComparerDistinguishesDifferentContent()
        {
            IEqualityComparer<ImmutableArray<string>> comparer =
                IncrementalValueComparers.ForArray<string>();

            Assert.Multiple(() =>
            {
                Assert.That(comparer.Equals(["a", "b"], ["a", "c"]), Is.False);
                Assert.That(comparer.Equals(["a"], ["a", "b"]), Is.False);
                Assert.That(comparer.Equals([], []), Is.True);
            });
        }

        [Test]
        public void ArrayComparerHandlesDefaultArrays()
        {
            IEqualityComparer<ImmutableArray<string>> comparer =
                IncrementalValueComparers.ForArray<string>();

            Assert.Multiple(() =>
            {
                Assert.That(comparer.Equals(default, default), Is.True);
                Assert.That(comparer.Equals(default, []), Is.False);
                Assert.That(comparer.GetHashCode(default), Is.Zero);
            });
        }

        /// <summary>
        /// Same for the state-type index, which is an ImmutableHashSet.
        /// </summary>
        [Test]
        public void SetComparerComparesByContentNotByReference()
        {
            ImmutableHashSet<string> first = ["a", "b"];
            ImmutableHashSet<string> second = ["b", "a"];

            IEqualityComparer<ImmutableHashSet<string>> comparer =
                IncrementalValueComparers.ForSet<string>();

            Assert.That(
                ReferenceEquals(first, second),
                Is.False,
                "two separately built sets are distinct instances");
            Assert.That(
                comparer.Equals(first, second),
                Is.True,
                "membership is what matters, not insertion order");
            Assert.That(
                comparer.GetHashCode(first),
                Is.EqualTo(comparer.GetHashCode(second)));
        }

        [Test]
        public void SetComparerDistinguishesDifferentContent()
        {
            IEqualityComparer<ImmutableHashSet<string>> comparer =
                IncrementalValueComparers.ForSet<string>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    comparer.Equals(["a", "b"], ["a", "c"]),
                    Is.False);
                Assert.That(
                    comparer.Equals(["a"], ["a", "b"]),
                    Is.False);
                Assert.That(comparer.Equals(null, null), Is.True);
                Assert.That(comparer.Equals(null, ["a"]), Is.False);
                Assert.That(comparer.Equals(["a"], null), Is.False);
            });
        }

        /// <summary>
        /// Roslyn compares the previous run's output, which can be absent, so
        /// the hash has to tolerate a null set rather than throwing inside the
        /// incremental pipeline.
        /// </summary>
        [Test]
        public void SetComparerHandlesNull()
        {
            IEqualityComparer<ImmutableHashSet<string>> comparer =
                IncrementalValueComparers.ForSet<string>();

            Assert.Multiple(() =>
            {
                Assert.That(comparer.GetHashCode(null), Is.Zero);
                Assert.That(comparer.GetHashCode(ImmutableHashSet<string>.Empty), Is.Zero);
            });
        }
    }
}
