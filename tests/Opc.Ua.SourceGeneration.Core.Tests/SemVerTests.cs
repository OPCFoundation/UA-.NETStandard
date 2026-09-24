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


using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for the lenient model version comparison.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SemVerTests
    {
        /// <summary>
        /// Regression: '+' build metadata was treated as a pre-release tag, so
        /// "1.05.03+20240101" ranked below "1.05.03". SemVer 2.0 ignores build
        /// metadata for precedence.
        /// </summary>
        [TestCase("1.05.03+20240101", "1.05.03", 0)]
        [TestCase("1.05.03+hotfix", "1.05.02", 1)]
        [TestCase("1.0.0-rc1+build", "1.0.0", -1)]
        [TestCase("1.0.0+build-5", "1.0.0", 0)]
        public void BuildMetadataDoesNotAffectPrecedence(string left, string right, int expected)
        {
            Assert.That(
                System.Math.Sign(SemVer.CompareVersionStrings(left, right)),
                Is.EqualTo(expected));
        }

        [Test]
        public void BuildMetadataIsNotAPrerelease()
        {
            Assert.That(SemVer.TryParse("1.05.03+20240101", out SemVer version), Is.True);
            Assert.That(version.IsPrerelease, Is.False);
            Assert.That(version, Is.EqualTo(SemVer.Parse("1.5.3")));
        }

        /// <summary>
        /// Regression: the fourth component was dropped, so "1.0.0.1" and
        /// "1.0.0.2" compared equal.
        /// </summary>
        [TestCase("1.0.0.1", "1.0.0.2", -1)]
        [TestCase("1.0.0.2", "1.0.0.1", 1)]
        [TestCase("1.0.0.0", "1.0.0", 0)]
        [TestCase("1.0.1.0", "1.0.0.9", 1)]
        public void FourthComponentIsCompared(string left, string right, int expected)
        {
            Assert.That(
                System.Math.Sign(SemVer.CompareVersionStrings(left, right)),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// The total order ranks the kind of version first, so it is transitive
        /// across a mix of versions and dates.
        /// </summary>
        [TestCase("1.0.3", "1.0.2", 1)]
        [TestCase("1.0.2", "2023-01-01", 1)]
        [TestCase("2024-01-01", "2023-01-01", 1)]
        [TestCase("2023-01-01", "", 1)]
        [TestCase("", null, 0)]
        [TestCase("abc", "2023-01-01", 1)]
        [TestCase("1.0.0", "abc", 1)]
        public void TotalOrderRanksTheKindOfVersionFirst(string left, string right, int expected)
        {
            Assert.That(
                System.Math.Sign(SemVer.CompareVersionStringsTotal(left, right)),
                Is.EqualTo(expected));
            Assert.That(
                System.Math.Sign(SemVer.CompareVersionStringsTotal(right, left)),
                Is.EqualTo(-expected));
        }
    }
}
