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
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for the OPC UA Part 4 §7.40 Like-operator wildcard
    /// match used by Part 17 <c>FindAlias</c>/<c>FindAliasVerbose</c>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameWildcardMatcherTests
    {
        [TestCase("Sensor", "Sensor", true)]
        [TestCase("sensor", "Sensor", false)]
        [TestCase("Sensor", "%", true)]
        [TestCase("Sensor", "Sen%", true)]
        [TestCase("Sensor", "%sor", true)]
        [TestCase("Sensor", "%nso%", true)]
        [TestCase("Sensor", "Sen_or", true)]
        [TestCase("Sensor", "Sen__r", true)]
        [TestCase("Sensor", "Sen___", true)]
        [TestCase("Sensor", "Sen____", false)]
        [TestCase("Sensor", "[Ss]ensor", true)]
        [TestCase("sensor", "[Ss]ensor", true)]
        [TestCase("Tensor", "[Ss]ensor", false)]
        [TestCase("Tensor", "[!Ss]ensor", true)]
        [TestCase("Sensor", "[!Ss]ensor", false)]
        [TestCase("Tag.X", "Tag.X", true)]
        [TestCase("Tag.X", "Tag_X", true)]
        [TestCase("Tag.X", @"Tag\.X", true)]
        [TestCase("Tag_X", @"Tag\_X", true)]
        [TestCase("TagAX", @"Tag\_X", false)]
        [TestCase("Tag%X", @"Tag\%X", true)]
        public void IsMatchPositive(string target, string pattern, bool expected)
        {
            Assert.That(AliasNameWildcardMatcher.IsMatch(target, pattern),
                Is.EqualTo(expected));
        }

        [Test]
        public void EmptyPatternMatchesNothing()
        {
            Assert.That(AliasNameWildcardMatcher.IsMatch("anything", ""),
                Is.False);
        }

        [Test]
        public void EmptyTargetMatchesEmptyWildcard()
        {
            Assert.That(AliasNameWildcardMatcher.IsMatch("", "%"), Is.True);
            Assert.That(AliasNameWildcardMatcher.IsMatch("", "_"), Is.False);
            Assert.That(AliasNameWildcardMatcher.IsMatch("", "X"), Is.False);
        }

        [Test]
        public void NullInputsReturnFalse()
        {
            Assert.That(AliasNameWildcardMatcher.IsMatch(null, "%"), Is.False);
            Assert.That(AliasNameWildcardMatcher.IsMatch("X", null), Is.False);
            Assert.That(AliasNameWildcardMatcher.IsMatch(null, null), Is.False);
        }

        [Test]
        public void RegexMetaCharactersAreEscaped()
        {
            // The pattern "a.b" should match only the literal "a.b" — the
            // wildcard implementation must escape regex metacharacters.
            Assert.That(AliasNameWildcardMatcher.IsMatch("aXb", "a.b"), Is.False);
            Assert.That(AliasNameWildcardMatcher.IsMatch("a.b", "a.b"), Is.True);
            Assert.That(AliasNameWildcardMatcher.IsMatch("a(b)c", "a(b)c"), Is.True);
            Assert.That(AliasNameWildcardMatcher.IsMatch("a+b", "a+b"), Is.True);
        }
    }
}
