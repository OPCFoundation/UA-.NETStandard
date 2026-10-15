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
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins the expanded profile inventory and the public value semantics of profile sets.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpExpandedToolProfileSetTests
    {
        private const string kEmptyListError = "The tool profile list is empty.";
        private const string kHighPairDisplay = "pubsub-security,wot-runtime";
        private const string kHighTripleDisplay = "Core,pubsub-security,wot-runtime";

        /// <summary>
        /// These literals are the independent oracle, not a reflection of the current enum.
        /// </summary>
        private static readonly (McpToolProfile Profile, string Name, int Ordinal, string Display)[] s_profileOracle =
        [
            (McpToolProfile.Core, "Core", 0, "Core"),
            (McpToolProfile.Services, "Services", 1, "Services"),
            (McpToolProfile.Administration, "Administration", 2, "Administration"),
            (McpToolProfile.PubSub, "PubSub", 3, "PubSub"),
            (McpToolProfile.Diagnostics, "Diagnostics", 4, "Diagnostics"),
            (McpToolProfile.Robotics, "Robotics", 5, "Robotics"),
            (McpToolProfile.Vision, "Vision", 6, "Vision"),
            (McpToolProfile.Full, "Full", 7, "Full"),
            (McpToolProfile.Amb, "Amb", 8, "Amb"),
            (McpToolProfile.Machinery, "Machinery", 9, "Machinery"),
            (McpToolProfile.Scales, "Scales", 10, "Scales"),
            (McpToolProfile.Pumps, "Pumps", 11, "Pumps"),
            (McpToolProfile.Di, "Di", 12, "Di"),
            (McpToolProfile.Isa95, "Isa95", 13, "Isa95"),
            (McpToolProfile.Positioning, "Positioning", 14, "Positioning"),
            (McpToolProfile.Wot, "Wot", 15, "Wot"),
            (McpToolProfile.XRegistry, "XRegistry", 16, "XRegistry"),
            (McpToolProfile.WotDocuments, "WotDocuments", 17, "wot-documents"),
            (McpToolProfile.Gds, "Gds", 18, "Gds"),
            (McpToolProfile.Certificates, "Certificates", 19, "Certificates"),
            (McpToolProfile.Credentials, "Credentials", 20, "Credentials"),
            (McpToolProfile.Onboarding, "Onboarding", 21, "Onboarding"),
            (McpToolProfile.Roles, "Roles", 22, "Roles"),
            (McpToolProfile.Users, "Users", 23, "Users"),
            (McpToolProfile.Files, "Files", 24, "Files"),
            (McpToolProfile.Alarms, "Alarms", 25, "Alarms"),
            (McpToolProfile.History, "History", 26, "History"),
            (McpToolProfile.Schemas, "Schemas", 27, "Schemas"),
            (McpToolProfile.PubSubRuntime, "PubSubRuntime", 28, "pubsub-runtime"),
            (McpToolProfile.PubSubConfiguration, "PubSubConfiguration", 29, "pubsub-configuration"),
            (McpToolProfile.PubSubBridge, "PubSubBridge", 30, "pubsub-bridge"),
            (McpToolProfile.PubSubSecurity, "PubSubSecurity", 31, "pubsub-security"),
            (McpToolProfile.WotRuntime, "WotRuntime", 32, "wot-runtime"),
            (McpToolProfile.MachineTool, "MachineTool", 33, "MachineTool"),
            (McpToolProfile.Gms, "Gms", 34, "Gms"),
            (McpToolProfile.CranesHoists, "CranesHoists", 35, "CranesHoists")
        ];

        private static readonly McpToolProfile[] s_highPair =
        [
            McpToolProfile.PubSubSecurity,
            McpToolProfile.WotRuntime
        ];

        private static readonly McpToolProfile[] s_highTriple =
        [
            McpToolProfile.Core,
            McpToolProfile.PubSubSecurity,
            McpToolProfile.WotRuntime
        ];

        private static readonly (McpToolProfile Profile, string Text)[] s_undefinedProfiles =
        [
            ((McpToolProfile)(-1), "-1"),
            ((McpToolProfile)36, "36"),
            ((McpToolProfile)63, "63"),
            ((McpToolProfile)64, "64"),
            ((McpToolProfile)int.MinValue, "-2147483648"),
            ((McpToolProfile)int.MaxValue, "2147483647")
        ];

        [Test]
        public void EnumMembersRetainExactNamesAndOrdinals()
        {
            string[] actualNames = Enum.GetNames<McpToolProfile>();
            McpToolProfile[] actualProfiles = Enum.GetValues<McpToolProfile>();

            Assert.That(actualNames, Has.Length.EqualTo(36));
            Assert.That(actualProfiles, Has.Length.EqualTo(36));
            Assert.That(actualNames, Is.EqualTo(s_profileOracle.Select(row => row.Name)));
            Assert.That(actualProfiles, Is.EqualTo(s_profileOracle.Select(row => row.Profile)));
            Assert.That(
                actualProfiles.Select(profile => (int)profile),
                Is.EqualTo(s_profileOracle.Select(row => row.Ordinal)));
        }

        [TestCaseSource(nameof(EmptyCases))]
        public void EmptyRepresentationsHaveExactValueSemantics(McpToolProfileSet set)
        {
            AssertSet(set, [], string.Empty);
            Assert.That(set, Is.EqualTo(McpToolProfileSet.Empty));
        }

        [Test]
        public void DefaultEnumAndDefaultSetRemainDistinct()
        {
            const McpToolProfile profile = default;
            McpToolProfileSet empty = default;
            var constructed = new McpToolProfileSet(profile);
            McpToolProfileSet converted = profile;

            Assert.That(profile, Is.EqualTo(McpToolProfile.Core));
            Assert.That((int)profile, Is.Zero);
            AssertSet(empty, [], string.Empty);
            AssertSet(constructed, [McpToolProfile.Core], "Core");
            AssertSet(converted, [McpToolProfile.Core], "Core");
            Assert.That(empty, Is.Not.EqualTo(constructed));
            Assert.That(empty, Is.Not.EqualTo(converted));
        }

        [TestCaseSource(nameof(SingletonCases))]
        public void SingletonsHaveExactMembershipEnumerationAndDisplay(
            McpToolProfile profile,
            string expectedDisplay)
        {
            var scalar = new McpToolProfileSet(profile);
            var sequence = new McpToolProfileSet([profile]);
            McpToolProfileSet converted = profile;
            McpToolProfileSet explicitConversion = McpToolProfileSet.FromMcpToolProfile(profile);

            AssertSet(scalar, [profile], expectedDisplay);
            AssertSet(sequence, [profile], expectedDisplay);
            AssertSet(converted, [profile], expectedDisplay);
            AssertSet(explicitConversion, [profile], expectedDisplay);
            Assert.That(sequence, Is.EqualTo(scalar));
            Assert.That(converted, Is.EqualTo(scalar));
            Assert.That(explicitConversion, Is.EqualTo(scalar));
        }

        [TestCaseSource(nameof(HighProfileCases))]
        public void HighProfilesRemainIndependentAcrossConstructionParsingAndWith(
            McpToolProfile[] expected,
            string input,
            string expectedDisplay)
        {
            var constructed = new McpToolProfileSet(expected.Reverse());
            McpToolProfileSet extended = CreateWithProfiles(expected.Reverse());
            McpToolProfileSet parsed = AssertSuccessfulParse(input, expected, expectedDisplay);

            AssertSet(constructed, expected, expectedDisplay);
            AssertSet(extended, expected, expectedDisplay);
            Assert.That(parsed, Is.EqualTo(constructed));
            Assert.That(extended, Is.EqualTo(constructed));
            Assert.That(McpToolProfileSet.Parse(constructed.ToString()), Is.EqualTo(constructed));
        }

        [TestCaseSource(nameof(AllProfileInputs))]
        public void AllProfilesFormExactDeduplicatedThirtySixMemberUnion(McpToolProfile[] input)
        {
            McpToolProfile[] expected = GetAllProfiles();
            string expectedDisplay = GetExpectedDisplay(expected);
            var set = new McpToolProfileSet(input);

            AssertSet(set, expected, expectedDisplay);
            Assert.That(set.Count, Is.EqualTo(36));
            Assert.That(set.Enumerate().Count(profile => profile == McpToolProfile.Full), Is.EqualTo(1));
            foreach ((McpToolProfile Profile, string Text) in s_undefinedProfiles)
            {
                Assert.That(set.Contains(Profile), Is.False);
            }

            McpToolProfileSet parsed = AssertSuccessfulParse(set.ToString(), expected, expectedDisplay);
            Assert.That(parsed, Is.EqualTo(set));
        }

        [Test]
        public void FullRemainsOneOrdinarySetMember()
        {
            var full = new McpToolProfileSet(McpToolProfile.Full);
            var pair = new McpToolProfileSet(
            [
                McpToolProfile.Full,
                McpToolProfile.Core,
                McpToolProfile.Full
            ]);
            McpToolProfile[] allProfiles = GetAllProfiles();
            var union = new McpToolProfileSet(allProfiles);

            AssertSet(full, [McpToolProfile.Full], "Full");
            AssertSet(pair, [McpToolProfile.Core, McpToolProfile.Full], "Core,Full");
            AssertSet(union, allProfiles, GetExpectedDisplay(allProfiles));
            Assert.That(full, Is.Not.EqualTo(McpToolProfileSet.Empty));
            Assert.That(full, Is.Not.EqualTo(pair));
            Assert.That(full, Is.Not.EqualTo(union));
        }

        [TestCaseSource(nameof(WithAdditionCases))]
        public void WithAddsOnlyRequestedMemberWithoutMutatingReceiver(
            McpToolProfile[] originalProfiles,
            McpToolProfile addedProfile,
            McpToolProfile[] expected)
        {
            var original = new McpToolProfileSet(originalProfiles.Reverse());
            McpToolProfileSet snapshot = original;
            string originalDisplay = GetExpectedDisplay(originalProfiles);
            AssertSet(original, originalProfiles, originalDisplay);

            McpToolProfileSet extended = original.With(addedProfile);

            AssertSet(original, originalProfiles, originalDisplay);
            Assert.That(original, Is.EqualTo(snapshot));
            Assert.That(original.Contains(addedProfile), Is.False);
            AssertSet(extended, expected, GetExpectedDisplay(expected));
            Assert.That(extended.Count, Is.EqualTo(originalProfiles.Length + 1));
            Assert.That(extended, Is.Not.EqualTo(original));
        }

        [TestCaseSource(nameof(WithExistingCases))]
        public void WithExistingMembersIsIdempotent(McpToolProfile[] expected, McpToolProfile existingProfile)
        {
            var original = new McpToolProfileSet(expected.Reverse().Concat(expected));
            string expectedDisplay = GetExpectedDisplay(expected);

            McpToolProfileSet extended = original.With(existingProfile);

            AssertSet(original, expected, expectedDisplay);
            AssertSet(extended, expected, expectedDisplay);
            Assert.That(extended, Is.EqualTo(original));
            Assert.That(extended.Count, Is.EqualTo(original.Count));
        }

        [TestCaseSource(nameof(OrderingCases))]
        public void EnumerationAndDisplayFollowDeclarationOrder(
            McpToolProfile[] input,
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            var set = new McpToolProfileSet(input);

            AssertSet(set, expected, expectedDisplay);
            Assert.That(set.Enumerate().ToArray(), Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(NamedParsingCases))]
        public void ParsingAcceptsCaseInsensitiveNamesAndCanonicalTokens(
            string input,
            McpToolProfile profile,
            string expectedDisplay)
        {
            AssertSuccessfulParse(input, [profile], expectedDisplay);
        }

        [TestCaseSource(nameof(DelimiterCases))]
        public void ParsingAcceptsEveryDelimiterAndIgnoresEmptyTokens(string input)
        {
            AssertSuccessfulParse(input, s_highTriple, kHighTripleDisplay);
        }

        [TestCaseSource(nameof(NumericParsingCases))]
        public void ParsingAcceptsDefinedNumericTokens(
            string input,
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            AssertSuccessfulParse(input, expected, expectedDisplay);
        }

        [TestCaseSource(nameof(RoundTripCases))]
        public void NonEmptySetsRoundTripThroughCanonicalDisplay(
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            var original = new McpToolProfileSet(expected.Reverse().Concat(expected));
            AssertSet(original, expected, expectedDisplay);
            Assert.That(original.IsEmpty, Is.False);

            McpToolProfileSet parsed = AssertSuccessfulParse(original.ToString(), expected, expectedDisplay);

            AssertEqualValues(original, parsed);
        }

        [Test]
        public void ParseRejectsNullInput()
        {
            Assert.That(
                () => McpToolProfileSet.Parse(null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("value"));

            string error = AssertTryParseFailure(null);

            Assert.That(error, Is.EqualTo(kEmptyListError));
        }

        [TestCaseSource(nameof(EmptyTextCases))]
        public void EmptyAndDelimiterOnlyTextIsRejectedWithoutAnEmptyRoundTrip(string input)
        {
            AssertSet(McpToolProfileSet.Empty, [], string.Empty);
            Assert.That(
                () => McpToolProfileSet.Parse(input),
                Throws.TypeOf<FormatException>().With.Message.EqualTo(kEmptyListError));

            string error = AssertTryParseFailure(input);

            Assert.That(error, Is.EqualTo(kEmptyListError));
        }

        [TestCaseSource(nameof(InvalidTextCases))]
        public void InvalidTextNeverReturnsAPartialSet(string input, string rejectedToken)
        {
            string validChoices = string.Join(", ", s_profileOracle.Select(row => row.Display));

            string error = AssertTryParseFailure(input);

            Assert.That(error, Does.Contain(rejectedToken));
            Assert.That(error, Does.Contain(validChoices));
            Assert.That(
                () => McpToolProfileSet.Parse(input),
                Throws.TypeOf<FormatException>().With.Message.EqualTo(error));
        }

        [TestCaseSource(nameof(UndefinedProfileCases))]
        public void UndefinedValuesAreRejectedByConstructorsConversionAndWith(McpToolProfile invalid)
        {
            AssertInvalidProfile(() => _ = new McpToolProfileSet(invalid), invalid);
            AssertInvalidProfile(() => _ = McpToolProfileSet.FromMcpToolProfile(invalid), invalid);
            McpToolProfile[][] sequences =
            [
                [invalid],
                [invalid, McpToolProfile.Core, McpToolProfile.PubSubSecurity, McpToolProfile.WotRuntime],
                [McpToolProfile.Core, McpToolProfile.PubSubSecurity, invalid, McpToolProfile.WotRuntime],
                [McpToolProfile.Core, McpToolProfile.PubSubSecurity, McpToolProfile.WotRuntime, invalid]
            ];
            foreach (McpToolProfile[] sequence in sequences)
            {
                AssertInvalidProfile(() => _ = new McpToolProfileSet(sequence), invalid);
            }
            AssertInvalidProfile(() => ConvertProfile(invalid), invalid);
            AssertInvalidProfile(() => McpToolProfileSet.Empty.With(invalid), invalid);

            var original = new McpToolProfileSet(s_highTriple);
            McpToolProfileSet snapshot = original;

            AssertInvalidProfile(() => original.With(invalid), invalid);

            AssertSet(original, s_highTriple, kHighTripleDisplay);
            Assert.That(original, Is.EqualTo(snapshot));
            AssertSet(McpToolProfileSet.Empty, [], string.Empty);
        }

        [Test]
        public void NullProfileSequenceIsRejected()
        {
            IEnumerable<McpToolProfile> profiles = null!;

            Assert.That(
                () => new McpToolProfileSet(profiles),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("profiles"));
            AssertSet(new McpToolProfileSet([]), [], string.Empty);
        }

        [TestCaseSource(nameof(InvalidMembershipCases))]
        public void InvalidContainsReturnsFalseWithoutWrapping(McpToolProfile[] expected, McpToolProfile invalid)
        {
            var set = new McpToolProfileSet(expected);

            Assert.That(set.Contains(invalid), Is.False);

            AssertSet(set, expected, GetExpectedDisplay(expected));
        }

        [TestCaseSource(nameof(EqualSetCases))]
        public void EqualSetsAgreeAcrossTypedObjectOperatorsAndHash(
            McpToolProfileSet first,
            McpToolProfileSet second,
            McpToolProfileSet third,
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            McpToolProfileSet[] representations = [first, second, third];
            foreach (McpToolProfileSet left in representations)
            {
                AssertSet(left, expected, expectedDisplay);
                foreach (McpToolProfileSet right in representations)
                {
                    AssertEqualValues(left, right);
                }
            }
        }

        [TestCaseSource(nameof(UnequalSetCases))]
        public void DifferentSetsRemainUnequalAcrossTypedObjectAndOperators(
            McpToolProfile[] leftProfiles,
            McpToolProfile[] rightProfiles)
        {
            var left = new McpToolProfileSet(leftProfiles);
            var right = new McpToolProfileSet(rightProfiles);

            AssertSet(left, leftProfiles, GetExpectedDisplay(leftProfiles));
            AssertSet(right, rightProfiles, GetExpectedDisplay(rightProfiles));
            AssertUnequalValues(left, right);
            AssertUnequalValues(right, left);
        }

        [TestCaseSource(nameof(ObjectEqualityCases))]
        public void ObjectEqualityRejectsNullAndUnrelatedTypes(McpToolProfile[] expected)
        {
            var set = new McpToolProfileSet(expected);
            string expectedDisplay = GetExpectedDisplay(expected);
            object?[] unrelated =
            [
                null,
                new object(),
                expectedDisplay,
                McpToolProfile.Core,
                McpToolProfile.PubSubSecurity,
                McpToolProfile.WotRuntime
            ];

            foreach (object? value in unrelated)
            {
                bool equals = set.Equals(value);

                Assert.That(equals, Is.False);
            }

            bool boxedSetEquals = set.Equals((object)new McpToolProfileSet(expected.Reverse()));
            Assert.That(boxedSetEquals, Is.True);
            AssertSet(set, expected, expectedDisplay);
        }

        private static void AssertSet(
            McpToolProfileSet actual,
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            Assert.That(actual.IsEmpty, Is.EqualTo(expected.Length == 0));
            Assert.That(actual.Count, Is.EqualTo(expected.Length));
            Assert.That(actual.Enumerate(), Is.EqualTo(expected));
            Assert.That(actual.ToString(), Is.EqualTo(expectedDisplay));

            foreach ((McpToolProfile Profile, string Name, _, _) in s_profileOracle)
            {
                Assert.That(
                    actual.Contains(Profile),
                    Is.EqualTo(expected.Contains(Profile)),
                    $"Membership of {Name} must match the literal oracle.");
            }
        }

        private static McpToolProfileSet AssertSuccessfulParse(
            string input,
            McpToolProfile[] expected,
            string expectedDisplay)
        {
            var parsed = McpToolProfileSet.Parse(input);
            bool success = McpToolProfileSet.TryParse(input, out McpToolProfileSet simple);
            bool successWithError = McpToolProfileSet.TryParse(
                input,
                out McpToolProfileSet detailed,
                out string? error);

            AssertSet(parsed, expected, expectedDisplay);
            Assert.That(success, Is.True);
            AssertSet(simple, expected, expectedDisplay);
            Assert.That(successWithError, Is.True);
            Assert.That(error, Is.Null);
            AssertSet(detailed, expected, expectedDisplay);
            Assert.That(simple, Is.EqualTo(parsed));
            Assert.That(detailed, Is.EqualTo(parsed));
            return parsed;
        }

        private static string AssertTryParseFailure(string? input)
        {
            bool success = McpToolProfileSet.TryParse(input, out McpToolProfileSet simple);
            bool successWithError = McpToolProfileSet.TryParse(
                input,
                out McpToolProfileSet detailed,
                out string? error);

            Assert.That(success, Is.False);
            AssertSet(simple, [], string.Empty);
            Assert.That(successWithError, Is.False);
            AssertSet(detailed, [], string.Empty);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            return error!;
        }

        private static void AssertInvalidProfile(Action action, McpToolProfile invalid)
        {
            Assert.That(
                action,
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("profile")
                    .And.Property(nameof(ArgumentOutOfRangeException.ActualValue)).EqualTo(invalid));
        }

        private static void AssertEqualValues(McpToolProfileSet left, McpToolProfileSet right)
        {
            bool typedEquals = left.Equals(right);
            bool objectEquals = left.Equals((object)right);
            Assert.That(typedEquals, Is.True);
            Assert.That(objectEquals, Is.True);
#pragma warning disable NUnit2010 // Assert the operators themselves, not a replacement equality constraint.
            Assert.That(left == right, Is.True);
            Assert.That(left != right, Is.False);
#pragma warning restore NUnit2010
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        private static void AssertUnequalValues(McpToolProfileSet left, McpToolProfileSet right)
        {
            bool typedEquals = left.Equals(right);
            bool objectEquals = left.Equals((object)right);
            Assert.That(typedEquals, Is.False);
            Assert.That(objectEquals, Is.False);
#pragma warning disable NUnit2010 // Assert the operators themselves, without making assumptions about hash collisions.
            Assert.That(left == right, Is.False);
            Assert.That(left != right, Is.True);
#pragma warning restore NUnit2010
        }

        private static McpToolProfileSet ConvertProfile(McpToolProfile profile)
        {
            return profile;
        }

        private static McpToolProfileSet CreateWithProfiles(IEnumerable<McpToolProfile> profiles)
        {
            McpToolProfileSet set = McpToolProfileSet.Empty;
            foreach (McpToolProfile profile in profiles)
            {
                set = set.With(profile);
            }
            return set;
        }

        private static McpToolProfile[] GetAllProfiles()
        {
            return [.. s_profileOracle.Select(row => row.Profile)];
        }

        private static string GetExpectedDisplay(IEnumerable<McpToolProfile> profiles)
        {
            return string.Join(",", profiles.Select(profile => s_profileOracle.Single(row => row.Profile == profile)
                .Display));
        }

        private static IEnumerable<string> GetCaseVariants(string value)
        {
            string mixedCase = new([.. value.Select((character, index) => index % 2 == 0
                ? char.ToUpperInvariant(character)
                : char.ToLowerInvariant(character))]);
            string[] variants = [value, value.ToUpperInvariant(), value.ToLowerInvariant(), mixedCase];
            return variants.Distinct(StringComparer.Ordinal);
        }

        private static IEnumerable<TestCaseData> EmptyCases()
        {
            yield return new TestCaseData(default(McpToolProfileSet)).SetName("{m}(Default)");
            yield return new TestCaseData(McpToolProfileSet.Empty).SetName("{m}(EmptyProperty)");
            yield return new TestCaseData(new McpToolProfileSet([]))
                .SetName("{m}(EmptySequence)");
        }

        private static IEnumerable<TestCaseData> SingletonCases()
        {
            foreach ((McpToolProfile Profile, _, _, string Display) in s_profileOracle)
            {
                yield return new TestCaseData(Profile, Display);
            }
        }

        private static IEnumerable<TestCaseData> HighProfileCases()
        {
            yield return new TestCaseData(new[] { McpToolProfile.Core }, "core", "Core");
            yield return new TestCaseData(new[] { McpToolProfile.PubSubSecurity }, "PUBSUBSECURITY", "pubsub-security");
            yield return new TestCaseData(new[] { McpToolProfile.WotRuntime }, "wot-runtime", "wot-runtime");
            yield return new TestCaseData(s_highPair, "WotRuntime+pubsub-security+WotRuntime", kHighPairDisplay);
            yield return new TestCaseData(s_highTriple, "wot-runtime+Core+PUBSUBSECURITY", kHighTripleDisplay);
            yield return new TestCaseData(
                new[]
                {
                    McpToolProfile.Services,
                    McpToolProfile.Administration,
                    McpToolProfile.PubSub,
                    McpToolProfile.MachineTool,
                    McpToolProfile.Gms,
                    McpToolProfile.CranesHoists
                },
                "CranesHoists+Services+Gms+Administration+MachineTool+PubSub",
                "Services,Administration,PubSub,MachineTool,Gms,CranesHoists");
        }

        private static IEnumerable<TestCaseData> AllProfileInputs()
        {
            McpToolProfile[] profiles = GetAllProfiles();
            yield return new TestCaseData(profiles).SetName("{m}(DeclarationOrder)");
            yield return new TestCaseData(profiles.Reverse().ToArray()).SetName("{m}(ReverseOrder)");
            yield return new TestCaseData(profiles.Reverse().Concat(profiles).Concat(profiles.Reverse()).ToArray())
                .SetName("{m}(RepeatedReverseOrder)");
        }

        private static IEnumerable<TestCaseData> WithAdditionCases()
        {
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), McpToolProfile.Core, new[] { McpToolProfile.Core });
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), McpToolProfile.PubSubSecurity, new[] { McpToolProfile.PubSubSecurity });
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), McpToolProfile.WotRuntime, new[] { McpToolProfile.WotRuntime });
            yield return new TestCaseData(
                new[] { McpToolProfile.Core },
                McpToolProfile.PubSubSecurity,
                new[] { McpToolProfile.Core, McpToolProfile.PubSubSecurity });
            yield return new TestCaseData(
                new[] { McpToolProfile.Core },
                McpToolProfile.WotRuntime,
                new[] { McpToolProfile.Core, McpToolProfile.WotRuntime });
            yield return new TestCaseData(
                new[] { McpToolProfile.PubSubSecurity }, McpToolProfile.WotRuntime, s_highPair);
            yield return new TestCaseData(
                new[] { McpToolProfile.PubSubSecurity },
                McpToolProfile.Core,
                new[] { McpToolProfile.Core, McpToolProfile.PubSubSecurity });
            yield return new TestCaseData(
                new[] { McpToolProfile.WotRuntime }, McpToolProfile.PubSubSecurity, s_highPair);
            yield return new TestCaseData(
                new[] { McpToolProfile.WotRuntime },
                McpToolProfile.Core,
                new[] { McpToolProfile.Core, McpToolProfile.WotRuntime });
            yield return new TestCaseData(
                new[] { McpToolProfile.Vision, McpToolProfile.Di },
                McpToolProfile.Gds,
                new[] { McpToolProfile.Vision, McpToolProfile.Di, McpToolProfile.Gds });
            yield return new TestCaseData(
                new[] { McpToolProfile.Full },
                McpToolProfile.WotRuntime,
                new[] { McpToolProfile.Full, McpToolProfile.WotRuntime });
            yield return new TestCaseData(s_highPair, McpToolProfile.Core, s_highTriple);
        }

        private static IEnumerable<TestCaseData> WithExistingCases()
        {
            foreach ((McpToolProfile Profile, string Name, _, _) in s_profileOracle)
            {
                yield return new TestCaseData(new[] { Profile }, Profile)
                    .SetName("{m}(Singleton" + Name + ")");
            }
            foreach (McpToolProfile profile in s_highPair)
            {
                yield return new TestCaseData(s_highPair, profile).SetName("{m}(HighPair" + profile + ")");
            }
            foreach (McpToolProfile profile in s_highTriple)
            {
                yield return new TestCaseData(s_highTriple, profile).SetName("{m}(HighTriple" + profile + ")");
            }
            foreach ((McpToolProfile Profile, string Name, int Ordinal, string Display) in s_profileOracle)
            {
                yield return new TestCaseData(GetAllProfiles(), Profile)
                    .SetName("{m}(CompleteUnion" + Name + ")");
            }
        }

        private static IEnumerable<TestCaseData> OrderingCases()
        {
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), Array.Empty<McpToolProfile>(), string.Empty).SetName("{m}(Empty)");
            yield return new TestCaseData(
                new[] { McpToolProfile.WotDocuments },
                new[] { McpToolProfile.WotDocuments },
                "wot-documents").SetName("{m}(Singleton)");
            yield return new TestCaseData(
                new[] { McpToolProfile.WotRuntime, McpToolProfile.PubSubSecurity, McpToolProfile.WotRuntime },
                s_highPair,
                kHighPairDisplay).SetName("{m}(HighPair)");
            yield return new TestCaseData(
                new[]
                {
                    McpToolProfile.WotRuntime, McpToolProfile.Gds, McpToolProfile.Di,
                    McpToolProfile.Full, McpToolProfile.Services, McpToolProfile.Gds,
                    McpToolProfile.Full, McpToolProfile.WotRuntime
                },
                new[]
                {
                    McpToolProfile.Services, McpToolProfile.Full, McpToolProfile.Di,
                    McpToolProfile.Gds, McpToolProfile.WotRuntime
                },
                "Services,Full,Di,Gds,wot-runtime").SetName("{m}(ReversedRepeatedMixedMembers)");
            yield return new TestCaseData(
                new[]
                {
                    McpToolProfile.XRegistry, McpToolProfile.Isa95, McpToolProfile.Di,
                    McpToolProfile.Amb, McpToolProfile.Vision, McpToolProfile.Core, McpToolProfile.Vision
                },
                new[]
                {
                    McpToolProfile.Core, McpToolProfile.Vision, McpToolProfile.Amb,
                    McpToolProfile.Di, McpToolProfile.Isa95, McpToolProfile.XRegistry
                },
                "Core,Vision,Amb,Di,Isa95,XRegistry").SetName("{m}(HistoricalCapitalization)");
        }

        private static IEnumerable<TestCaseData> NamedParsingCases()
        {
            foreach ((McpToolProfile Profile, string Name, int Ordinal, string Display) in s_profileOracle)
            {
                foreach (string input in GetCaseVariants(Name))
                {
                    yield return new TestCaseData(input, Profile, Display);
                }
                if (Name != Display)
                {
                    foreach (string input in GetCaseVariants(Display))
                    {
                        yield return new TestCaseData(input, Profile, Display);
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> DelimiterCases()
        {
            foreach (string delimiter in (string[])[",", "+", ";", "|", " ", "\t"])
            {
                yield return new TestCaseData(
                    $"{delimiter}Core{delimiter}{delimiter}PubSubSecurity{delimiter}WotRuntime{delimiter}");
            }
            yield return new TestCaseData("Core+PubSubSecurity;WotRuntime");
            yield return new TestCaseData(
                " \t||WotRuntime; ;Core++pubsub-security,\tCORE|PUBSUBSECURITY|wot-runtime,,\t ");
            yield return new TestCaseData("\r\nCore\r\n + \r\nPUBSUBSECURITY\r\n;\r\nwot-runtime\r\n");
        }

        private static IEnumerable<TestCaseData> NumericParsingCases()
        {
            foreach ((McpToolProfile Profile, _, int Ordinal, string Display) in s_profileOracle)
            {
                yield return new TestCaseData(
                    Ordinal.ToString(CultureInfo.InvariantCulture),
                    new[] { Profile },
                    Display);
            }
            yield return new TestCaseData("0+PubSubSecurity+32+0", s_highTriple, kHighTripleDisplay);
        }

        private static IEnumerable<TestCaseData> RoundTripCases()
        {
            foreach ((McpToolProfile Profile, string Name, _, string Display) in s_profileOracle)
            {
                yield return new TestCaseData(new[] { Profile }, Display).SetName("{m}(" + Name + ")");
            }
            yield return new TestCaseData(s_highPair, kHighPairDisplay).SetName("{m}(HighPair)");
            yield return new TestCaseData(s_highTriple, kHighTripleDisplay).SetName("{m}(HighTriple)");
            yield return new TestCaseData(
                new[]
                {
                    McpToolProfile.Services, McpToolProfile.Di, McpToolProfile.Gds,
                    McpToolProfile.Users, McpToolProfile.PubSubConfiguration
                },
                "Services,Di,Gds,Users,pubsub-configuration").SetName("{m}(MixedInterior)");
            McpToolProfile[] profiles = GetAllProfiles();
            yield return new TestCaseData(profiles, GetExpectedDisplay(profiles)).SetName("{m}(CompleteUnion)");
        }

        private static IEnumerable<TestCaseData> EmptyTextCases()
        {
            string[] inputs =
            [
                string.Empty, " ", "\t", "\r\n", " \t \r\n", ",", "+", ";", "|", ",+;| \t,;+", " \t|| , ;++\t "
            ];
            foreach (string input in inputs)
            {
                yield return new TestCaseData(input);
            }
        }

        private static IEnumerable<TestCaseData> InvalidTextCases()
        {
            yield return new TestCaseData("mystery", "mystery");
            yield return new TestCaseData("mystery+Core+PubSubSecurity+WotRuntime", "mystery");
            yield return new TestCaseData("Core+mystery+PubSubSecurity+WotRuntime", "mystery");
            yield return new TestCaseData("Core+PubSubSecurity+mystery+WotRuntime", "mystery");
            yield return new TestCaseData("Core+PubSubSecurity+WotRuntime+mystery", "mystery");
            yield return new TestCaseData("pubsub-unknown", "pubsub-unknown");
            yield return new TestCaseData("Core+PUBSUB-UNKNOWN+WotRuntime", "PUBSUB-UNKNOWN");

            foreach ((_, string Text) in s_undefinedProfiles)
            {
                yield return new TestCaseData(Text, Text);
                yield return new TestCaseData("Core+PubSubSecurity+WotRuntime+" + Text, Text);
            }
        }

        private static IEnumerable<TestCaseData> UndefinedProfileCases()
        {
            foreach ((McpToolProfile Profile, _) in s_undefinedProfiles)
            {
                yield return new TestCaseData(Profile);
            }
        }

        private static IEnumerable<(string Name, McpToolProfile[] Profiles)> GetRepresentativeSets()
        {
            return
            [
                ("Empty", []),
                ("Core", [McpToolProfile.Core]),
                ("PubSubSecurity", [McpToolProfile.PubSubSecurity]),
                ("WotRuntime", [McpToolProfile.WotRuntime]),
                ("HighPair", s_highPair),
                ("HighTriple", s_highTriple),
                ("CompleteUnion", GetAllProfiles())
            ];
        }

        private static IEnumerable<TestCaseData> InvalidMembershipCases()
        {
            foreach ((string Name, McpToolProfile[] Profiles) set in GetRepresentativeSets())
            {
                foreach ((McpToolProfile Profile, string Text) in s_undefinedProfiles)
                {
                    yield return new TestCaseData(set.Profiles, Profile)
                        .SetName("{m}(" + set.Name + "," + Text + ")");
                }
            }
        }

        private static IEnumerable<TestCaseData> EqualSetCases()
        {
            yield return new TestCaseData(
                default(McpToolProfileSet),
                McpToolProfileSet.Empty,
                new McpToolProfileSet([]),
                Array.Empty<McpToolProfile>(),
                string.Empty).SetName("{m}(Empty)");

            foreach ((McpToolProfile Profile, string Name, _, string Display) in s_profileOracle)
            {
                McpToolProfileSet converted = Profile;
                yield return new TestCaseData(
                    new McpToolProfileSet(Profile),
                    new McpToolProfileSet([Profile, Profile]),
                    converted,
                    new[] { Profile },
                    Display).SetName("{m}(" + Name + ")");
            }

            McpToolProfile[] mixed = [McpToolProfile.Vision, McpToolProfile.Di, McpToolProfile.Gds];
            yield return CreateEqualSetCase(mixed, "Vision,Di,Gds", "MixedInterior");
            yield return CreateEqualSetCase(s_highPair, kHighPairDisplay, "HighPair");
            yield return CreateEqualSetCase(s_highTriple, kHighTripleDisplay, "HighTriple");
            McpToolProfile[] allProfiles = GetAllProfiles();
            yield return CreateEqualSetCase(allProfiles, GetExpectedDisplay(allProfiles), "CompleteUnion");
        }

        private static TestCaseData CreateEqualSetCase(
            McpToolProfile[] profiles,
            string expectedDisplay,
            string name)
        {
            return new TestCaseData(
                new McpToolProfileSet(profiles),
                new McpToolProfileSet(profiles.Reverse().Concat(profiles)),
                CreateWithProfiles(profiles.Reverse()),
                profiles,
                expectedDisplay).SetName("{m}(" + name + ")");
        }

        private static IEnumerable<TestCaseData> UnequalSetCases()
        {
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), new[] { McpToolProfile.Core }).SetName("{m}(EmptyAndCore)");
            yield return new TestCaseData(
                Array.Empty<McpToolProfile>(), new[] { McpToolProfile.WotRuntime }).SetName("{m}(EmptyAndBit32)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Core },
                new[] { McpToolProfile.PubSubSecurity }).SetName("{m}(CoreAndBit31)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Core },
                new[] { McpToolProfile.WotRuntime }).SetName("{m}(CoreAndBit32)");
            yield return new TestCaseData(
                new[] { McpToolProfile.PubSubSecurity },
                new[] { McpToolProfile.WotRuntime }).SetName("{m}(Bit31AndBit32)");
            yield return new TestCaseData(s_highPair, s_highTriple).SetName("{m}(HighPairAndTriple)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Core, McpToolProfile.PubSubSecurity },
                s_highPair).SetName("{m}(EqualCountsCoreAndBit32)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Core, McpToolProfile.WotRuntime },
                s_highPair).SetName("{m}(EqualCountsCoreAndBit31)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Vision, McpToolProfile.Di },
                new[] { McpToolProfile.Vision, McpToolProfile.Di, McpToolProfile.Gds })
                .SetName("{m}(InteriorSubsetAndSuperset)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Core },
                new[] { McpToolProfile.Core, McpToolProfile.WotRuntime }).SetName("{m}(CoreAndCoreWithBit32)");
            yield return new TestCaseData(
                new[] { McpToolProfile.PubSubSecurity }, s_highPair).SetName("{m}(Bit31AndHighPair)");
            yield return new TestCaseData(
                new[] { McpToolProfile.Full }, GetAllProfiles()).SetName("{m}(FullAndCompleteUnion)");
        }

        private static IEnumerable<TestCaseData> ObjectEqualityCases()
        {
            foreach ((string Name, McpToolProfile[] Profiles) row in GetRepresentativeSets())
            {
                yield return new TestCaseData(row.Profiles).SetName("{m}(" + row.Name + ")");
            }
            yield return new TestCaseData(new[] { McpToolProfile.Full }).SetName("{m}(Full)");
        }
    }
}
#endif
