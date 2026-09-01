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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The corrections a cross-repository review of the WoT Binding alignment
    /// found: two residue-loss regressions, the standardized vocabulary and its
    /// forward-compatible claims, the scoped record grammar of
    /// <c>uav:nodes</c>, the quantity-kind migration, the compact opaque size
    /// of Annex G.4 and the retained-bytes residue digest of Annex G.2.
    /// </summary>
    /// <remarks>
    /// Each rule is exercised twice where the correction is a behavioural
    /// change: once with a document that satisfies it, and once with one that
    /// breaks it in the way the rule exists to catch. A rule that only ever
    /// sees a conforming document proves that the document conforms, not that
    /// the check would notice if it stopped.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotBindingCorrectionTests
    {
        private static readonly string[] s_propertyOnlyTerms =
        [
            "uav:valueRank",
            "uav:arrayDimensions",
            "minimum",
            "maximum",
            "uav:instrumentRange",
            "uav:engineeringUnits",
            "uav:componentOf",
            "readOnly"
        ];

        private static readonly string[] s_standardizedTerms =
        [
            "uav:referenceType",
            "uav:dataType",
            "uav:inverseName",
            "uav:symmetric"
        ];

        [Test]
        public void AnEventDataSchemaWithoutPropertiesIsPreservedAsResidue()
        {
            using WotDocument document = ParseThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"data\":{\"type\":\"string\",\"description\":\"An opaque payload.\"}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement data = regenerated.Events["AlarmEventType"].GetProperty("data");
            Assert.That(
                data.TryGetProperty("description", out JsonElement description),
                Is.True,
                "The materializer reads data.properties, so a data member without one " +
                "produces no Node and has to be carried verbatim rather than replaced by " +
                "the schema the generator writes.");
            Assert.That(description.GetString(), Is.EqualTo("An opaque payload."));
        }

        [Test]
        public void AnEmptyEventDataSchemaIsPreservedAsResidue()
        {
            using WotDocument document = ParseThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"data\":{\"uav:metadata\":{\"pump:note\":\"kept\"}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement data = regenerated.Events["AlarmEventType"].GetProperty("data");
            Assert.That(
                data.TryGetProperty("uav:metadata", out JsonElement metadata),
                Is.True,
                "An object without 'properties' materializes nothing, so nothing about it " +
                "may be dropped.");
            Assert.That(metadata.GetProperty("pump:note").GetString(), Is.EqualTo("kept"));
        }

        [Test]
        public void AnEventDataSchemaWithPropertiesIsMaterializedAndNotDuplicated()
        {
            using WotDocument document = ParseThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{" +
                "\"Cause\":{\"type\":\"string\",\"uav:browseName\":\"pump:Cause\"}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(
                nodeSet.Items!.Any(i => i.BrowseName?.EndsWith("Cause", StringComparison.Ordinal)
                    == true),
                Is.True,
                "A schema the converter materializes becomes Nodes, and is therefore not " +
                "also carried as residue.");
        }

        [Test]
        public void PropertyOnlyTermsOnAnActionStayResidue()
        {
            using WotDocument document = ParseThingModel(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
                "\"uav:browseName\":\"pump:Reset\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":[3]," +
                "\"minimum\":0,\"maximum\":10," +
                "\"uav:instrumentRange\":{\"minimum\":0,\"maximum\":20}," +
                "\"uav:engineeringUnits\":{\"namespaceUri\":\"urn:test:units\"," +
                "\"unitId\":17,\"displayName\":\"rpm\"}," +
                "\"uav:componentOf\":[\"nsu=urn:test:pump;i=1001\"]," +
                "\"readOnly\":true}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement reset = regenerated.Actions["Reset"];
            Assert.Multiple(() =>
            {
                foreach (string term in s_propertyOnlyTerms)
                {
                    Assert.That(
                        reset.TryGetProperty(term, out _),
                        Is.True,
                        $"'{term}' names a Variable Attribute a Method does not have, so on " +
                        "an action it is outside the mapped domain and shall stay residue.");
                }
            });
        }

        [Test]
        public void PropertyOnlyTermsOnAnEventStayResidue()
        {
            using WotDocument document = ParseThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"uav:valueRank\":1,\"minimum\":0,\"maximum\":10," +
                "\"uav:componentOf\":[\"nsu=urn:test:pump;i=1001\"]," +
                "\"data\":{\"type\":\"object\",\"properties\":{}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement alarm = regenerated.Events["AlarmEventType"];
            Assert.Multiple(() =>
            {
                Assert.That(alarm.TryGetProperty("uav:valueRank", out _), Is.True);
                Assert.That(alarm.TryGetProperty("minimum", out _), Is.True);
                Assert.That(alarm.TryGetProperty("maximum", out _), Is.True);
                Assert.That(alarm.TryGetProperty("uav:componentOf", out _), Is.True);
            });
        }

        [Test]
        public void PropertyOnlyTermsOnAPropertyStayMapped()
        {
            using WotDocument document = ParseThingModel(
                "\"properties\":{\"speed\":{\"@type\":\"uav:variable\",\"type\":\"number\"," +
                "\"uav:browseName\":\"pump:Speed\",\"uav:valueRank\":-1," +
                "\"minimum\":0,\"maximum\":3600}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement speed = regenerated.Properties["Speed"];
            Assert.Multiple(() =>
            {
                Assert.That(speed.GetProperty("minimum").GetDouble(), Is.Zero);
                Assert.That(speed.GetProperty("maximum").GetDouble(), Is.EqualTo(3600));
                Assert.That(
                    CountResidueEntries(nodeSet, "/properties/"),
                    Is.Zero,
                    "On a property the same terms are model facts and come back from the " +
                    "Nodes, so nothing is carried twice.");
            });
        }

        [Test]
        public void AGeneratedDocumentStatesTheRevisionItWasGeneratedAgainst()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateRichNodeSet());

            Assert.That(
                document.RootElement
                    .GetProperty(WotBindingConformance.BindingVersionTerm)
                    .GetString(),
                Is.EqualTo(WotBindingConformance.CurrentRevision),
                "Section 4.1: a generator, unlike a hand author, always knows which " +
                "revision it emitted and shall state it.");
        }

        [Test]
        public void AnAuthoredForwardCompatibleRevisionSurvivesTheRoundTrip()
        {
            using WotDocument document = ParseThingModel(
                "\"uav:bindingVersion\":\"1.9\"," +
                "\"uav:profile\":[\"WoT-Reader\",\"WoT-TimeSeriesReader\"]");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.Multiple(() =>
            {
                Assert.That(
                    regenerated.RootElement
                        .GetProperty(WotBindingConformance.BindingVersionTerm)
                        .GetString(),
                    Is.EqualTo("1.9"),
                    "The author's claim wins over the generator's stamp: a claim a consumer " +
                    "shall preserve is not one it may overwrite.");
                Assert.That(
                    regenerated.RootElement
                        .GetProperty(WotBindingConformance.ProfileTerm)
                        .EnumerateArray()
                        .Select(e => e.GetString()),
                    Does.Contain("WoT-TimeSeriesReader"));
            });
        }

        [Test]
        public void AGeneratedRevisionClaimIsNotCarriedTwice()
        {
            using WotDocument document = ParseThingModel(
                "\"uav:bindingVersion\":\"" + WotBindingConformance.CurrentRevision + "\"");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(
                CountResidueEntries(nodeSet, "/" + WotBindingConformance.BindingVersionTerm),
                Is.Zero,
                "A claim that agrees with the stamp is re-derived and shall not also be " +
                "stated as residue.");
        }

        [Test]
        public void TheVocabularyCoversEveryTermTheContextMints()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotBindingConformance.VocabularyTerms.Count,
                    Is.EqualTo(115),
                    "The published @context of revision 1.1 mints 115 uav IRIs.");
                Assert.That(
                    WotBindingConformance.ScopedTerms.Count,
                    Is.EqualTo(13),
                    "Thirteen of them are minted under a short member name inside a scoped " +
                    "context and are never spelled with the prefix in a document.");
                foreach (string scoped in WotBindingConformance.ScopedTerms)
                {
                    Assert.That(
                        WotBindingConformance.IsScopedTerm(scoped), Is.True, scoped);
                    Assert.That(
                        WotBindingConformance.IsKnownTerm(scoped),
                        Is.False,
                        $"'{scoped}' is vocabulary, but a document never writes it as a " +
                        "member name, so it is not a known member term.");
                }
                foreach (string term in s_standardizedTerms)
                {
                    Assert.That(
                        WotBindingConformance.IsKnownTerm(term),
                        Is.True,
                        $"'{term}' is standardized by revision 1.1.");
                }
            });
        }

        [Test]
        public void ANodeRecordMemberNameIsNotATopLevelTerm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotBindingConformance.NodesVocabularyNamespace,
                    Is.EqualTo("http://opcfoundation.org/UA/WoT-Binding/nodes/"),
                    "The uav:nodes record grammar has a @vocab of its own so a UANodeSet " +
                    "field name neither enters the vocabulary nor collides with a class " +
                    "annotation of the same spelling.");
                Assert.That(
                    WotBindingConformance.IsKnownTerm("nodeClass"), Is.False);
                Assert.That(
                    WotBindingConformance.IsKnownTerm("browseName"), Is.False);
                Assert.That(
                    WotBindingConformance.IsKnownTerm("uav:dataType"),
                    Is.True,
                    "The class annotation keeps its own IRI; a node record's dataType " +
                    "member is a different name in a different namespace.");
            });
        }

        [Test]
        public void ANativeProjectionRecordGrammarIsNotReportedAsUnknownVocabulary()
        {
            using WotDocument document = ParseThingModel(
                "\"uav:nodes\":{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"namespaceUris\":[\"urn:test:pump\"],\"nodes\":[" +
                "{\"nodeClass\":\"UAVariable\",\"nodeId\":\"ns=1;i=7001\"," +
                "\"browseName\":\"1:Extra\",\"dataType\":\"i=11\"," +
                "\"references\":[{\"referenceType\":\"i=47\",\"isForward\":false," +
                "\"value\":\"ns=1;i=1001\"}]}]}");

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                Is.False,
                "The record members are a versioned record grammar and are resolved through " +
                "their own namespace table (Sections 7 and 10.1). " +
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AQuantityKindInUnitIsDeprecatedForAConsumerAndInvalidForAnAuthor()
        {
            const string members =
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"pump:Speed\"," +
                "\"unit\":\"qudt-quantitykind:AngularVelocity\"}}";

            WotConversionResult<UANodeSet> consumer = Convert(members, new WotNodeSetConverterOptions());
            WotConversionResult<UANodeSet> authoring = Convert(
                members,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict,
                    AuthoringValidation = true
                });

            Assert.Multiple(() =>
            {
                Assert.That(
                    consumer.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.QuantityKindInUnit &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                    Is.True,
                    "Revision 1.0 permitted it, so a consumer reports the value as deprecated " +
                    "and preserves it.");
                Assert.That(
                    consumer.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    string.Join("; ", consumer.Diagnostics.Select(d => d.Message)));
                Assert.That(
                    authoring.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.QuantityKindInUnit &&
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.True,
                    "Strict authoring against revision 1.1 rejects it.");
            });
        }

        [Test]
        public void AnEngineeringUnitInUnitIsNotReportedAsAQuantityKind()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"pump:Speed\",\"unit\":\"rpm\"," +
                "\"qudt:hasQuantityKind\":\"qudt-quantitykind:AngularVelocity\"}}",
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict,
                    AuthoringValidation = true
                });

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.QuantityKindInUnit),
                Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void TheMigrationHelperMovesAQuantityKindAndInventsNoUnit()
        {
            using WotDocument document = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"unit\":\"qudt-quantitykind:AngularVelocity\"}}");

            WotUnitMigrationResult migration = WotUnitMigration.MoveQuantityKinds(document);

            Assert.That(migration.Changed, Is.True);
            using WotDocument migrated = WotDocument.Parse(migration.Document!);
            JsonElement speed = migrated.Properties["speed"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    speed.TryGetProperty("unit", out _),
                    Is.False,
                    "No unit is invented in the vacated member: AngularVelocity is measured " +
                    "in rpm and in rad/s alike.");
                Assert.That(
                    speed.GetProperty(WotUnitMigration.QuantityKindTerm).GetString(),
                    Is.EqualTo("qudt-quantitykind:AngularVelocity"));
                Assert.That(
                    migration.MovedPointers.ToArray(),
                    Does.Contain("/properties/speed/unit"));
            });
        }

        [Test]
        public void TheMigrationHelperLeavesAConflictingQuantityKindAlone()
        {
            using WotDocument document = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"unit\":\"qudt-quantitykind:AngularVelocity\"," +
                "\"qudt:hasQuantityKind\":\"qudt-quantitykind:Frequency\"}}");

            WotUnitMigrationResult migration = WotUnitMigration.MoveQuantityKinds(document);

            Assert.Multiple(() =>
            {
                Assert.That(migration.Changed, Is.False);
                Assert.That(
                    migration.ConflictPointers.ToArray(),
                    Does.Contain("/properties/speed/unit"),
                    "Two quantity kinds are two facts, and choosing between them is the " +
                    "author's decision.");
            });
        }

        [Test]
        public void TheOpaqueSizeIsMeasuredOverTheCompactReceivedForm()
        {
            // Annex G.4 removes insignificant whitespace and changes nothing
            // else: member order, number spellings and string escapes are the
            // received ones. An almost-JCS measurement would re-sort the two
            // members and reformat 1.0 and 1e3, and two implementations would
            // then disagree about the number.
            using WotDocument document = ParseThingModel(
                "\"uav:metadata\":{\n  \"pump:b\" : 1.0,\n  \"pump:a\" : 1e3\n}");

            JsonElement metadata = document.RootElement.GetProperty("uav:metadata");

            Assert.That(
                WotDocument.MeasureCompactUtf8(metadata),
                Is.EqualTo(Encoding.UTF8.GetByteCount("{\"pump:b\":1.0,\"pump:a\":1e3}")),
                "The compact received form is the received text with insignificant " +
                "whitespace removed, and nothing else.");
        }

        [Test]
        public void TheOpaqueSizeCountsNonAsciiInUtf8Octets()
        {
            using WotDocument document = ParseThingModel(
                "\"uav:metadata\":{\"pump:a\":\"\u00e4\u20ac\ud83d\ude00\"}");

            JsonElement metadata = document.RootElement.GetProperty("uav:metadata");

            Assert.That(
                WotDocument.MeasureCompactUtf8(metadata),
                Is.EqualTo(Encoding.UTF8.GetByteCount("{\"pump:a\":\"\u00e4\u20ac\ud83d\ude00\"}")),
                "The measured size is the length in octets of the UTF-8 encoding: two for " +
                "U+00E4, three for U+20AC and four for the supplementary scalar.");
        }

        [Test]
        public void TheOpaqueSizeKeepsWhitespaceInsideAStringLiteral()
        {
            using WotDocument document = ParseThingModel(
                "\"uav:metadata\":{ \"pump:a\" : \"a b\\tc\" }");

            JsonElement metadata = document.RootElement.GetProperty("uav:metadata");

            Assert.That(
                WotDocument.MeasureCompactUtf8(metadata),
                Is.EqualTo(Encoding.UTF8.GetByteCount("{\"pump:a\":\"a b\\tc\"}")),
                "Whitespace is insignificant only outside a string literal, and an escape " +
                "keeps the spelling it was written with.");
        }

        [Test]
        public void AnOpaqueObjectAtTheBoundIsNotReported()
        {
            (string members, int measured) = OpaqueOfSize(WotBindingConformance.OpaqueMaxOctets);

            WotConversionResult<UANodeSet> result = Convert(
                members,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(measured, Is.EqualTo(WotBindingConformance.OpaqueMaxOctets));
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.OpaqueObjectInvalid),
                Is.False,
                "The bound is inclusive: 65 536 octets is within it. " +
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AnOpaqueObjectOneOctetOverTheBoundIsReported()
        {
            (string members, int measured) = OpaqueOfSize(WotBindingConformance.OpaqueMaxOctets + 1);

            WotConversionResult<UANodeSet> result = Convert(
                members,
                new WotNodeSetConverterOptions
                {
                    ConformanceMode = WotConformanceMode.Strict
                });

            Assert.That(measured, Is.EqualTo(WotBindingConformance.OpaqueMaxOctets + 1));
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.OpaqueObjectInvalid),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void TheResidueDigestIsOverTheRetainedBytes()
        {
            using WotDocument document = ParseThingModel(
                "\"pump:vendorNote\":{\"b\":1.0,\"a\":\"kept\"}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            (string json, string digest) = ResidueEntry(nodeSet, "/pump:vendorNote");
            Assert.Multiple(() =>
            {
                Assert.That(
                    json,
                    Is.EqualTo("{\"b\":1.0,\"a\":\"kept\"}"),
                    "Section 6.6 forbids a consumer to reorder or reformat a value it " +
                    "preserves, so the retained bytes are the ones the author wrote.");
                Assert.That(
                    digest,
                    Is.EqualTo(Sha256Hex(Encoding.UTF8.GetBytes(json))),
                    "Annex G.2: the Sha256 of a WoTJsonResidue member is the digest of the " +
                    "decoded bytes exactly, and never of a re-serialization.");
            });
        }

        [Test]
        public void ACorruptedResidueDigestIsReported()
        {
            using WotDocument document = ParseThingModel(
                "\"pump:vendorNote\":{\"a\":\"kept\"}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);
            System.Xml.XmlElement member = ResidueMembers(nodeSet)
                .Single(m => string.Equals(
                    m.GetAttribute("Pointer"), "/pump:vendorNote", StringComparison.Ordinal));
            member.SetAttribute("Sha256", new string('0', 64));

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(nodeSet);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True,
                "A mismatch is a mismatch: the entry is corrupt, not merely differently " +
                "spelled.");
        }

        [Test]
        public void ADocumentThatProjectsAReferenceTypeCarriesTheTwoAttributes()
        {
            WotConversionResult<UANodeSet> result = ConvertReferenceType(
                "\"uav:inverseName\":\"MaterialReferencedBy\",\"uav:symmetric\":false");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ReferenceTypeProjectionInvalid),
                Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void ASymmetricReferenceTypeShallNotNameAnInverseDirection()
        {
            WotConversionResult<UANodeSet> result = ConvertReferenceType(
                "\"uav:inverseName\":\"MaterialReferencedBy\",\"uav:symmetric\":true");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ReferenceTypeProjectionInvalid),
                Is.True,
                "A symmetric Reference reads the same in both directions, so a second name " +
                "states a direction that does not exist.");
        }

        [Test]
        public void TheReferenceTypeAttributesBelongOnlyToAReferenceTypeProjection()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:inverseName\":\"PumpedBy\"",
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ReferenceTypeProjectionInvalid),
                Is.True,
                "Both terms carry ReferenceType Attributes, so they belong to a document " +
                "that projects a ReferenceType Node and to no other.");
        }

        [Test]
        public void ATypeCarriesAtMostOneNodeClassAnnotation()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\",\"uav:referenceType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"}");
            using WotDocument document = WotDocument.Parse(json);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document, new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NodeClassAnnotationConflict),
                Is.True,
                "A Node has exactly one NodeClass.");
        }

        [Test]
        public void TwoSelectClausesThatNormalizeToOnePathAreRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"Severity\"}]}}",
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.EventSelectClauseInvalid),
                Is.True,
                "Uniqueness is stated over the normalized path and not over the " +
                "typeDefinitionId-and-path pair: the path alone decides the output member " +
                "(Section 6.1).");
        }

        [Test]
        public void TwoPrefixesForOneNamespaceSelectOnePath()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\",\"p2\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:Trace\"}," +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"p2:Trace\"}]}}}");
            using WotDocument document = WotDocument.Parse(json);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document, new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.EventSelectClauseInvalid),
                Is.True,
                "Normalization resolves each element's prefix to the NamespaceUri the " +
                "document binds it to, so two prefixes for one namespace name one path.");
        }

        [Test]
        public void TwoDistinctPathsUnderDifferentEventTypesAreAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"LastSeverity\"}]}}",
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.EventSelectClauseInvalid),
                Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AConditionAffordanceSelectsTheEventIdItDeclares()
        {
            WotConversionResult<UANodeSet> result = Convert(ConditionEvent(
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}]"),
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionEventIdMissing),
                Is.True,
                "A complete list replaces the documented default rather than extending it, " +
                "so a list that omits EventId describes a notification that never carries " +
                "the one field Section 13.3 requires.");
        }

        [Test]
        public void AConditionAffordanceThatSelectsEventIdIsValid()
        {
            WotConversionResult<UANodeSet> result = Convert(ConditionEvent(
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"EventId\"}," +
                "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"\"}]"),
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionEventIdMissing),
                Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AConditionAffordanceNeedsNoOtherConditionFieldSelected()
        {
            WotConversionResult<UANodeSet> result = Convert(ConditionEvent(
                "\"uav:eventSelectClauses\":[" +
                "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"EventId\"}]"),
                new WotNodeSetConverterOptions());

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False,
                "EventId is the one hard requirement; every other Condition field is present " +
                "where the affordance selects it. " +
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void TheCodePointComparerIsTheOneAnnexG3Order()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotCodePointComparer.Instance.Compare("\ud83d\ude00", "\uff20"),
                    Is.GreaterThan(0),
                    "U+1F600 is above U+FF20 by code point, and an ordinal comparison of " +
                    "UTF-16 code units says the opposite.");
                Assert.That(
                    string.CompareOrdinal("\ud83d\ude00", "\uff20"),
                    Is.LessThan(0),
                    "Which is exactly why one shared implementation exists.");
                Assert.That(WotCodePointComparer.Instance.Compare("de", "fr"), Is.LessThan(0));
            });
        }

        private static string ConditionEvent(string clauses)
        {
            return "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":true," +
                "\"uav:browseName\":\"pump:AlarmEventType\"," +
                "\"uav:conditionType\":\"ua:ConditionType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{" +
                "\"EventId\":{\"type\":\"string\"," +
                "\"contentEncoding\":\"base64\"}}}," +
                clauses + "}}";
        }

        private static (string Members, int Measured) OpaqueOfSize(int octets)
        {
            const string prefix = "{\"pump:pad\":\"";
            const string suffix = "\"}";
            int padding = octets - prefix.Length - suffix.Length;
            string value = prefix + new string('x', padding) + suffix;
            using JsonDocument parsed = JsonDocument.Parse(value);
            return ("\"uav:metadata\":" + value,
                (int)WotDocument.MeasureCompactUtf8(parsed.RootElement));
        }

        private static IEnumerable<System.Xml.XmlElement> ResidueMembers(UANodeSet nodeSet)
        {
            foreach (System.Xml.XmlElement extension in nodeSet.Extensions ?? [])
            {
                if (!string.Equals(
                    extension.NamespaceURI,
                    WotBindingConformance.VocabularyNamespace,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (System.Xml.XmlNode child in extension.ChildNodes)
                {
                    if (child is System.Xml.XmlElement member)
                    {
                        yield return member;
                    }
                }
            }
        }

        private static (string Json, string Digest) ResidueEntry(
            UANodeSet nodeSet, string pointer)
        {
            System.Xml.XmlElement member = ResidueMembers(nodeSet)
                .Single(m => string.Equals(
                    m.GetAttribute("Pointer"), pointer, StringComparison.Ordinal));
            return (
                Encoding.UTF8.GetString(System.Convert.FromBase64String(member.InnerText)),
                member.GetAttribute("Sha256"));
        }

        private static int CountResidueEntries(UANodeSet nodeSet, string pointerPrefix)
        {
            return ResidueMembers(nodeSet).Count(m =>
                m.GetAttribute("Pointer").StartsWith(pointerPrefix, StringComparison.Ordinal));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            var builder = new StringBuilder(64);
            foreach (byte value in ComputeSha256(bytes))
            {
                builder.Append(value.ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static byte[] ComputeSha256(byte[] bytes)
        {
#if NET6_0_OR_GREATER
            return System.Security.Cryptography.SHA256.HashData(bytes);
#else
            using System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(bytes);
#endif
        }

        private static WotConversionResult<UANodeSet> ConvertReferenceType(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:referenceType\"]," +
                "\"title\":\"MaterialReference\"," +
                "\"uav:browseName\":\"pump:MaterialReference\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                members + "}");
            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document, new WotNodeSetConverterOptions());
        }

        private static WotConversionResult<UANodeSet> Convert(
            string members, WotNodeSetConverterOptions options)
        {
            using WotDocument document = ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document, options);
        }

        internal static WotDocument ParseThingModel(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"qudt\":\"http://qudt.org/schema/qudt/\"," +
                "\"qudt-quantitykind\":\"http://qudt.org/vocab/quantitykind/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members + "}");
            return WotDocument.Parse(json);
        }
    }
}
