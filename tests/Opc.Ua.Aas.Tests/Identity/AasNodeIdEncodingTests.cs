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
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Identity
{
    /// <summary>
    /// Tests the clause 6.1.3 String NodeId encoding against the worked
    /// examples the specification states, and against the properties the
    /// clause claims the encoding has.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasNodeIdEncodingTests
    {
        [Test]
        public void ShellIdentifierMatchesTheSpecificationExample()
        {
            Assert.That(
                AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, "a#b"),
                Is.EqualTo("i4aas3:A:3:a#b"));
        }

        [Test]
        public void ElementIdentifierMatchesTheSpecificationExample()
        {
            Assert.That(
                AasNodeIdEncoding.CreateElementId("a", "b"),
                Is.EqualTo("i4aas3:E:1:1:ab"));
        }

        [Test]
        public void ShellAndElementExamplesDoNotCollide()
        {
            // Clause 6.1.3 offers these two side by side as the demonstration
            // that the encoding is collision-free across node kinds.
            Assert.That(
                AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, "a#b"),
                Is.Not.EqualTo(AasNodeIdEncoding.CreateElementId("a", "b")));
        }

        [TestCase("a\nb", "i4aas3:S:5:a%0Ab", TestName = "LineFeedEncodesAsPercent0A")]
        [TestCase("a\0b", "i4aas3:S:5:a%00b", TestName = "NulEncodesAsPercent00")]
        [TestCase("\u0085", "i4aas3:S:6:%C2%85", TestName = "C1ControlEncodesAsItsUtf8Bytes")]
        [TestCase("%0A", "i4aas3:S:5:%250A", TestName = "LiteralPercentEncodesAsPercent25")]
        public void SubmodelIdentifierMatchesTheSpecificationExamples(string id, string expected)
        {
            Assert.That(
                AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Submodel, id),
                Is.EqualTo(expected));
        }

        [Test]
        public void EscapeLeavesAnUnreservedScalarValueUnchanged()
        {
            const string value = "https://fabrikam.com/ids/sm/ordering";
            Assert.That(AasNodeIdEncoding.Escape(value), Is.EqualTo(value));
        }

        [Test]
        public void EscapeDoesNotNormalize()
        {
            // The scan is over scalar values without normalization, so a
            // decomposed and a precomposed spelling stay distinct.
            const string precomposed = "\u00E9";
            const string decomposed = "e\u0301";

            Assert.That(
                AasNodeIdEncoding.Escape(precomposed),
                Is.Not.EqualTo(AasNodeIdEncoding.Escape(decomposed)));
        }

        [TestCase("")]
        [TestCase("plain")]
        [TestCase("a#b")]
        [TestCase("a\nb")]
        [TestCase("%")]
        [TestCase("%0A")]
        [TestCase("\u0000\u001F\u007F\u009F")]
        [TestCase("\uD83D\uDE00")]
        [TestCase("urn:samm:io.admin-shell.idta.batterypass:1.0.0#Battery")]
        [TestCase("0173-1#02-AAO677#002")]
        public void EscapeAndUnescapeRoundTrip(string value)
        {
            Assert.That(
                AasNodeIdEncoding.Unescape(AasNodeIdEncoding.Escape(value)),
                Is.EqualTo(value));
        }

        [Test]
        public void ARunOfConsecutiveEscapesDecodesAsOneUtf8Sequence()
        {
            // Four escaped scalar values in a row produce five octets with
            // nothing between them, so a decoder that assumes one escape run is
            // at most one UTF-8 sequence fails here.
            const string value = "\u0000\u001F\u007F\u009F";

            Assert.Multiple(() =>
            {
                Assert.That(AasNodeIdEncoding.Escape(value), Is.EqualTo("%00%1F%7F%C2%9F"));
                Assert.That(AasNodeIdEncoding.Unescape("%00%1F%7F%C2%9F"), Is.EqualTo(value));
            });
        }

        [Test]
        public void ALongRunOfConsecutiveEscapesRoundTrips()
        {
            string value = new('\u0001', 64);

            Assert.That(
                AasNodeIdEncoding.Unescape(AasNodeIdEncoding.Escape(value)),
                Is.EqualTo(value));
        }

        [TestCase("%41", TestName = "RejectsAnEscapeWhoseValueWouldNotBeEscaped")]
        [TestCase("%0a", TestName = "RejectsLowercaseHexadecimal")]
        [TestCase("%0", TestName = "RejectsATruncatedEscape")]
        [TestCase("%ZZ", TestName = "RejectsNonHexadecimalDigits")]
        [TestCase("\n", TestName = "RejectsARawControlCharacter")]
        [TestCase("%C2", TestName = "RejectsAnIncompleteUtf8Sequence")]
        public void UnescapeRejectsANonCanonicalForm(string value)
        {
            Assert.That(
                () => AasNodeIdEncoding.Unescape(value),
                Throws.TypeOf<FormatException>());
        }

        [TestCase(AasNodeKind.Shell, 'A')]
        [TestCase(AasNodeKind.Submodel, 'S')]
        [TestCase(AasNodeKind.ConceptDescription, 'C')]
        [TestCase(AasNodeKind.SubmodelElement, 'E')]
        public void DiscriminatorOfMatchesTheSpecification(AasNodeKind kind, char expected)
        {
            Assert.That(AasNodeIdEncoding.DiscriminatorOf(kind), Is.EqualTo(expected));
        }

        [TestCase(AasNodeKind.Shell)]
        [TestCase(AasNodeKind.Submodel)]
        [TestCase(AasNodeKind.ConceptDescription)]
        public void IdentifiableIdentifiersParseBackToTheirSource(AasNodeKind kind)
        {
            const string id = "https://fabrikam.com/ids/sm/a#b\u0085";

            string identifier = AasNodeIdEncoding.CreateIdentifiableId(kind, id);

            Assert.That(AasNodeIdEncoding.TryParse(identifier, out AasParsedNodeId parsed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parsed.Kind, Is.EqualTo(kind));
                Assert.That(parsed.Id, Is.EqualTo(id));
                Assert.That(parsed.IdShortPath, Is.Null);
                Assert.That(parsed.IsIdentifiable, Is.True);
            });
        }

        [Test]
        public void ElementIdentifiersParseBackToTheirTwoSources()
        {
            const string owner = "https://fabrikam.com/ids/sm/ordering";
            const string path = "CollectionsInsideAList[0].Value";

            string identifier = AasNodeIdEncoding.CreateElementId(owner, path);

            Assert.That(AasNodeIdEncoding.TryParse(identifier, out AasParsedNodeId parsed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parsed.Kind, Is.EqualTo(AasNodeKind.SubmodelElement));
                Assert.That(parsed.Id, Is.EqualTo(owner));
                Assert.That(parsed.IdShortPath, Is.EqualTo(path));
                Assert.That(parsed.IsIdentifiable, Is.False);
            });
        }

        [Test]
        public void TheLengthPrefixSplitsAPayloadThatContainsTheDelimiter()
        {
            // The point of the length prefixes: the owner identifier itself
            // contains ':' characters, so no delimiter scan could find the
            // boundary.
            const string owner = "urn:x:1:2:3";
            const string path = "a:b";

            string identifier = AasNodeIdEncoding.CreateElementId(owner, path);

            Assert.That(AasNodeIdEncoding.TryParse(identifier, out AasParsedNodeId parsed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(parsed.Id, Is.EqualTo(owner));
                Assert.That(parsed.IdShortPath, Is.EqualTo(path));
            });
        }

        [Test]
        public void LengthsAreCountedInCodePointsOfTheEncodedComponents()
        {
            // An astral character is one code point but two UTF-16 units, and
            // the prefix counts code points.
            string identifier = AasNodeIdEncoding.CreateIdentifiableId(
                AasNodeKind.Submodel, "\uD83D\uDE00");

            Assert.That(identifier, Is.EqualTo("i4aas3:S:1:\uD83D\uDE00"));
        }

        [TestCase(null, TestName = "RejectsNull")]
        [TestCase("", TestName = "RejectsEmpty")]
        [TestCase("i4aas3:", TestName = "RejectsAMissingDiscriminator")]
        [TestCase("i4aas3:X:1:a", TestName = "RejectsAnUnknownDiscriminator")]
        [TestCase("i4aas3:A:1:ab", TestName = "RejectsALengthThatDisagreesWithThePayload")]
        [TestCase("i4aas3:A:01:a", TestName = "RejectsALeadingZeroInALength")]
        [TestCase("i4aas3:A::a", TestName = "RejectsAMissingLength")]
        [TestCase("i4aas3:E:1:a", TestName = "RejectsAnElementWithOnlyOneLength")]
        [TestCase("i4aas3:E:9:1:ab", TestName = "RejectsAnOverlongOwnerLength")]
        [TestCase("nsu=x;i4aas3:A:1:a", TestName = "RejectsAMissingPrefix")]
        public void TryParseRejectsANonCanonicalIdentifier(string? identifier)
        {
            Assert.That(AasNodeIdEncoding.TryParse(identifier, out _), Is.False);
        }

        [Test]
        public void IsWithinLengthLimitAcceptsAnIdentifierAtTheLimit()
        {
            string identifier = new('a', AasNodeIdEncoding.MaxIdentifierLength);
            Assert.That(AasNodeIdEncoding.IsWithinLengthLimit(identifier), Is.True);
        }

        [Test]
        public void IsWithinLengthLimitRejectsAnIdentifierOverTheLimit()
        {
            string identifier = new('a', AasNodeIdEncoding.MaxIdentifierLength + 1);
            Assert.That(AasNodeIdEncoding.IsWithinLengthLimit(identifier), Is.False);
        }

        [Test]
        public void AnIdentifierNearTheAasLimitStillFitsTheNodeIdLimit()
        {
            // The metamodel allows an id of up to 2048 characters and clause
            // 6.1.3 keeps it verbatim, so the worst unescaped case must fit.
            string id = new('a', 2048);
            string identifier = AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, id);

            Assert.That(AasNodeIdEncoding.IsWithinLengthLimit(identifier), Is.True);
        }

        [Test]
        public void CreateIdentifiableIdRejectsTheElementKind()
        {
            Assert.That(
                () => AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.SubmodelElement, "a"),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CreateIdentifiableIdRejectsANullIdentifier()
        {
            Assert.That(
                () => AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateElementIdRejectsNullArguments()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => AasNodeIdEncoding.CreateElementId(null!, "b"),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => AasNodeIdEncoding.CreateElementId("a", null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void ParsedNodeIdEqualityComparesAllThreeComponents()
        {
            var left = new AasParsedNodeId(AasNodeKind.Submodel, "a", "b");
            var same = new AasParsedNodeId(AasNodeKind.Submodel, "a", "b");
            var otherKind = new AasParsedNodeId(AasNodeKind.Shell, "a", "b");
            var otherPath = new AasParsedNodeId(AasNodeKind.Submodel, "a", "c");

            bool equalOperator = left == same;
            bool notEqualOperator = left != otherKind;

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(same));
                Assert.That(equalOperator, Is.True);
                Assert.That(notEqualOperator, Is.True);
                Assert.That(left.GetHashCode(), Is.EqualTo(same.GetHashCode()));
                Assert.That(left, Is.Not.EqualTo(otherKind));
                Assert.That(left, Is.Not.EqualTo(otherPath));
            });
        }
    }
}
