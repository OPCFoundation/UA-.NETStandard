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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Validates the document-level conformance vocabulary of the WoT Binding:
    /// the event select-clause list of Section 6.1, the vocabulary revision and
    /// conformance claims of Section 4.1, the structural bounds on opaque
    /// objects of Section 6.6 and the <c>auto</c> endpoint security floor of
    /// Section 5.7.1, in both the permissive and the strict conformance mode.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotBindingConformanceTests
    {
        [Test]
        public void TheImplicitDefaultSelectsTheEightMandatoryBaseEventTypeFields()
        {
            Assert.That(
                WotEventSelectClauses.Default.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_mandatoryBaseEventTypeFields));
            Assert.That(
                WotEventSelectClauses.Default.ToList().All(c =>
                    c.TypeDefinitionId == WotEventSelectClauses.BaseEventTypeId),
                Is.True,
                "Every default clause is declared by BaseEventType.");
        }

        [Test]
        public void StandardSelectClausesProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}," +
                "{\"tm:ref\":\"./condition.tm.jsonld\",\"uav:browsePath\":\"EnabledState/Id\"}," +
                "{\"tm:ref\":\"./condition.tm.jsonld\",\"uav:browsePath\":\"\"}," +
                "{\"tm:ref\":\"./pump-event.tm.jsonld\"," +
                "\"uav:browsePath\":\"pump:Temperature\"}"));

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventSelectClauseInvalid),
                Is.False,
                Describe(result));
        }

        [Test]
        public void EmptyBrowsePathIsTheConditionIdSelection()
        {
            var clause = new WotEventSelectClause("./condition.tm.jsonld", string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(clause.IsConditionIdSelection, Is.True);
                Assert.That(clause.FieldName, Is.EqualTo("ConditionId"));
            });
        }

        [Test]
        public void NestedBrowsePathNamesItsLastElement()
        {
            var clause = new WotEventSelectClause("./condition.tm.jsonld", "EnabledState/Id");

            Assert.Multiple(() =>
            {
                Assert.That(clause.IsConditionIdSelection, Is.False);
                Assert.That(clause.FieldName, Is.EqualTo("Id"));
            });
        }

        [Test]
        public void AbsoluteSelectClausePathIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"/EventId\"}"));

            AssertSelectClauseError(result, "absolute");
        }

        [Test]
        public void EmptySelectClauseArrayIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(string.Empty));

            AssertSelectClauseError(result, "empty");
        }

        [Test]
        public void SelectClauseCarryingAWhereClauseIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"," +
                "\"uav:whereClause\":{\"op\":\"GreaterThan\"}}"));

            AssertSelectClauseError(result, "WhereClause");
        }

        [Test]
        public void RepeatedSelectClauseIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}," +
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}"));

            AssertSelectClauseError(result, "twice");
        }

        [Test]
        public void SelectClauseMissingItsTypeDefinitionIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"uav:browsePath\":\"EventId\"}"));

            AssertSelectClauseError(result, "tm:ref");
        }

        [Test]
        public void SelectClausesOnAPropertyAffordanceAreRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:eventSelectClauses\":[{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                "\"uav:browsePath\":\"EventId\"}]}}");

            AssertSelectClauseError(result, "belongs only directly on an event affordance");
        }

        [Test]
        public void SelectClausesAtTheDocumentRootAreRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:eventSelectClauses\":[{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                "\"uav:browsePath\":\"EventId\"}]");

            AssertSelectClauseError(result, "belongs only directly on an event affordance");
        }

        /// <summary>
        /// A clause names its EventType by reference and never by NodeId, so
        /// the shape that carried one is rejected as an unexpected member
        /// rather than being read as a second spelling of the same fact
        /// (WoT Binding Section 6.1).
        /// </summary>
        [Test]
        public void ASelectClauseCarryingANodeIdIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"uav:typeDefinitionId\":\"nsu=urn:test:pump;i=6001\"," +
                "\"uav:browsePath\":\"Temperature\"}"));

            AssertSelectClauseError(result, "uav:typeDefinitionId");
        }

        [Test]
        public void NumericNamespacePrefixInASelectClausePathIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"2:Temperature\"}"));

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonPortableQualifiedName),
                Is.True,
                Describe(result));
        }

        [Test]
        public async Task SelectClausesSurviveWotToNodeSetToWotAsync()
        {
            using WotDocument original = ParseThingModel(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}," +
                "{\"tm:ref\":\"./condition.tm.jsonld\",\"uav:browsePath\":\"\"}"));

            UANodeSet nodeSet = await ConvertResolvedAsync(original).ConfigureAwait(false);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement affordance = restored.Events.Values.Single();
            JsonElement clauses = affordance.GetProperty(WotEventSelectClauses.Term);
            Assert.Multiple(() =>
            {
                Assert.That(clauses.GetArrayLength(), Is.EqualTo(2));
                Assert.That(
                    clauses[0].GetProperty("tm:ref").GetString(),
                    Is.EqualTo("./base-event.tm.jsonld"));
                Assert.That(clauses[1].GetProperty("uav:browsePath").GetString(), Is.Empty);
            });
        }

        [Test]
        public async Task ADocumentCarryingTheNewTermsImportsAsANodeSetAsync()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:bindingVersion\":\"1.1\",\"uav:profile\":[\"WoT-Modeller\"]," +
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"," +
                "\"uav:securityPolicy\":\"Basic256Sha256\"}}}," +
                "\"security\":\"opcua_auto_sc\"," +
                EventWithClauses(
                    "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}"));

            UANodeSet nodeSet = await ConvertResolvedAsync(original).ConfigureAwait(false);
            byte[] serialized = WotTestData.Serialize(nodeSet);
            using var stream = new System.IO.MemoryStream(serialized);
            UANodeSet reread = UANodeSet.Read(stream);

            Assert.That(reread.Items, Is.Not.Null);
            Assert.DoesNotThrow(() => WotNodeSetConverter.FromNodeSet(reread).Dispose());
        }

        /// <summary>
        /// An affordance that states a selection of WoT Binding Section 6.1
        /// names EventType definitions that live in other documents, so it
        /// converts through the asynchronous path that holds them and is
        /// reported by the synchronous one that does not.
        /// </summary>
        [Test]
        public void SelectClausesWithoutAResolverAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(EventWithClauses(
                "{\"tm:ref\":\"./base-event.tm.jsonld\",\"uav:browsePath\":\"EventId\"}"));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.EventSelectionUnresolved &&
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                Describe(result));
        }

        [Test]
        public void ValidRevisionAndProfileClaimsProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:bindingVersion\":\"1.1\"," +
                "\"uav:profile\":[\"WoT-Reader\",\"WoT-ModelVocabulary\"]",
                Strict());

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code is WotDiagnosticCode.InvalidBindingVersion
                        or WotDiagnosticCode.InvalidConformanceClaim),
                Is.False,
                Describe(result));
        }

        [Test]
        public void MalformedRevisionIsRejectedInEveryMode()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:bindingVersion\":\"one.one\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidBindingVersion),
                Is.True,
                Describe(result));
        }

        [Test]
        public void UnimplementedRevisionIsUnsupportedForAConsumerAndInvalidForAnAuthor()
        {
            WotConversionResult<UANodeSet> permissive = Convert("\"uav:bindingVersion\":\"9.9\"");
            WotConversionResult<UANodeSet> strict =
                Convert("\"uav:bindingVersion\":\"9.9\"", Strict());
            WotConversionResult<UANodeSet> authoring =
                Convert("\"uav:bindingVersion\":\"9.9\"", Authoring());

            Assert.Multiple(() =>
            {
                Assert.That(
                    permissive.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    "Section 4.1 forbids rejecting a document for declaring an unimplemented " +
                    "revision. " + Describe(permissive));
                Assert.That(
                    permissive.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnsupportedBindingRevision &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                    Is.True,
                    "A consumer reports the value as unsupported rather than invalid. " +
                    Describe(permissive));
                Assert.That(
                    strict.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    "Strict conformance reports unknown terms; it is still a consumer, and a " +
                    "syntactically valid future revision is not an error on its own. " +
                    Describe(strict));
                Assert.That(
                    authoring.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnsupportedBindingRevision &&
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.True,
                    "An authoring validator holds a document to a published revision. " +
                    Describe(authoring));
            });
        }

        [Test]
        public void BothPublishedRevisionsAreAccepted()
        {
            WotConversionResult<UANodeSet> previous =
                Convert("\"uav:bindingVersion\":\"1.0\"", Authoring());
            WotConversionResult<UANodeSet> current =
                Convert("\"uav:bindingVersion\":\"1.1\"", Authoring());

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotBindingConformance.SupportedRevisions.ToArray(),
                    Is.EqualTo(s_publishedBindingRevisions).AsCollection);
                Assert.That(
                    previous.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnsupportedBindingRevision),
                    Is.False,
                    Describe(previous));
                Assert.That(
                    current.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnsupportedBindingRevision),
                    Is.False,
                    Describe(current));
            });
        }

        /// <summary>
        /// A document that binds the <c>uav</c> prefix to the superseded
        /// <c>http://opcfoundation.org/UA/WoT/v1#</c> IRI still reads exactly
        /// like a current one.
        /// </summary>
        /// <remarks>
        /// The Binding places the binding obligation on the author and defines
        /// no consumer-rejection rule, so the reader matches the compact
        /// <c>uav:</c> spelling and never consults the prefix IRI. That is
        /// deliberate, and it is what keeps documents written against the
        /// earlier draft readable. Strict conformance does not change it:
        /// strictness is about the vocabulary a document uses, not about the
        /// IRI it declares the vocabulary under. New documents should
        /// nevertheless bind <c>uav</c> to
        /// <see cref="WotBindingConformance.VocabularyNamespace"/>.
        /// </remarks>
        [Test]
        public void SupersededVocabularyIriStillReadsInEveryMode()
        {
            const string members =
                "\"uav:bindingVersion\":\"1.1\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"pump:Speed\"}}";

            WotConversionResult<UANodeSet> current = Convert(members);
            WotConversionResult<UANodeSet> legacy = ConvertWithLegacyVocabularyIri(members);
            WotConversionResult<UANodeSet> legacyStrict =
                ConvertWithLegacyVocabularyIri(members, Strict());

            Assert.Multiple(() =>
            {
                Assert.That(legacy.Value, Is.Not.Null, Describe(legacy));
                Assert.That(legacyStrict.Value, Is.Not.Null, Describe(legacyStrict));
                Assert.That(
                    legacyStrict.Diagnostics.Any(d =>
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    "The prefix IRI is not part of the vocabulary strictness rule. " +
                    Describe(legacyStrict));
                Assert.That(
                    legacy.Value!.Items!.Select(i => i.BrowseName),
                    Is.EqualTo(current.Value!.Items!.Select(i => i.BrowseName)).AsCollection,
                    "The superseded IRI changes nothing about how the document reads.");
            });
        }

        [Test]
        public void UnknownProfileNameIsUnrecognizedForAConsumerAndInvalidForAnAuthor()
        {
            WotConversionResult<UANodeSet> consumer = Convert("\"uav:profile\":[\"WoT-Wizard\"]");
            WotConversionResult<UANodeSet> authoring =
                Convert("\"uav:profile\":[\"WoT-Wizard\"]", Authoring());

            Assert.Multiple(() =>
            {
                Assert.That(
                    consumer.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    "A later revision defines further units, so a consumer that rejected the " +
                    "claim would refuse a document it can otherwise read. " + Describe(consumer));
                Assert.That(
                    consumer.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnrecognizedConformanceClaim &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                    Is.True,
                    Describe(consumer));
                Assert.That(
                    authoring.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnrecognizedConformanceClaim &&
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.True,
                    Describe(authoring));
            });
        }

        [Test]
        public void MalformedProfileNameIsRejectedInEveryMode()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:profile\":[\"Wizard\"]");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidConformanceClaim &&
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                "The syntactic rule is what a consumer enforces, and it enforces it always. " +
                Describe(result));
        }

        [Test]
        public void EmptyProfileArrayIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:profile\":[]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidConformanceClaim),
                Is.True,
                Describe(result));
        }

        [Test]
        public void MissingRequiredClaimIsReportedInStrictMode()
        {
            var options = Strict();
            options.RequiredConformance = s_eventMappingRequirement;

            WotConversionResult<UANodeSet> missing = Convert("\"uav:profile\":[\"WoT-Reader\"]", options);
            WotConversionResult<UANodeSet> claimed =
                Convert("\"uav:profile\":[\"WoT-Modeller\"]", options);

            Assert.Multiple(() =>
            {
                Assert.That(
                    missing.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.InvalidConformanceClaim),
                    Is.True,
                    "WoT-Reader names neither WoT-EventMapping nor a profile that does.");
                Assert.That(
                    claimed.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.InvalidConformanceClaim),
                    Is.False,
                    "Claiming WoT-Modeller claims every unit it names, WoT-EventMapping among " +
                    "them: " + Describe(claimed));
            });
        }

        [Test]
        public void RequiredClaimIsNotEnforcedPermissively()
        {
            var options = new WotNodeSetConverterOptions
            {
                RequiredConformance = s_eventMappingRequirement
            };

            WotConversionResult<UANodeSet> result = Convert("\"uav:profile\":[\"WoT-Reader\"]", options);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidConformanceClaim),
                Is.False,
                Describe(result));
        }

        [Test]
        public void RevisionAndProfileClaimsSurviveWotToNodeSetToWot()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:bindingVersion\":\"1.1\",\"uav:profile\":[\"WoT-Converter\"]");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.Multiple(() =>
            {
                Assert.That(
                    restored.RootElement.GetProperty("uav:bindingVersion").GetString(),
                    Is.EqualTo("1.1"));
                Assert.That(
                    restored.RootElement.GetProperty("uav:profile")[0].GetString(),
                    Is.EqualTo("WoT-Converter"));
            });
        }

        [Test]
        public void ClaimsNeverBecomeNodes()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:bindingVersion\":\"1.1\",\"uav:profile\":[\"WoT-Reader\"]");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            Assert.That(
                nodeSet.Items?.Any(node =>
                    node.BrowseName is not null &&
                    (node.BrowseName.Contains("bindingVersion", StringComparison.Ordinal) ||
                        node.BrowseName.Contains("profile", StringComparison.Ordinal))),
                Is.False,
                "A document-level claim describes the document, not the AddressSpace.");
        }

        [Test]
        public void UnknownVocabularyTermIsCarriedPermissivelyAndReportedStrictly()
        {
            WotConversionResult<UANodeSet> permissive = Convert("\"uav:futureTerm\":\"value\"");
            WotConversionResult<UANodeSet> strict = Convert("\"uav:futureTerm\":\"value\"", Strict());

            Assert.Multiple(() =>
            {
                Assert.That(
                    permissive.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                    Is.False);
                Assert.That(
                    strict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                    Is.True,
                    Describe(strict));
            });
        }

        [Test]
        public void MisspelledTermOnAnAffordanceIsReportedStrictly()
        {
            WotConversionResult<UANodeSet> strict = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"uav:browsname\":\"pump:Speed\"}}",
                Strict());

            Assert.That(
                strict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                Is.True,
                Describe(strict));
        }

        [Test]
        public void TheSupersededEventFieldsSpellingIsUnknownToStrictConformance()
        {
            WotConversionResult<UANodeSet> strict = Convert(
                "\"events\":{\"alarm\":{" +
                "\"uav:eventFields\":[\"LocalTime\"]}}",
                Strict());

            Assert.That(
                strict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                Is.True,
                Describe(strict));
        }

        [Test]
        public void EveryTermTheKnownSetHoldsIsRecognized()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WotBindingConformance.IsKnownTerm("uav:eventSelectClauses"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("tm:ref"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:bindingVersion"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:profile"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:minimumSecurity"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:instrumentRange"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:engineeringUnits"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:valueRank"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:arrayDimensions"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:inverseName"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:symmetric"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:fieldOrder"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:structureType"), Is.True);
                Assert.That(WotBindingConformance.IsKnownTerm("uav:eventFields"), Is.False);
                Assert.That(
                    WotBindingConformance.IsKnownTerm("uav:typeDefinitionId"),
                    Is.False,
                    "The NodeId clause form was removed; a clause names its EventType with " +
                    "tm:ref (WoT Binding Section 6.1).");
            });
        }

        [Test]
        public void NamespacedOpaqueKeysProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:metadata\":{\"pump:revision\":3," +
                "\"http://example.com/vocab/maintainer\":\"Modeling WG\"}",
                Strict());

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.OpaqueObjectInvalid),
                Is.False,
                Describe(result));
        }

        [Test]
        public void UnnamespacedOpaqueKeyWarnsPermissivelyAndFailsStrictly()
        {
            WotConversionResult<UANodeSet> permissive = Convert("\"uav:metadata\":{\"revision\":3}");
            WotConversionResult<UANodeSet> strict =
                Convert("\"uav:metadata\":{\"revision\":3}", Strict());

            Assert.Multiple(() =>
            {
                Assert.That(permissive.Value, Is.Not.Null, "The value is preserved, not rejected.");
                Assert.That(
                    permissive.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                    Is.True,
                    Describe(permissive));
                Assert.That(
                    strict.Diagnostics.Any(d =>
                        d.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                        d.Severity == WotDiagnosticSeverity.Error),
                    Is.True,
                    Describe(strict));
            });
        }

        [Test]
        public void UnboundOpaqueKeyPrefixIsReported()
        {
            WotConversionResult<UANodeSet> result =
                Convert("\"uav:metadata\":{\"vendor:revision\":3}", Strict());

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.OpaqueObjectInvalid),
                Is.True,
                Describe(result));
        }

        [Test]
        public void OpaqueObjectExceedingTheKeyBoundIsReported()
        {
            var builder = new StringBuilder("\"uav:eventConfiguration\":{");
            for (int ii = 0; ii <= WotBindingConformance.OpaqueMaxTopLevelKeys; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(',');
                }
                builder.Append("\"pump:k").Append(ii).Append("\":1");
            }
            builder.Append('}');

            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"alarm\":{" + builder + "}}", Strict());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                    d.Message.Contains("top-level keys", StringComparison.Ordinal)),
                Is.True,
                Describe(result));
        }

        [Test]
        public void OpaqueObjectExceedingTheDepthBoundIsReported()
        {
            var builder = new StringBuilder("\"uav:metadata\":{\"pump:deep\":");
            for (int ii = 0; ii < WotBindingConformance.OpaqueMaxDepth; ii++)
            {
                builder.Append("{\"pump:n\":");
            }
            builder.Append('1');
            for (int ii = 0; ii < WotBindingConformance.OpaqueMaxDepth; ii++)
            {
                builder.Append('}');
            }
            builder.Append('}');

            WotConversionResult<UANodeSet> result = Convert(builder.ToString(), Strict());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                    d.Message.Contains("levels deep", StringComparison.Ordinal)),
                Is.True,
                Describe(result));
        }

        [Test]
        public void OpaqueObjectExceedingTheSizeBoundIsReported()
        {
            string padding = new('x', WotBindingConformance.OpaqueMaxOctets + 16);

            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:propertyConfiguration\":{\"pump:blob\":\"" + padding + "\"}", Strict());

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                    d.Message.Contains("octets", StringComparison.Ordinal)),
                Is.True,
                Describe(result));
        }

        [Test]
        public void OpaqueContentsAreNeverInterpreted()
        {
            // A vendor key that spells a term of this Binding is still the
            // vendor's own: the bounds apply to the shape, never to what the
            // object says.
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:metadata\":{\"pump:inner\":{\"uav:futureTerm\":1}}", Strict());

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnknownVocabularyTerm),
                Is.False,
                Describe(result));
        }

        [Test]
        public void ValidMinimumSecurityProducesNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"," +
                "\"uav:securityPolicy\":\"Basic256Sha256\"}}}," +
                "\"security\":\"opcua_auto_sc\"",
                Strict());

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.False,
                Describe(result));
        }

        [Test]
        public void MinimumSecurityOnANonAutoSchemeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"securityDefinitions\":{\"opcua_channel_sc\":{\"scheme\":\"uav:channelsec\"," +
                "\"uav:securityMode\":\"Sign\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.True,
                Describe(result));
        }

        [Test]
        public void MinimumSecurityOutsideASecuritySchemeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.True,
                Describe(result));
        }

        [Test]
        public void MinimumSecurityWithAnUnknownPolicyIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityPolicy\":\"Basic999\"}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.True,
                Describe(result));
        }

        [Test]
        public void MinimumSecurityCarryingAnotherMemberIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{\"uav:securityMode\":\"Sign\"," +
                "\"uav:trustList\":\"required\"}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.True,
                Describe(result));
        }

        [Test]
        public void EmptyMinimumSecurityIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"securityDefinitions\":{\"opcua_auto_sc\":{\"scheme\":\"auto\"," +
                "\"uav:minimumSecurity\":{}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidSecurityFloor),
                Is.True,
                Describe(result));
        }

        [Test]
        public void SecurityFloorOrdersModesAndPoliciesAsTheSpecificationStates()
        {
            var floor = new WotSecurityFloor("Sign", "Basic256Sha256");

            Assert.Multiple(() =>
            {
                Assert.That(floor.Permits("SignAndEncrypt", "Aes256_Sha256_RsaPss"), Is.True);
                Assert.That(floor.Permits("Sign", "Basic256Sha256"), Is.True);
                Assert.That(floor.Permits("None", "Aes256_Sha256_RsaPss"), Is.False);
                Assert.That(floor.Permits("SignAndEncrypt", "Basic256"), Is.False,
                    "A floor of Basic256Sha256 excludes the deprecated policies without naming " +
                    "them.");
                Assert.That(floor.Permits("SignAndEncrypt", "Vendor_Policy"), Is.False,
                    "A policy this Binding does not name ranks below every policy it names.");
            });
        }

        [Test]
        public void ClaimExpansionFollowsTheProfileNesting()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotBindingConformance.ClaimsSatisfy(
                        s_archivalClaim, "WoT-NativeMapping"),
                    Is.True);
                Assert.That(
                    WotBindingConformance.ClaimsSatisfy(
                        s_readerClaim, "WoT-JsonResidue"),
                    Is.False);
                Assert.That(
                    WotBindingConformance.Expand("WoT-Reader").ToList(),
                    Does.Contain("WoT-ProtocolBinding"));
            });
        }

        private static readonly string[] s_mandatoryBaseEventTypeFields =
        [
            "EventId", "EventType", "SourceNode", "SourceName",
            "Time", "ReceiveTime", "Message", "Severity"
        ];

        private static readonly string[] s_archivalClaim = ["WoT-ArchivalConverter"];

        private static readonly string[] s_readerClaim = ["WoT-Reader"];

        private static readonly string[] s_eventMappingRequirement = ["WoT-EventMapping"];

        private static WotNodeSetConverterOptions Strict()
        {
            return new WotNodeSetConverterOptions
            {
                ConformanceMode = WotConformanceMode.Strict
            };
        }

        /// <summary>
        /// Strict conformance plus the authoring rule of WoT Binding
        /// Section 4.1, which is what a tool that writes a document against a
        /// published revision holds itself to.
        /// </summary>
        private static WotNodeSetConverterOptions Authoring()
        {
            return new WotNodeSetConverterOptions
            {
                ConformanceMode = WotConformanceMode.Strict,
                AuthoringValidation = true
            };
        }

        private static string EventWithClauses(string clauses)
        {
            return "\"events\":{\"overTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:browseName\":\"pump:OverTemperatureEventType\"," +
                "\"uav:eventSelectClauses\":[" + clauses + "]}}";
        }

        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.ToString()));
        }

        private static void AssertSelectClauseError(
            WotConversionResult<UANodeSet> result, string fragment)
        {
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.EventSelectClauseInvalid &&
                    d.Message.Contains(fragment, StringComparison.Ordinal)),
                Is.True,
                Describe(result));
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            return Convert(members, new WotNodeSetConverterOptions());
        }

        /// <summary>
        /// Converts a document whose event affordance states a selection,
        /// resolving that selection against the sibling EventType definitions
        /// the fixtures name (WoT Binding Sections 5.1.5 and 6.1).
        /// </summary>
        private static async Task<UANodeSet> ConvertResolvedAsync(WotDocument document)
        {
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, WotTestData.EventTypeDocuments())
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                Describe(result));
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!;
        }

        private static WotConversionResult<UANodeSet> Convert(
            string members, WotNodeSetConverterOptions options)
        {
            using WotDocument document = ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document, options);
        }

        private static WotDocument ParseThingModel(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members +
                "}");
            return WotDocument.Parse(json);
        }

        private static WotConversionResult<UANodeSet> ConvertWithLegacyVocabularyIri(
            string members)
        {
            return ConvertWithLegacyVocabularyIri(members, new WotNodeSetConverterOptions());
        }

        private static WotConversionResult<UANodeSet> ConvertWithLegacyVocabularyIri(
            string members, WotNodeSetConverterOptions options)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT/v1#\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members +
                "}");
            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document, options);
        }

        /// <summary>
        /// Both published revisions of the WoT Binding, in the order the
        /// specification lists them.
        /// </summary>
        private static readonly string[] s_publishedBindingRevisions = ["1.0", "1.1"];
    }
}
