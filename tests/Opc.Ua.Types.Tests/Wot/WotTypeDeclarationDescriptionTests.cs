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
 *
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
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// How a Thing Model's affordances are read as the instance declarations of
    /// the type it projects, and how a set of such documents is indexed so a
    /// <c>tm:extends</c> chain can be walked.
    /// </summary>
    /// <remarks>
    /// The declaration view has no NodeSet namespace table and performs no
    /// conversion, so every name, identity, DataType and dimension it reports
    /// is derived by a second reading of the document. Each rule is therefore
    /// exercised with a document that satisfies it and one that breaks it in
    /// the way the rule exists to catch: a rule that only ever sees a
    /// well-formed document proves the document is well formed, not that the
    /// rule would notice otherwise.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotTypeDeclarationDescriptionTests
    {
        private const string ModelNamespace = "urn:test:declared";
        private const string OtherNamespace = "urn:test:other";
        private const string TankTypeId = "nsu=urn:test:declared;i=1042";

        /// <summary>
        /// Only a Thing Model projects a type, so a Thing Description declares
        /// nothing and says so rather than reporting an empty type.
        /// </summary>
        [Test]
        public void AThingDescriptionDescribesNoDeclarations()
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(Instance()));

            bool described = WotNodeSetConverter.TryDescribeTypeDeclarations(
                document,
                out ArrayOf<WotTypeDeclaration> declarations,
                out ArrayOf<string> supertypes);

            Assert.Multiple(() =>
            {
                Assert.That(described, Is.False);
                Assert.That(declarations, Is.Empty);
                Assert.That(supertypes, Is.Empty);
            });
        }

        /// <summary>
        /// Section 5.1.3's <c>nsu=</c> form names the declaration's own
        /// namespace, which is how a type declares a member of a namespace
        /// other than its own.
        /// </summary>
        [Test]
        public void AQualifiedBrowseNameNamesItsOwnNamespace()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"nsu=" + OtherNamespace + ";Velocity\"}");

            Assert.Multiple(() =>
            {
                Assert.That(declaration.NamespaceUri, Is.EqualTo(OtherNamespace));
                Assert.That(declaration.BrowseName, Is.EqualTo("Velocity"));
            });
        }

        /// <summary>
        /// A percent-escaped NamespaceUri is unescaped, which is the other half
        /// of the escaping the generated identity applies.
        /// </summary>
        [Test]
        public void AnEscapedNamespaceUriIsUnescaped()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"nsu=urn%3Atest%3Aother;Velocity\"}");

            Assert.That(declaration.NamespaceUri, Is.EqualTo(OtherNamespace));
        }

        /// <summary>
        /// A <c>nsu=</c> spelling that carries no namespace, no local name or
        /// no delimiter at all is not a qualified name: it names nothing this
        /// Binding could split, so the whole spelling stays the name and the
        /// model's own namespace qualifies it. Guessing a split would invent a
        /// namespace the document never wrote.
        /// </summary>
        [TestCase("nsu=" + OtherNamespace, TestName = "NoDelimiter")]
        [TestCase("nsu=;Velocity", TestName = "NoNamespace")]
        [TestCase("nsu=" + OtherNamespace + ";", TestName = "NoLocalName")]
        public void AMalformedQualifiedBrowseNameIsTakenWhole(string browseName)
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"" + browseName + "\"}");

            Assert.Multiple(() =>
            {
                Assert.That(declaration.NamespaceUri, Is.EqualTo(ModelNamespace));
                Assert.That(declaration.BrowseName, Is.EqualTo(browseName));
            });
        }

        /// <summary>
        /// A compact name whose prefix the document's <c>@context</c> does not
        /// define names no namespace, so the local name is qualified by the
        /// model's own. The prefix is dropped rather than kept, because a
        /// BrowseName is a name and not a name with a prefix glued to it.
        /// </summary>
        [Test]
        public void AnUndefinedPrefixFallsBackToTheModelsOwnNamespace()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"zz:Velocity\"}");

            Assert.Multiple(() =>
            {
                Assert.That(declaration.NamespaceUri, Is.EqualTo(ModelNamespace));
                Assert.That(declaration.BrowseName, Is.EqualTo("Velocity"));
            });
        }

        /// <summary>
        /// The declared type definition comes from the affordance's own
        /// <c>ua:HasTypeDefinition</c> link. A <c>links</c> member that holds
        /// no such link, or that is not a list of links at all, states none, and
        /// the declaration falls back to <c>BaseDataVariableType</c> rather than
        /// to whatever the first link happens to name.
        /// </summary>
        [TestCase("\"links\":[{\"rel\":\"icon\",\"href\":\"i=68\"}]", TestName = "OtherRelation")]
        [TestCase("\"links\":[{\"rel\":\"ua:HasTypeDefinition\"}]", TestName = "NoHref")]
        [TestCase("\"links\":{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"i=68\"}",
            TestName = "NotAList")]
        public void ATypeDefinitionIsOnlyTakenFromTheTypeBindingLink(string links)
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\"," + links + "}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    declaration.TypeDefinitionNodeId,
                    Is.EqualTo(WotVocabulary.BaseDataVariableType));
                Assert.That(declaration.ReferenceTypeName, Is.EqualTo("HasComponent"));
            });
        }

        /// <summary>
        /// A declared <c>PropertyType</c> is reached by <c>HasProperty</c>,
        /// which is the ReferenceType the populated Node has to carry.
        /// </summary>
        [Test]
        public void ADeclaredPropertyTypeIsReachedByHasProperty()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"links\":[{\"rel\":\"ua:HasTypeDefinition\"," +
                "\"href\":\"" + WotVocabulary.PropertyType + "\"}]}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    declaration.TypeDefinitionNodeId, Is.EqualTo(WotVocabulary.PropertyType));
                Assert.That(declaration.ReferenceTypeName, Is.EqualTo("HasProperty"));
            });
        }

        /// <summary>
        /// Section 5.4's DataType precedence: the definitive
        /// <c>uav:mapToType</c> first, then the annotated
        /// <c>uav:dataTypeId</c>, and only then what the json type implies.
        /// </summary>
        [TestCase("\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\"", "i=11")]
        [TestCase("\"uav:dataTypeId\":\"i=12\"", "i=12")]
        [TestCase("", "i=26")]
        public void TheDeclaredDataTypeFollowsTheStatedPrecedence(string terms, string expected)
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\"" +
                (terms.Length == 0 ? string.Empty : "," + terms) + "}");

            Assert.That(declaration.DataType, Is.EqualTo(expected));
        }

        /// <summary>
        /// Declared ArrayDimensions are read only where every element is a
        /// non-negative whole number. A list holding anything else does not
        /// describe dimensions at all, so none are reported rather than a
        /// prefix of the list that happened to parse.
        /// </summary>
        [TestCase("[2,3]", new uint[] { 2, 3 }, TestName = "WellFormed")]
        [TestCase("[2,\"3\"]", new uint[0], TestName = "NotANumber")]
        [TestCase("[2,-1]", new uint[0], TestName = "Negative")]
        [TestCase("[2,4294967296]", new uint[0], TestName = "TooLarge")]
        [TestCase("3", new uint[0], TestName = "NotAList")]
        public void DimensionsAreReadOnlyWhereEveryElementIsOne(
            string dimensions, uint[] expected)
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:valueRank\":1," +
                "\"uav:arrayDimensions\":" + dimensions + "}");

            Assert.That(declaration.ArrayDimensions.ToArray(), Is.EqualTo(expected).AsCollection);
        }

        /// <summary>
        /// A compact name whose prefix the document's <c>@context</c> binds
        /// names the namespace it is bound to, which is how Section 5.1.3's
        /// compact form works everywhere else in the document.
        /// </summary>
        [Test]
        public void ABoundPrefixNamesTheNamespaceItIsBoundTo()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:browseName\":\"other:Velocity\"}");

            Assert.Multiple(() =>
            {
                Assert.That(declaration.NamespaceUri, Is.EqualTo(OtherNamespace));
                Assert.That(declaration.BrowseName, Is.EqualTo("Velocity"));
            });
        }

        /// <summary>
        /// An affordance that is not a DataSchema object states no name, no
        /// type definition, no DataType and no dimensions, so the declaration
        /// it contributes is the bare one its key names. Reading a member out
        /// of a value that has no members would be reading a statement the
        /// document never made.
        /// </summary>
        [Test]
        public void AnAffordanceThatIsNotAnObjectDeclaresOnlyItsKey()
        {
            WotTypeDeclaration declaration = Single("\"Speed\":7");

            Assert.Multiple(() =>
            {
                Assert.That(declaration.NamespaceUri, Is.EqualTo(ModelNamespace));
                Assert.That(declaration.BrowseName, Is.EqualTo("Speed"));
                Assert.That(
                    declaration.TypeDefinitionNodeId,
                    Is.EqualTo(WotVocabulary.BaseDataVariableType));
                Assert.That(declaration.ArrayDimensions, Is.Empty);
                Assert.That(declaration.ModellingRule, Is.EqualTo(WotModellingRule.None));
            });
        }

        /// <summary>
        /// A ValueRank is a whole number of dimensions. A term that is not one
        /// - a word, a fraction, a value past the range - states no rank at
        /// all, so the declaration reports the scalar rank the absence of the
        /// term means rather than a rank read out of half a value.
        /// </summary>
        [TestCase("\"one\"", TestName = "NotANumber")]
        [TestCase("1.5", TestName = "NotAWholeNumber")]
        [TestCase("2147483648", TestName = "PastTheRange")]
        [TestCase("null", TestName = "Absent")]
        public void AValueRankThatIsNotAWholeNumberStatesNone(string valueRank)
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:valueRank\":" + valueRank + "}");

            Assert.That(declaration.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        /// <summary>
        /// A ValueRank that is a whole number is the rank the declaration
        /// states.
        /// </summary>
        [Test]
        public void AWholeNumberValueRankIsTheDeclaredRank()
        {
            WotTypeDeclaration declaration = Single(
                "\"Speed\":{\"type\":\"number\",\"uav:valueRank\":2}");

            Assert.That(declaration.ValueRank, Is.EqualTo(2));
        }

        /// <summary>
        /// A document that states an empty <c>uav:id</c> projects no identity,
        /// and a type with no identity is nothing a <c>tm:extends</c> href
        /// could name, so the index does not hold it.
        /// </summary>
        [Test]
        public void ATypeWithNoIdentityIsNotIndexed()
        {
            var index = new WotDocumentDeclarationIndex();
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(string.Empty, "TankType", "\"Speed\":{\"type\":\"number\"}")));

            index.Add(document);

            Assert.That(index.Resolve(string.Empty, WotDeclarationScope.Effective), Is.Null);
        }

        /// <summary>
        /// Two documents claiming one identity is a conflict the conversion
        /// reports through name resolution. The index keeps the first entry so
        /// that what a declaration view reports never depends on the order the
        /// documents happened to be enumerated in.
        /// </summary>
        [Test]
        public void TheFirstDocumentClaimingAnIdentityKeepsIt()
        {
            var index = new WotDocumentDeclarationIndex();
            using WotDocument first = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(TankTypeId, "TankType", "\"Speed\":{\"type\":\"number\"}")));
            using WotDocument second = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(TankTypeId, "TankType", "\"Serial\":{\"type\":\"string\"}")));

            index.Add(first);
            index.Add(second);

            WotTypeDeclarationSet? set = index.Resolve(
                TankTypeId, WotDeclarationScope.Direct);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(Names(set!.Declarations), Is.EqualTo(s_speedOnly).AsCollection);
            });
        }

        /// <summary>
        /// An alias names the document a <c>tm:extends</c> href reaches it by.
        /// A fragment addresses a part of the document rather than another
        /// document, so it is trimmed; an alias that is nothing but a fragment
        /// names no document and is not indexed; and the first document to
        /// claim an alias keeps it, for the same reason it keeps an identity.
        /// </summary>
        [Test]
        public void AnAliasIsTrimmedOfItsFragmentAndClaimedOnce()
        {
            var index = new WotDocumentDeclarationIndex();
            using WotDocument super = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(
                    "nsu=urn:test:declared;i=1000",
                    "BaseType",
                    "\"Serial\":{\"type\":\"string\"}")));
            using WotDocument other = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(
                    "nsu=urn:test:declared;i=1001",
                    "OtherType",
                    "\"Colour\":{\"type\":\"string\"}")));
            using WotDocument sub = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(
                    TankTypeId,
                    "TankType",
                    "\"Speed\":{\"type\":\"number\"}",
                    supertype: "https://example.com/base#TankBase")));

            index.Add(super, ["https://example.com/base#TankBase", "#TankBase"]);
            index.Add(other, ["https://example.com/base"]);
            index.Add(sub);

            WotTypeDeclarationSet? set = index.Resolve(
                TankTypeId, WotDeclarationScope.Effective);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(
                    Names(set!.Declarations),
                    Is.EqualTo(s_serialThenSpeed).AsCollection,
                    "The fragment is trimmed, so the href names the first claimant.");
                Assert.That(set.IsComplete, Is.True);
                Assert.That(
                    index.Resolve("#TankBase", WotDeclarationScope.Direct),
                    Is.Null,
                    "A bare fragment names no document, so it is not an alias.");
            });
        }

        /// <summary>
        /// The supertype walk is bounded. A chain longer than the bound stops
        /// with the declarations it did read and says why, rather than
        /// presenting a truncated closure as the whole one - a caller that read
        /// it as whole would call a declared member undeclared.
        /// </summary>
        [Test]
        public void AChainDeeperThanTheBoundStopsAndSaysSo()
        {
            const int depth = WotTypeDeclarations.MaxSupertypeDepth + 2;
            var index = new WotDocumentDeclarationIndex();
            var documents = new List<WotDocument>();
            try
            {
                for (int level = 0; level < depth; level++)
                {
                    string id = Level(level);
                    string? supertype = level + 1 < depth ? Level(level + 1) : null;
                    WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                        TypeModel(
                            id,
                            "Level" + level.ToString(CultureInfo.InvariantCulture),
                            "\"P" + level.ToString(CultureInfo.InvariantCulture) +
                                "\":{\"type\":\"number\"}",
                            supertype)));
                    documents.Add(document);
                    index.Add(document);
                }

                WotTypeDeclarationSet? set = index.Resolve(
                    Level(0), WotDeclarationScope.Effective);

                Assert.Multiple(() =>
                {
                    Assert.That(set, Is.Not.Null);
                    Assert.That(set!.IsComplete, Is.False);
                    Assert.That(set.Detail, Does.Contain("exceeded the maximum"));
                    Assert.That(
                        set.Supertypes,
                        Has.Count.EqualTo(WotTypeDeclarations.MaxSupertypeDepth));
                    Assert.That(
                        set.Declarations,
                        Has.Count.EqualTo(WotTypeDeclarations.MaxSupertypeDepth + 1),
                        "Every level the walk did reach contributed its own member.");
                });
            }
            finally
            {
                foreach (WotDocument document in documents)
                {
                    document.Dispose();
                }
            }
        }

        private static readonly string[] s_speedOnly = ["Speed"];

        private static readonly string[] s_serialThenSpeed = ["Serial", "Speed"];

        private static string Level(int level)
        {
            return "nsu=" + ModelNamespace + ";i=" +
                (2000 + level).ToString(CultureInfo.InvariantCulture);
        }

        private static List<string> Names(ArrayOf<WotTypeDeclaration> declarations)
        {
            var names = new List<string>();
            foreach (WotTypeDeclaration declaration in declarations)
            {
                names.Add(declaration.BrowseName);
            }
            return names;
        }

        private static WotTypeDeclaration Single(string properties)
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                TypeModel(TankTypeId, "TankType", properties)));

            bool described = WotNodeSetConverter.TryDescribeTypeDeclarations(
                document,
                out ArrayOf<WotTypeDeclaration> declarations,
                out _);

            Assert.That(described, Is.True);
            Assert.That(declarations, Has.Count.EqualTo(1));
            foreach (WotTypeDeclaration declaration in declarations)
            {
                return declaration;
            }
            throw new InvalidOperationException("unreachable");
        }

        private static string TypeModel(
            string nodeId, string browseName, string properties, string? supertype = null)
        {
            string links = supertype is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"tm:extends\",\"href\":\"" + supertype + "\"}]";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"decl\":\"" + ModelNamespace + "\"," +
                "\"other\":\"" + OtherNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"" + browseName + "\"," +
                "\"uav:browseName\":\"decl:" + browseName + "\"," +
                "\"uav:id\":\"" + nodeId + "\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{" + properties + "}" + links + "}";
        }

        private static string Instance()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"decl\":\"" + ModelNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"decl:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:declared;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Speed\":{\"type\":\"number\"}}}";
        }
    }
}
