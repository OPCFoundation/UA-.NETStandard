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
            Assert.That(
                disambiguated,
                Does.EndWith("." + XRegistryIdentifier.Disambiguator(identity)));
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
                    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                    Is.True,
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
