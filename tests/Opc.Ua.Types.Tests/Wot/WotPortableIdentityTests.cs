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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The generated identity of Annex G.1 is one algorithm, and a conversion
    /// calls the same one a published vector does.
    /// </summary>
    /// <remarks>
    /// A second implementation inside the conversion is what let a generated
    /// NodeId be non-conformant and non-injective while every vector passed:
    /// the vector measured a function nothing in the conversion called.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotPortableIdentityTests
    {
        private const string ModelNamespace = "urn:test:identity";
        private const string EscapedModel = "nsu=urn%3Atest%3Aidentity;";

        /// <summary>
        /// The escaping is what makes the encoding injective. Without it a
        /// member named <c>A/B</c> of <c>Root</c> and a member named <c>B</c>
        /// of <c>Root/A</c> are one string, so two Nodes answer to one
        /// identifier and one of them is unreachable.
        /// </summary>
        [Test]
        public void TwoDifferentPathsCannotProduceOneIdentifier()
        {
            string first = WotPortableIdentity.GenerateNodeId(
                ModelNamespace,
                Path(("Root", ModelNamespace), ("A/B", ModelNamespace)));
            string second = WotPortableIdentity.GenerateNodeId(
                ModelNamespace,
                Path(("Root", ModelNamespace), ("A", ModelNamespace), ("B", ModelNamespace)));

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(first, Does.Contain("A&/B"));
            });
        }

        /// <summary>
        /// A NamespaceUri routinely contains the characters that end an
        /// element, so it is percent-encoded rather than written raw. Otherwise
        /// the <c>/</c> of <c>http://</c> starts a new element.
        /// </summary>
        [Test]
        public void ANamespaceUriCannotEndAnElementEarly()
        {
            string generated = WotPortableIdentity.GenerateNodeId(
                "http://example.com/x",
                Path(("Node", "http://example.com/x")));

            Assert.That(
                generated,
                Is.EqualTo("nsu=http://example.com/x;s=/nsu=http%3A%2F%2Fexample.com%2Fx;Node"));
        }

        /// <summary>
        /// Every OPC 10000-4 Annex A.2 reserved character is escaped inside a
        /// name, and only inside a name.
        /// </summary>
        [TestCase('/')]
        [TestCase('.')]
        [TestCase('<')]
        [TestCase('>')]
        [TestCase(':')]
        [TestCase('#')]
        [TestCase('!')]
        [TestCase('&')]
        public void EveryReservedCharacterIsEscapedInsideAName(char reserved)
        {
            string generated = WotPortableIdentity.GenerateBrowsePath(
                Path(("A" + reserved + "B", ModelNamespace)));

            Assert.That(generated, Is.EqualTo("/" + EscapedModel + "A&" + reserved + "B"));
        }

        /// <summary>
        /// A name outside the Basic Multilingual Plane is one code point and
        /// carries no reserved character, so it passes through whole rather
        /// than being split at a surrogate boundary.
        /// </summary>
        [Test]
        public void ANonBmpNameSurvivesWhole()
        {
            const string name = "Pump\U0001F600Speed";

            string generated = WotPortableIdentity.GenerateBrowsePath(
                Path((name, ModelNamespace)));

            Assert.Multiple(() =>
            {
                Assert.That(generated, Is.EqualTo("/" + EscapedModel + name));
                Assert.That(
                    generated.EndsWith(name, StringComparison.Ordinal),
                    Is.True);
            });
        }

        /// <summary>
        /// A base-namespace element is written bare, which is what tells
        /// <c>InputArguments</c> apart from a member of the model that happens
        /// to share the name.
        /// </summary>
        [Test]
        public void ABaseNamespaceElementIsBareAndAModelElementIsNot()
        {
            string bare = WotPortableIdentity.GenerateBrowsePath(
                Path(("InputArguments", null)));
            string qualified = WotPortableIdentity.GenerateBrowsePath(
                Path(("InputArguments", ModelNamespace)));

            Assert.Multiple(() =>
            {
                Assert.That(bare, Is.EqualTo("/InputArguments"));
                Assert.That(qualified, Is.EqualTo("/" + EscapedModel + "InputArguments"));
                Assert.That(bare, Is.Not.EqualTo(qualified));
            });
        }

        /// <summary>
        /// The base OPC UA NamespaceUri stated explicitly means the same thing
        /// as stating none.
        /// </summary>
        [Test]
        public void TheBaseNamespaceStatedExplicitlyIsStillBare()
        {
            Assert.That(
                WotPortableIdentity.GenerateBrowsePath(
                    Path(("Objects", WotVocabulary.OpcUaNamespace))),
                Is.EqualTo("/Objects"));
        }

        [Test]
        public void GeneratingWithoutANamespaceIsRefused()
        {
            Assert.That(
                () => WotPortableIdentity.GenerateNodeId(
                    null!, ArrayOf<WotBrowsePathElement>.Empty),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// An empty path is the projection root's own namespace and nothing
        /// else, which is a well-formed - if not useful - identifier rather
        /// than a fault.
        /// </summary>
        [Test]
        public void AnEmptyPathIsTheNamespaceAlone()
        {
            Assert.That(
                WotPortableIdentity.GenerateNodeId(
                    ModelNamespace, ArrayOf<WotBrowsePathElement>.Empty),
                Is.EqualTo("nsu=urn:test:identity;s="));
        }

        /// <summary>
        /// Section 5.1.1 admits all four OPC 10000-6 identifier types, with or
        /// without the <c>nsu=</c> qualifier, and refuses the session-local
        /// <c>ns=</c> and the <c>svr=</c> prefix.
        /// </summary>
        [TestCase("i=2041", true)]
        [TestCase("s=Pump", true)]
        [TestCase("g=09087e75-8e5e-499b-954f-f2a9603db28a", true)]
        [TestCase("b=M/RbKBsRVkePCePcx24oRA==", true)]
        [TestCase("nsu=urn:t;i=1", true)]
        [TestCase("nsu=urn:t;s=A", true)]
        [TestCase("nsu=urn:t;g=09087e75-8e5e-499b-954f-f2a9603db28a", true)]
        [TestCase("nsu=urn:t;b=AQI=", true)]
        [TestCase("ns=1;s=Pump", false)]
        [TestCase("ns=0;i=85", false)]
        [TestCase("svr=1;nsu=urn:t;s=A", false)]
        [TestCase("x=1", false)]
        [TestCase("i=", false)]
        [TestCase("i;1", false)]
        [TestCase("nsu=urn:t", false)]
        [TestCase("nsu=;s=A", false)]
        [TestCase("nsu=urn:t;", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void EveryIdentifierTypeIsJudgedAsSpecified(string? value, bool portable)
        {
            Assert.That(WotPortableIdentity.IsPortableNodeId(value), Is.EqualTo(portable));
        }

        /// <summary>
        /// The session-local form is recognized wherever it is written, which
        /// is what the conversion refuses on a persisted term.
        /// </summary>
        [TestCase("ns=1;s=A", true)]
        [TestCase("ns=12;i=3", true)]
        [TestCase("ns=", false)]
        [TestCase("ns=;s=A", false)]
        [TestCase("ns=-1;i=3", false)]
        [TestCase("ns=12", false)]
        [TestCase("nsu=urn:t;s=A", false)]
        [TestCase("nsx=1;s=A", false)]
        [TestCase(null, false)]
        public void TheSessionLocalFormIsRecognized(string? value, bool sessionLocal)
        {
            Assert.That(
                WotPortableIdentity.IsSessionLocalNodeId(value),
                Is.EqualTo(sessionLocal));
        }

        /// <summary>
        /// An element with no name is a path element that names nothing, which
        /// is encoded as the empty name rather than throwing on a value the
        /// caller is entitled to construct.
        /// </summary>
        [Test]
        public void AnElementWithNoNameEncodesAsAnEmptyName()
        {
            Assert.That(
                WotPortableIdentity.GenerateBrowsePath(
                    new ArrayOf<WotBrowsePathElement>(
                        new[] { new WotBrowsePathElement(null, null!) })),
                Is.EqualTo("/"));
        }

        /// <summary>
        /// A conversion generates the identifiers Annex G.1 states, which is
        /// checked against the formula rather than against a copy of the
        /// conversion's own output.
        /// </summary>
        [Test]
        public void AConversionGeneratesTheAnnexG1Identifier()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                Encoding.UTF8.GetBytes(Document("\"Speed\":{\"type\":\"number\"}")));

            UAVariable speed = nodeSet.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, "1:Speed", StringComparison.Ordinal));
            string expected = "ns=1;s=" + WotPortableIdentity.GenerateBrowsePath(
                Path(("Tank", ModelNamespace), ("Speed", ModelNamespace)));

            Assert.That(speed.NodeId, Is.EqualTo(expected));
        }

        /// <summary>
        /// A member whose name carries a path separator cannot collide with a
        /// nested Node of the same spelling, because the conversion escapes it
        /// exactly as the formula does.
        /// </summary>
        [Test]
        public void AConversionEscapesASeparatorInAMemberName()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                Encoding.UTF8.GetBytes(Document(
                    "\"A/B\":{\"type\":\"number\"},\"C\":{\"type\":\"number\"}")));

            List<string> identifiers = [.. nodeSet.Items!.OfType<UAVariable>()
                .Select(v => v.NodeId!)];

            Assert.Multiple(() =>
            {
                Assert.That(identifiers, Is.Unique);
                Assert.That(
                    identifiers.Any(id => id.Contains("A&/B", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// A standard child OPC 10000-5 declares in the base namespace is
        /// written bare, so it cannot be confused with a model member of the
        /// same name.
        /// </summary>
        [Test]
        public void AStandardArgumentChildIsWrittenBare()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                Encoding.UTF8.GetBytes(Document(
                    properties: null,
                    actions: "\"Reset\":{\"input\":{\"type\":\"number\"}}")));

            UAVariable arguments = nodeSet.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(
                    v.BrowseName, "InputArguments", StringComparison.Ordinal));

            Assert.That(
                arguments.NodeId,
                Is.EqualTo("ns=1;s=" + WotPortableIdentity.GenerateBrowsePath(
                    Path(
                        ("Tank", ModelNamespace),
                        ("Reset", ModelNamespace),
                        ("InputArguments", null)))));
        }

        /// <summary>
        /// The declaration view and the synthesis derive one identity from the
        /// same two inputs, so a Method instance and the declaration it points
        /// at cannot disagree.
        /// </summary>
        [Test]
        public void TheDeclarationViewAndTheSynthesisAgree()
        {
            using WotDocument model = WotDocument.Parse(
                Encoding.UTF8.GetBytes(TypeDocument()));

            Assert.That(
                WotNodeSetConverter.TryDescribeTypeDeclarations(
                    model,
                    out ArrayOf<WotTypeDeclaration> declarations,
                    out _),
                Is.True);

            WotTypeDeclaration reset = Single(declarations);
            Assert.That(
                reset.NodeId,
                Is.EqualTo(WotPortableIdentity.GenerateNodeId(
                    ModelNamespace,
                    Path(("TankType", ModelNamespace), ("Reset", ModelNamespace)))));
        }

        /// <summary>
        /// Section 5.1.4: a relative browse path with no anchor names a
        /// sequence of steps from nowhere, and the conversion refuses it
        /// through the same predicate the published vectors are run against.
        /// </summary>
        [Test]
        public void ARelativeBrowsePathWithoutAnAnchorIsRefused()
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Document(
                    "\"Speed\":{\"type\":\"number\",\"uav:browsePath\":\"Speed\"}")));
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NonPortableIdentity &&
                        d.Message.Contains("no starting Node", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// An anchor is what gives a relative path a starting Node, so the same
        /// path with one is accepted.
        /// </summary>
        [Test]
        public void AnAnchoredRelativeBrowsePathIsAccepted()
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Document(
                    "\"Speed\":{\"type\":\"number\",\"uav:browsePath\":\"Speed\"," +
                    "\"uav:browsePathAnchor\":\"nsu=urn:test:identity;s=Tank\"}")));
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NonPortableIdentity &&
                        d.Message.Contains("no starting Node", StringComparison.Ordinal)),
                Is.False);
        }

        /// <summary>
        /// An absolute path always has a starting Node, and an element using a
        /// numeric NamespaceIndex never does.
        /// </summary>
        [TestCase("/Objects/1:Pump", false, false)]
        [TestCase("/Objects/Pump", false, true)]
        [TestCase("/Objects/Pump/", false, true)]
        [TestCase("/", false, true)]
        [TestCase("Pump", false, false)]
        [TestCase("Pump", true, true)]
        [TestCase("", true, false)]
        [TestCase(null, true, false)]
        public void ABrowsePathResolvesOnlyWithAStartingNode(
            string? path, bool anchored, bool resolvable)
        {
            Assert.That(
                WotPortableIdentity.IsResolvableBrowsePath(path, anchored),
                Is.EqualTo(resolvable));
        }

        /// <summary>
        /// Annex G.3 length-prefixes every item, so a sequence carrying a
        /// <c>null</c> is encoded as the empty string it stands for rather than
        /// throwing on a value an <see cref="ArrayOf{T}"/> is entitled to hold.
        /// </summary>
        [Test]
        public void ANullItemEncodesAsTheEmptyString()
        {
            ByteString withNull = WotPortableIdentity.EncodeSequence(
                new ArrayOf<string>(new[] { "A", null!, "B" }));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Encoding.UTF8.GetString(withNull.Span.ToArray()),
                    Is.EqualTo("1:A\n0:\n1:B\n"));
                Assert.That(
                    withNull.Span.SequenceEqual(
                        WotPortableIdentity.EncodeSequence(
                            new ArrayOf<string>(new[] { "A", string.Empty, "B" })).Span),
                    Is.True,
                    "A null item and an empty item are the same item.");
            });
        }

        /// <summary>
        /// Section 12.6 computes the <c>ViewVersion</c> over a <em>set</em>: a
        /// member reached twice contributes once, and a member that is
        /// <c>null</c> stands for the empty identity rather than ending the
        /// computation.
        /// </summary>
        [Test]
        public void TheViewVersionIsComputedOverTheDistinctMembership()
        {
            uint duplicated = WotPortableIdentity.ComputeViewVersion(
                new ArrayOf<string>(s_repeatedMembership));
            uint distinct = WotPortableIdentity.ComputeViewVersion(
                new ArrayOf<string>(s_distinctMembership));
            uint withNull = WotPortableIdentity.ComputeViewVersion(
                new ArrayOf<string>(s_membershipWithNull));
            uint withEmpty = WotPortableIdentity.ComputeViewVersion(
                new ArrayOf<string>(s_membershipWithEmpty));

            Assert.Multiple(() =>
            {
                Assert.That(duplicated, Is.EqualTo(distinct));
                Assert.That(withNull, Is.EqualTo(withEmpty));
                Assert.That(withNull, Is.Not.EqualTo(distinct));
            });
        }

        /// <summary>
        /// OPC 10000-3 requires a <c>ViewVersion</c> greater than zero, so the
        /// one membership in a billion whose digest opens with four zero octets
        /// is reported as one rather than as the zero that means "no version".
        /// </summary>
        /// <remarks>
        /// The membership below was found by search: its Annex G.3 digest is
        /// <c>00000000a8a37695…</c>, so the big-endian UInt32 the clause reads
        /// out of the first four octets is exactly zero. It is pinned here
        /// because nothing else reaches the clause - and a clause nothing
        /// reaches is a clause that could stop working unnoticed.
        /// </remarks>
        [Test]
        public void AViewVersionThatComputesToZeroIsReportedAsOne()
        {
            var membership = new ArrayOf<string>(s_zeroDigestMembership);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Hex(WotPortableIdentity.SequenceDigest(membership)),
                    Does.StartWith("00000000"),
                    "The pinned membership is the one whose digest opens with four zero octets.");
                Assert.That(WotPortableIdentity.ComputeViewVersion(membership), Is.EqualTo(1u));
            });
        }

        private static string Hex(ByteString value)
        {
            var text = new StringBuilder(value.Length * 2);
            foreach (byte octet in value.Span)
            {
                text.Append(octet.ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>
        /// A <c>uav:nodes</c> projection and a <c>uav:nodeSet</c> envelope are
        /// exact preservation subtrees: their namespace indices are resolved
        /// through their own tables, so Section 5.1.1's portability rules do
        /// not apply inside them.
        /// </summary>
        [TestCase("uav:nodes")]
        [TestCase("uav:nodeSet")]
        public void APreservationSubtreeKeepsItsOwnIndices(string term)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"id\":\"" + ModelNamespace + "\"," +
                "\"title\":\"Tank\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Speed\":{\"type\":\"number\"," +
                "\"" + term + "\":{\"uav:id\":\"ns=1;i=5\"}}}}"));

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NonPortableIdentity),
                Is.False,
                "The preservation subtree is not read for portability.");
        }

        /// <summary>
        /// The session-local form outside a preservation subtree is what
        /// Section 5.1.1 refuses, and a ServerIndex prefix is refused for the
        /// same reason with its own wording.
        /// </summary>
        [TestCase("ns=1;i=5", "session-local")]
        [TestCase("svr=1;nsu=urn:t;s=A", "not an ExpandedNodeId a persisted document may carry")]
        public void ANonPortableIdentityIsRefusedOnAPersistedTerm(string value, string reason)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"id\":\"" + ModelNamespace + "\"," +
                "\"title\":\"Tank\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Speed\":{\"type\":\"number\"," +
                "\"uav:mapToNodeId\":\"" + value + "\"}}}"));

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NonPortableIdentity &&
                        d.Message.Contains(reason, StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// Section 5.1.3. A persisted QualifiedName is a compact prefixed name,
        /// a bare namespace-0 name, or the <c>nsu=</c> form; a numeric
        /// NamespaceIndex prefix is never persisted.
        /// </summary>
        [TestCase("Temperature", true)]
        [TestCase("ua:Temperature", true)]
        [TestCase("nsu=urn:t;Temperature", true)]
        [TestCase("nsu=;Temperature", false)]
        [TestCase("nsu=urn:t;", false)]
        [TestCase("1:Temperature", false)]
        [TestCase("-1:Temperature", true)]
        [TestCase(":Temperature", false)]
        [TestCase("ua:", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AQualifiedNameIsJudgedAsSpecified(string? value, bool portable)
        {
            Assert.That(
                WotPortableIdentity.IsPortableQualifiedName(value), Is.EqualTo(portable));
        }

        /// <summary>
        /// An escaped separator inside a name does not split the path, which is
        /// the other half of the escaping that makes the generation injective.
        /// </summary>
        [Test]
        public void AnEscapedSeparatorDoesNotSplitAPath()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotPortableIdentity.IsResolvableBrowsePath("/Objects/A&/B", false),
                    Is.True,
                    "'A/B' is one element, not two.");
                Assert.That(
                    WotPortableIdentity.IsResolvableBrowsePath("/Objects/1:B", false),
                    Is.False,
                    "A numeric NamespaceIndex is never persisted, escaped or not.");
            });
        }

        /// <summary>
        /// Every character an OPC 10000-4 relative path gives a meaning to is
        /// percent-encoded inside a NamespaceUri, so the URI cannot end its
        /// element early however it is spelled.
        /// </summary>
        [Test]
        public void EveryDelimiterInsideANamespaceUriIsEncoded()
        {
            Assert.That(
                WotPortableIdentity.GenerateBrowsePath(
                    Path(("Node", "a/b:c;d%e"))),
                Is.EqualTo("/nsu=a%2Fb%3Ac%3Bd%25e;Node"));
        }

        private static readonly string[] s_repeatedMembership =
            ["nsu=urn:t;i=2", "nsu=urn:t;i=1", "nsu=urn:t;i=2"];

        private static readonly string[] s_distinctMembership =
            ["nsu=urn:t;i=1", "nsu=urn:t;i=2"];

        private static readonly string[] s_membershipWithNull =
            [null!, "nsu=urn:t;i=1"];

        private static readonly string[] s_membershipWithEmpty =
            [string.Empty, "nsu=urn:t;i=1"];

        /// <summary>
        /// The one membership whose Annex G.3 digest opens with four zero
        /// octets, found by exhaustive search over <c>urn:v:&lt;n&gt;</c>.
        /// </summary>
        private static readonly string[] s_zeroDigestMembership = ["urn:v:1021232785"];

        private static ArrayOf<WotBrowsePathElement> Path(
            params (string Name, string? NamespaceUri)[] elements)
        {
            var built = new WotBrowsePathElement[elements.Length];
            for (int ii = 0; ii < elements.Length; ii++)
            {
                built[ii] = new WotBrowsePathElement(
                    elements[ii].NamespaceUri, elements[ii].Name);
            }
            return new ArrayOf<WotBrowsePathElement>(built);
        }

        private static WotTypeDeclaration Single(ArrayOf<WotTypeDeclaration> declarations)
        {
            Assert.That(declarations.Count, Is.EqualTo(1));
            foreach (WotTypeDeclaration declaration in declarations)
            {
                return declaration;
            }
            throw new InvalidOperationException("unreachable");
        }

        private static string Document(string? properties, string? actions = null)
        {
            var builder = new StringBuilder();
            builder
                .Append("{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}],")
                .Append("\"@type\":\"uav:object\",")
                .Append("\"id\":\"")
                .Append(ModelNamespace)
                .Append("\",\"title\":\"Tank\",")
                .Append("\"security\":\"nosec_sc\",")
                .Append("\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}");
            if (properties is not null)
            {
                builder.Append(",\"properties\":{").Append(properties).Append('}');
            }
            if (actions is not null)
            {
                builder.Append(",\"actions\":{").Append(actions).Append('}');
            }
            return builder.Append('}').ToString();
        }

        private static string TypeDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"id\":\"" + ModelNamespace + "\"," +
                "\"title\":\"TankType\"," +
                "\"uav:browseName\":\"nsu=" + ModelNamespace + ";TankType\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"actions\":{\"Reset\":{\"uav:modellingRule\":\"Mandatory\"}}}";
        }
    }
}
