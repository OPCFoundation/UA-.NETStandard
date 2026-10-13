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
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Tests the reverse-authority identifier construction of xRegistry
    /// Section 6.9.
    /// </summary>
    [TestFixture]
    [Category("XRegistryIdentifier")]
    public sealed class XRegistryIdentifierTests
    {
        [Test]
        [TestCase("http://contoso.org/UA/Pumps/", "org.contoso.UA.Pumps")]
        [TestCase("http://opcfoundation.org/UA/", "org.opcfoundation.UA")]
        [TestCase("pump.usda", "pump.usda")]
        [TestCase("textures/albedo.png", "textures.albedo.png")]
        [TestCase("pkg.usdz[tex/a.png]", "pkg.usdz-tex.a.png")]
        [TestCase("urn:dev:ops:32473-pump-01", "urn.dev.ops.32473-pump-01")]
        [TestCase("https://contoso.org/things/pump-01", "org.contoso.things.pump-01")]
        public void PublishedExamplesConstructTheExpectedIdentifier(
            string sourceIdentity,
            string expected)
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity(sourceIdentity),
                Is.EqualTo(expected));
        }

        [Test]
        public void AuthorityLabelsAreReversedAndThePortBecomesAFurtherLabel()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("http://contoso.org:4840/UA/"),
                Is.EqualTo("org.contoso.4840.UA"));
        }

        [TestCase("https://contoso.org?x=1")]
        [TestCase("https://contoso.org#f")]
        [TestCase("https://contoso.org?next=/things#f")]
        public void AuthorityOnlyUriDiscardsQueryAndFragment(string identity)
        {
            Assert.That(XRegistryIdentifier.FromSourceIdentity(identity), Is.EqualTo("org.contoso"));
        }

        [Test]
        public void OccupiedDisambiguatorAdvancesToAnotherBoundedCandidate()
        {
            const string identity = "https://contoso.org/things";
            string readable = XRegistryIdentifier.FromSourceIdentity(identity);
            string occupied = readable + "." + XRegistryIdentifier.Disambiguator(identity);

            string assigned = XRegistryIdentifier.FromSourceIdentity(identity, [readable, occupied]);

            Assert.Multiple(() =>
            {
                Assert.That(assigned, Is.Not.EqualTo(readable));
                Assert.That(assigned, Is.Not.EqualTo(occupied));
                Assert.That(assigned, Has.Length.LessThanOrEqualTo(128));
            });
        }

        [TestCase(119, "")]
        [TestCase(120, "")]
        [TestCase(128, "")]
        [TestCase(129, "")]
        [TestCase(120, "td.")]
        [TestCase(128, "tm.")]
        [TestCase(200, "td.")]
        public void AllocationReservesEverySuffixAndBoundsTheFinalOrdinal(int length, string prefix)
        {
            string identity = new('a', length);
            var occupied = new List<string>();
            string readable = prefix + identity;
            if (readable.Length <= 128)
            {
                Assert.That(
                    XRegistryIdentifier.FromSourceIdentity(identity, occupied.ToArrayOf(), prefix, 2),
                    Is.EqualTo(readable));
                occupied.Add(readable.ToUpperInvariant());
            }

            string fullHash = string.Empty;
            foreach (int suffixLength in new[] { 8, 16, 32, 64 })
            {
                string candidate = XRegistryIdentifier.FromSourceIdentity(
                    identity, occupied.ToArrayOf(), prefix, 2);
                string suffix = candidate[(candidate.LastIndexOf('.') + 1)..];
                int budget = 128 - 1 - suffixLength;
                string expectedPrefix = readable[..Math.Min(readable.Length, budget)].TrimEnd('.', '-');
                Assert.Multiple(() =>
                {
                    Assert.That(candidate, Has.Length.LessThanOrEqualTo(128));
                    Assert.That(suffix, Has.Length.EqualTo(suffixLength));
                    Assert.That(suffix, Does.StartWith(XRegistryIdentifier.Disambiguator(identity)));
                    Assert.That(candidate, Is.EqualTo(expectedPrefix + "." + suffix));
                    Assert.That(occupied, Does.Not.Contain(candidate));
                });
                occupied.Add(candidate.ToUpperInvariant());
                fullHash = suffix;
            }
            foreach (uint ordinal in new uint[] { 1, 2 })
            {
                string candidate = XRegistryIdentifier.FromSourceIdentity(
                    identity, occupied.ToArrayOf(), prefix, 2);
                Assert.Multiple(() =>
                {
                    Assert.That(candidate, Has.Length.LessThanOrEqualTo(128));
                    Assert.That(candidate, Does.EndWith("." + fullHash + "." + ordinal));
                    Assert.That(occupied, Does.Not.Contain(candidate));
                });
                occupied.Add(candidate.ToUpperInvariant());
            }
            Assert.Throws<InvalidOperationException>(() =>
                XRegistryIdentifier.FromSourceIdentity(identity, occupied.ToArrayOf(), prefix, 2));
        }

        [Test]
        public void DefaultCollisionOrdinalStopsAfterExactly1024Candidates()
        {
            const string source = "https://contoso.org?x=1";
            const string readable = "org.contoso";
            const string hash = "fba5939aa5e3479c3f23480304bbbd621ed8cf43a2ccaf3c91cd60ef7bd29216";
            var occupied = new List<string> { readable };
            foreach (int length in new[] { 8, 16, 32, 64 })
            {
                occupied.Add(readable + "." + hash[..length]);
            }
            for (int ordinal = 1; ordinal < 1024; ordinal++)
            {
                occupied.Add(readable + "." + hash + "." + ordinal.ToString(CultureInfo.InvariantCulture));
            }

            string assigned = XRegistryIdentifier.FromSourceIdentity(source, occupied);

            Assert.Multiple(() =>
            {
                Assert.That(XRegistryIdentifier.MaxCollisionOrdinal, Is.EqualTo(1024u));
                Assert.That(assigned, Is.EqualTo(readable + "." + hash + ".1024"));
                Assert.That(assigned, Has.Length.LessThanOrEqualTo(128));
            });
            occupied.Add(assigned);
            Assert.Throws<InvalidOperationException>(() => XRegistryIdentifier.FromSourceIdentity(source, occupied));
            Assert.Throws<InvalidOperationException>(() =>
                XRegistryIdentifier.FromSourceIdentity(source, occupied.ToArrayOf(), string.Empty));
        }

        [Test]
        public void A120CharacterUriPathReservesTheHashBudget()
        {
            string identity = "https://contoso.org/" + new string('a', 120);
            string assigned = XRegistryIdentifier.FromSourceIdentity(identity);

            Assert.That(assigned, Is.EqualTo(
                ("org.contoso." + new string('a', 120))[..119] +
                "." +
                XRegistryIdentifier.Disambiguator(identity)));
        }

        [TestCase(
            "https://contoso.org?x=1",
            "org.contoso",
            "fba5939aa5e3479c3f23480304bbbd621ed8cf43a2ccaf3c91cd60ef7bd29216")]
        [TestCase(
            "https://Contoso.org/a%2Fb?q=A#F",
            "org.Contoso.a-b",
            "e072b494b1675906e18f91626f2d4be601a951cd6179cee2f70b8d4ef24d0cfa")]
        [TestCase(
            "urn:Example:%C3%A9",
            "urn.Example",
            "7d00e2f58365a75f8a0c97623cbbc7eddb4d6273f64a4f24ea1a3c6b321c4bf6")]
        public void GoldenSuffixesHashTheOriginalUtf8Authority(string source, string readable, string sha256)
        {
            var occupied = new List<string> { readable };
            foreach (int length in new[] { 8, 16, 32, 64 })
            {
                string candidate = XRegistryIdentifier.FromSourceIdentity(source, occupied);

                Assert.That(candidate, Is.EqualTo(readable + "." + sha256[..length]));
                occupied.Add(candidate);
            }
        }

        [Test]
        public void SchemeUserInfoQueryAndFragmentAreDiscarded()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity(
                    "https://user:pw@contoso.org/things/pump-01?v=2#frag"),
                Is.EqualTo("org.contoso.things.pump-01"));
        }

        [Test]
        public void UrnKeepsItsLeadingLabelSoItCannotAliasABarePath()
        {
            // A URN is split on ':' so that "urn" survives as the first label.
            // Without that rule "urn:dev:ops" and the path "dev/ops" would
            // normalize to the same token.
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("urn:dev:ops"),
                Is.EqualTo("urn.dev.ops"));
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("dev/ops"),
                Is.EqualTo("dev.ops"));
        }

        [Test]
        public void PathSegmentsArePercentDecodedBeforeNormalization()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("things/pump%20one"),
                Is.EqualTo("things.pump-one"));
        }

        [Test]
        public void RunsOutsideTheAlphabetCollapseToASingleDash()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("a!!!b"),
                Is.EqualTo("a-b"));
        }

        [Test]
        public void LeadingAndTrailingSeparatorsAreStripped()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("---abc---"),
                Is.EqualTo("abc"));
        }

        [Test]
        public void LetterCaseIsPreserved()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("http://contoso.org/UA/Pumps/"),
                Does.Contain("UA"));
        }

        [Test]
        public void AnIdentityWithNoSurvivingLabelBecomesTheEmptyIdentifier()
        {
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("///"),
                Is.EqualTo(XRegistryIdentifier.Empty));
            Assert.That(
                XRegistryIdentifier.FromSourceIdentity("!!!"),
                Is.EqualTo(XRegistryIdentifier.Empty));
        }

        [Test]
        public void EveryConstructedLabelStartsWithALetterDigitOrUnderscore()
        {
            string[] identities =
            [
                "http://contoso.org/UA/Pumps/",
                "pkg.usdz[tex/a.png]",
                "urn:dev:ops:32473-pump-01",
                "---abc---",
                "a!!!b"
            ];

            foreach (string identity in identities)
            {
                string identifier = XRegistryIdentifier.FromSourceIdentity(identity);
                foreach (string label in identifier.Split('.'))
                {
                    Assert.That(label, Is.Not.Empty);
                    char first = label[0];
                    Assert.That(
                        char.IsLetterOrDigit(first) || first == '_',
                        Is.True,
                        $"Label '{label}' of '{identifier}' should satisfy the " +
                        "xRegistry start-character rule.");
                }
            }
        }

        [Test]
        public void ALongIdentityIsTruncatedAndDisambiguatedKeepingItsFirstLabel()
        {
            string identity = "http://contoso.org/" +
                string.Join("/", Enumerable.Range(0, 40).Select(i => $"segment{i}"));

            string identifier = XRegistryIdentifier.FromSourceIdentity(identity);

            Assert.That(identifier, Has.Length.LessThanOrEqualTo(XRegistryIdentifier.MaxLength));
            Assert.That(
                identifier,
                Does.StartWith("org."),
                "The first label carries the reverse-DNS root and is never dropped.");
            Assert.That(
                identifier,
                Does.EndWith("." + XRegistryIdentifier.Disambiguator(identity)));
        }

        [Test]
        public void ACollidingSiblingGetsTheDisambiguator()
        {
            const string identity = "http://contoso.org/UA/Pumps/";
            string plain = XRegistryIdentifier.FromSourceIdentity(identity);

            string disambiguated = XRegistryIdentifier.FromSourceIdentity(
                identity,
                [plain]);

            Assert.That(disambiguated, Is.Not.EqualTo(plain));
            Assert.That(
                disambiguated,
                Is.EqualTo(plain + "." + XRegistryIdentifier.Disambiguator(identity)));
        }

        [Test]
        public void ACollisionIsDetectedCaseInsensitively()
        {
            const string identity = "http://contoso.org/UA/Pumps/";
            string plain = XRegistryIdentifier.FromSourceIdentity(identity);

            string disambiguated = XRegistryIdentifier.FromSourceIdentity(
                identity,
                [plain.ToUpperInvariant()]);

            Assert.That(disambiguated, Is.Not.EqualTo(plain));
        }

        [Test]
        public void ACollidingSiblingOnALongIdentityStaysWithinMaxLength()
        {
            // An identity whose identifier already sits close to MaxLength: the
            // disambiguator has to displace part of the head rather than extend
            // past the documented cap.
            string identity = "http://contoso.org/" +
                string.Join("/", Enumerable.Range(0, 40).Select(i => $"segment{i}"));
            string plain = XRegistryIdentifier.FromSourceIdentity(identity);

            string disambiguated = XRegistryIdentifier.FromSourceIdentity(identity, [plain]);

            Assert.That(
                disambiguated,
                Has.Length.LessThanOrEqualTo(XRegistryIdentifier.MaxLength),
                "The disambiguated identifier must honour MaxLength; it is used as a " +
                "NodeId component and as a file name.");
            Assert.That(disambiguated, Is.Not.EqualTo(plain));
            Assert.That(disambiguated[(disambiguated.LastIndexOf('.') + 1)..], Has.Length.EqualTo(16));
        }

        [Test]
        public void ANonCollidingSiblingLeavesTheIdentifierAlone()
        {
            const string identity = "http://contoso.org/UA/Pumps/";

            Assert.That(
                XRegistryIdentifier.FromSourceIdentity(identity, ["something.else"]),
                Is.EqualTo(XRegistryIdentifier.FromSourceIdentity(identity)));
        }

        [Test]
        public void TheDisambiguatorIsEightLowerCaseHexCharactersOfTheIdentity()
        {
            string disambiguator = XRegistryIdentifier.Disambiguator(
                "http://contoso.org/UA/Pumps/");

            Assert.That(disambiguator, Has.Length.EqualTo(8));
            foreach (char c in disambiguator)
            {
                Assert.That(
                    c,
                    Is.GreaterThanOrEqualTo('0').And.LessThanOrEqualTo('9')
                        .Or.GreaterThanOrEqualTo('a').And.LessThanOrEqualTo('f'),
                    $"'{c}' should be a lower-case hexadecimal character.");
            }
        }

        [Test]
        public void TheDisambiguatorDependsOnlyOnTheSourceIdentity()
        {
            // It is a function of the identity rather than of any document, so
            // it does not change when a new version is written.
            Assert.That(
                XRegistryIdentifier.Disambiguator("urn:dev:ops:32473-pump-01"),
                Is.EqualTo(XRegistryIdentifier.Disambiguator("urn:dev:ops:32473-pump-01")));
            Assert.That(
                XRegistryIdentifier.Disambiguator("urn:dev:ops:32473-pump-01"),
                Is.Not.EqualTo(XRegistryIdentifier.Disambiguator("urn:dev:ops:32473-pump-02")));
        }

        [Test]
        public void ConstructionIsStableAcrossCalls()
        {
            const string identity = "https://contoso.org/things/pump-01";

            Assert.That(
                XRegistryIdentifier.FromSourceIdentity(identity),
                Is.EqualTo(XRegistryIdentifier.FromSourceIdentity(identity)));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.That(
                () => XRegistryIdentifier.FromSourceIdentity(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => XRegistryIdentifier.FromSourceIdentity("urn:a", null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => XRegistryIdentifier.Disambiguator(null!),
                Throws.ArgumentNullException);
        }
    }
}
