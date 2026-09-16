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
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Tests for <see cref="LikePattern"/> (OPC 10000-4 §7.7.3).
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public class LikePatternTests
    {
        /// <summary>
        /// The examples of the OPC 10000-4 §7.7.3 "Wildcard characters" table.
        /// </summary>
        [TestCase("main", "main%", true)]
        [TestCase("mainserver", "main%", true)]
        [TestCase("amain", "main%", false)]
        [TestCase("entail", "%en%", true)]
        [TestCase("green", "%en%", true)]
        [TestCase("content", "%en%", true)]
        [TestCase("alpha", "%en%", false)]
        [TestCase("5%", "5[%]", true)]
        [TestCase("5x", "5[%]", false)]
        [TestCase("would", "_ould", true)]
        [TestCase("could", "_ould", true)]
        [TestCase("ould", "_ould", false)]
        [TestCase("shoulder", "_ould", false)]
        [TestCase("5_", "5[_]", true)]
        [TestCase("5a", "5[_]", false)]
        [TestCase(@"\", @"\\", true)]
        [TestCase("%", @"\%", true)]
        [TestCase("_", @"\_", true)]
        [TestCase("a", @"\_", false)]
        [TestCase("abc1", "abc[13-68]", true)]
        [TestCase("abc3", "abc[13-68]", true)]
        [TestCase("abc4", "abc[13-68]", true)]
        [TestCase("abc5", "abc[13-68]", true)]
        [TestCase("abc6", "abc[13-68]", true)]
        [TestCase("abc8", "abc[13-68]", true)]
        [TestCase("abc2", "abc[13-68]", false)]
        [TestCase("abc7", "abc[13-68]", false)]
        [TestCase("abc-", "abc[13-68]", false)]
        [TestCase("xyzc", "xyz[c-f]", true)]
        [TestCase("xyzf", "xyz[c-f]", true)]
        [TestCase("xyzg", "xyz[c-f]", false)]
        [TestCase("ABC2", "ABC[^13-5]", true)]
        [TestCase("ABC6", "ABC[^13-5]", true)]
        [TestCase("ABC1", "ABC[^13-5]", false)]
        [TestCase("ABC3", "ABC[^13-5]", false)]
        [TestCase("ABC4", "ABC[^13-5]", false)]
        [TestCase("ABC5", "ABC[^13-5]", false)]
        [TestCase("xyza", "xyz[^dgh]", true)]
        [TestCase("xyzd", "xyz[^dgh]", false)]
        [TestCase("xyzg", "xyz[^dgh]", false)]
        [TestCase("xyzh", "xyz[^dgh]", false)]
        [TestCase("That is fine", "Th[ia][ts]%", true)]
        [TestCase("This is fine", "Th[ia][ts]%", true)]
        [TestCase("Then is fine", "Th[ia][ts]%", false)]
        [TestCase("this is fine", "Th[ia][ts]%", false)]
        public void SpecificationExamples(string target, string pattern, bool expected)
        {
            Assert.That(LikePattern.IsMatch(target, pattern), Is.EqualTo(expected));
        }

        /// <summary>
        /// The QueryServers/QueryApplications patterns of the CTT GDS
        /// Application Directory and Query Applications units against the
        /// applicationUri and ApplicationName values the CTT registers.
        /// The GDS used to return no record for most of them.
        /// </summary>
        [TestCase("urn:OPCFoundation:ServerApplication", "%_erver%", true)]
        [TestCase("urn:OPCFoundation:ComplianceTestToolEmbeddedServer", "%_erver%", true)]
        [TestCase("urn:OPCFoundation:ComplianceTestTool", "%_erver%", false)]
        [TestCase("Server", "%_erver%", true)]
        [TestCase("erver", "%_erver%", false)]
        [TestCase("urn:OPCFoundation:ComplianceTestToolEmbeddedServer", "%e_", true)]
        [TestCase("urn:OPCFoundation:ServerApplication", "%e_", false)]
        [TestCase("cab:other_foundation:ClientAndServer", @"%\_%", true)]
        [TestCase("urn:OPCFoundation:ServerApplication", @"%\_%", false)]
        [TestCase("Server Application with % wildcard character", @"%\%%", true)]
        [TestCase("OPC Foundation Compliance Test Tool", @"%\%%", false)]
        [TestCase("Example_Vendor - ClientAndServer", "%[q-s]", true)]
        [TestCase("OPC Foundation Compliance Test Tool", "%[q-s]", false)]
        [TestCase("OPC Foundation Compliance Test Tool", "%[^q-s]", true)]
        [TestCase("urn:OPCFoundation:ComplianceTestToolEmbeddedServer", "%[^q-s]", false)]
        [TestCase("OPC Foundation Compliance Test Tool", "%_ompliance%", true)]
        [TestCase("cab:other_foundation:ClientAndServer", "[a-c]%", true)]
        [TestCase("urn:OPCFoundation:ServerApplication", "[^a-c]%", true)]
        [TestCase("cab:other_foundation:ClientAndServer", "[^a-c]%", false)]
        public void ConformanceTestPatterns(string target, string pattern, bool expected)
        {
            Assert.That(LikePattern.IsMatch(target, pattern), Is.EqualTo(expected));
        }

        [Test]
        public void MatchingIsAnchoredAndCaseSensitive()
        {
            Assert.That(LikePattern.IsMatch("a_b", "[_]"), Is.False);
            Assert.That(LikePattern.IsMatch("_", "[_]"), Is.True);
            Assert.That(LikePattern.IsMatch("xabcx", "abc"), Is.False);
            Assert.That(LikePattern.IsMatch("ABC", "abc"), Is.False);
            // A list negation matches one character; it does not exclude the
            // characters from the rest of the string.
            Assert.That(LikePattern.IsMatch("other", "%[^f-h]%"), Is.True);
        }

        [Test]
        public void EmptyAndNullInputs()
        {
            Assert.That(LikePattern.IsMatch(string.Empty, string.Empty), Is.True);
            Assert.That(LikePattern.IsMatch("a", string.Empty), Is.False);
            Assert.That(LikePattern.IsMatch(string.Empty, "%"), Is.True);
            Assert.That(LikePattern.IsMatch(string.Empty, "%%"), Is.True);
            Assert.That(LikePattern.IsMatch(string.Empty, "_"), Is.False);
            Assert.That(LikePattern.IsMatch(null, "%"), Is.False);
            Assert.That(LikePattern.IsMatch("a", null), Is.False);
            Assert.That(LikePattern.IsValid(null), Is.False);
            Assert.That(LikePattern.IsValid(string.Empty), Is.True);
        }

        [TestCase("a.b", "a.b", true)]
        [TestCase("aXb", "a.b", false)]
        [TestCase("a(b)c+d*", "a(b)c+d*", true)]
        [TestCase("a$^b", "a$^b", true)]
        [TestCase("-", "[-a]", true)]
        [TestCase("-", "[a-]", true)]
        [TestCase("]", @"[\]]", true)]
        [TestCase("^", @"[\^]", true)]
        [TestCase("!", "[!a]", true)]
        [TestCase("a", "[!a]", false)]
        [TestCase("a%b", "a%%%b", true)]
        [TestCase("aaab", "%a_b", true)]
        [TestCase("ab", "%a_b", false)]
        public void LiteralsListsAndBacktracking(string target, string pattern, bool expected)
        {
            Assert.That(LikePattern.IsMatch(target, pattern), Is.EqualTo(expected));
        }

        /// <summary>
        /// Malformed search strings, including the CTT GDS Application
        /// Directory 078.js pattern with a '^' that is not the first list
        /// character.
        /// </summary>
        [TestCase("%[a^j-l]%")]
        [TestCase("[a^]")]
        [TestCase("[a-^]")]
        [TestCase("A[")]
        [TestCase("A[abc")]
        [TestCase(@"A\")]
        [TestCase(@"A\\\")]
        [TestCase("A[]")]
        [TestCase("A[^]")]
        [TestCase("A[!]")]
        [TestCase(@"A[a\]")]
        [TestCase(@"A[a\")]
        [TestCase("A[z-a]")]
        public void InvalidPatternsAreRejected(string pattern)
        {
            Assert.That(LikePattern.IsValid(pattern), Is.False);
            Assert.That(LikePattern.TryParse(pattern, out LikePattern parsed), Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(LikePattern.IsMatch("A[", pattern), Is.False);
        }

        [Test]
        public void ParsedPatternCanBeReused()
        {
            Assert.That(LikePattern.TryParse("%Server", out LikePattern pattern), Is.True);
            Assert.That(pattern.Pattern, Is.EqualTo("%Server"));
            Assert.That(pattern.ToString(), Is.EqualTo("%Server"));
            Assert.That(pattern.IsMatch("ClientAndServer"), Is.True);
            Assert.That(pattern.IsMatch("ServerApplication"), Is.False);
            Assert.That(pattern.IsMatch(null), Is.False);
        }

        /// <summary>
        /// Verifies finite and unlimited evaluation preserve shared Like syntax and whole-string matching.
        /// </summary>
        [TestCase("d", @"[\d]", true)]
        [TestCase("5", @"[\d]", false)]
        [TestCase("-", @"[a\-z]", true)]
        [TestCase("m", @"[a\-z]", false)]
        [TestCase("]", @"[\]]", true)]
        [TestCase("a\n", "a", false)]
        [TestCase("a\n", "a_", true)]
        [TestCase("a\nb", "a%b", true)]
        [TestCase("", "", true)]
        [TestCase("", "%", true)]
        [TestCase("", "_", false)]
        [TestCase(null, "%", false)]
        public void EvaluationTimeoutPreservesPatternSemantics(string target, string text, bool expected)
        {
            Assert.That(LikePattern.TryParse(text, out LikePattern pattern), Is.True);
            Assert.That(pattern.IsMatch(target, TimeSpan.FromSeconds(5)), Is.EqualTo(expected));
            Assert.That(pattern.IsMatch(target, Timeout.InfiniteTimeSpan), Is.EqualTo(expected));
            Assert.That(pattern.IsMatch(target), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies invalid timeout values are reported explicitly rather than treated as an unlimited match.
        /// </summary>
        [TestCase(0)]
        [TestCase(-2)]
        public void InvalidEvaluationTimeoutIsRejected(int milliseconds)
        {
            Assert.That(LikePattern.TryParse("%", out LikePattern pattern), Is.True);
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                pattern.IsMatch("value", TimeSpan.FromMilliseconds(milliseconds)));
            Assert.That(error.ParamName, Is.EqualTo("matchTimeout"));
        }

        /// <summary>
        /// Verifies timeout enforcement during prefix retries and long character-list scans without poisoning reuse.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EvaluationTimeoutBoundsWorkAndPreservesPatternReuse(bool characterList)
        {
            string text = characterList ? "[" + new string('a', 200_000) + "]" : "%" + new string('a', 8192) + "b";
            string target = characterList ? "z" : new string('a', 32768);
            string matching = characterList ? "a" : new string('a', 8192) + "b";
            Assert.That(LikePattern.TryParse(text, out LikePattern pattern), Is.True);
            var elapsed = Stopwatch.StartNew();

            Assert.Throws<TimeoutException>(() => pattern.IsMatch(target, TimeSpan.FromMilliseconds(1)));

            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(pattern.IsMatch(matching, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(pattern.IsMatch(matching), Is.True);
        }
    }
}
