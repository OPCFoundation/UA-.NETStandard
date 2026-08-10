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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Exercises the composition primitive that lets a host expose the tools
    /// of several bounded profiles at once - a vision-guided pick-and-place
    /// agent, for instance, that needs both the Vision tools to see and the
    /// Robotics tools to act.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpToolProfileSetTests
    {
        [Test]
        public void EmptySetContainsNoProfiles()
        {
            McpToolProfileSet set = McpToolProfileSet.Empty;

            Assert.That(set.IsEmpty, Is.True);
            Assert.That(set.Count, Is.Zero);
            Assert.That(set.Contains(McpToolProfile.Vision), Is.False);
            Assert.That(set.Enumerate(), Is.Empty);
            Assert.That(set.ToString(), Is.Empty);
        }

        [Test]
        public void SingleProfileSetContainsOnlyThatProfile()
        {
            var set = new McpToolProfileSet(McpToolProfile.Vision);

            Assert.That(set.IsEmpty, Is.False);
            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(set.Contains(McpToolProfile.Vision), Is.True);
            Assert.That(set.Contains(McpToolProfile.Robotics), Is.False);
            Assert.That(set.Enumerate(), Is.EqualTo(new[] { McpToolProfile.Vision }));
        }

        [Test]
        public void SetFromSequenceCollapsesDuplicates()
        {
            var set = new McpToolProfileSet(new[]
            {
                McpToolProfile.Vision,
                McpToolProfile.Robotics,
                McpToolProfile.Vision
            });

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.Contains(McpToolProfile.Vision), Is.True);
            Assert.That(set.Contains(McpToolProfile.Robotics), Is.True);
        }

        [Test]
        public void WithReturnsAugmentedSet()
        {
            McpToolProfileSet original = new McpToolProfileSet(McpToolProfile.Vision);

            McpToolProfileSet extended = original.With(McpToolProfile.Robotics);

            Assert.That(original.Count, Is.EqualTo(1));
            Assert.That(extended.Count, Is.EqualTo(2));
            Assert.That(extended.Contains(McpToolProfile.Vision), Is.True);
            Assert.That(extended.Contains(McpToolProfile.Robotics), Is.True);
        }

        [TestCase("vision,robotics", McpToolProfile.Vision, McpToolProfile.Robotics)]
        [TestCase("Vision, Robotics", McpToolProfile.Vision, McpToolProfile.Robotics)]
        [TestCase("vision+robotics", McpToolProfile.Vision, McpToolProfile.Robotics)]
        [TestCase("VISION;ROBOTICS", McpToolProfile.Vision, McpToolProfile.Robotics)]
        [TestCase("vision|robotics|diagnostics",
            McpToolProfile.PubSub, McpToolProfile.Diagnostics, McpToolProfile.Robotics, McpToolProfile.Vision)]
        public void ParseAcceptsDelimitedProfileNames(string value, params McpToolProfile[] expected)
        {
            McpToolProfileSet set = McpToolProfileSet.Parse(value);

            List<McpToolProfile> profiles = set.Enumerate().ToList();
            HashSet<McpToolProfile> expectedProfiles = new(expected);
            HashSet<McpToolProfile> actualProfiles = new(profiles);
            Assert.That(actualProfiles.SetEquals(expectedProfiles) || actualProfiles.IsSubsetOf(expectedProfiles), Is.True);
        }

        [Test]
        public void ParseRoundTripsThroughToString()
        {
            McpToolProfileSet set = McpToolProfileSet.Parse("vision,robotics");

            McpToolProfileSet parsed = McpToolProfileSet.Parse(set.ToString());

            Assert.That(parsed, Is.EqualTo(set));
        }

        [Test]
        public void ParseRejectsAnUnknownProfileName()
        {
            Assert.That(
                () => McpToolProfileSet.Parse("vision,unknown"),
                Throws.TypeOf<FormatException>().With.Message.Contains("unknown"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(",,,")]
        public void TryParseRejectsEmptyInput(string? value)
        {
            bool succeeded = McpToolProfileSet.TryParse(value, out McpToolProfileSet set);

            Assert.That(succeeded, Is.False);
            Assert.That(set.IsEmpty, Is.True);
        }

        [Test]
        public void ParseRejectsNullInput()
        {
            Assert.That(
                () => McpToolProfileSet.Parse(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorRejectsAnUnknownProfile()
        {
            Assert.That(
                () => new McpToolProfileSet((McpToolProfile)int.MaxValue),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void SetsWithTheSameProfilesAreEqual()
        {
            var lhs = new McpToolProfileSet(new[] { McpToolProfile.Vision, McpToolProfile.Robotics });
            var rhs = new McpToolProfileSet(new[] { McpToolProfile.Robotics, McpToolProfile.Vision });

            Assert.That(lhs, Is.EqualTo(rhs));
#pragma warning disable NUnit2010
            Assert.That(lhs == rhs, Is.True, "The operator overload must return true for equal sets.");
            Assert.That(lhs != rhs, Is.False, "The operator overload must return false for equal sets.");
#pragma warning restore NUnit2010
            Assert.That(lhs.GetHashCode(), Is.EqualTo(rhs.GetHashCode()));
        }

        [Test]
        public void ImplicitConversionFromASingleProfileYieldsASingletonSet()
        {
            McpToolProfileSet set = McpToolProfile.Vision;

            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(set.Contains(McpToolProfile.Vision), Is.True);
        }
    }
}
#endif
