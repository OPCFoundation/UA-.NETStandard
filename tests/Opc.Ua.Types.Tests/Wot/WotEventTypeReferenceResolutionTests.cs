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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The EventType definition resolution of WoT Binding Section 6.1: the
    /// three shapes a <c>tm:ref</c> names a definition in, the ordered sources
    /// it is looked up in, and the ambiguity a consumer reports rather than
    /// settling by read order.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventTypeReferenceResolutionTests
    {
        [Test]
        public async Task ANestedDefinitionResolvesByItsLogicalIdentifierAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses =
                await ResolveAsync(SelfContainedDocument("evt:highTemperature"))
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(s_eventIdAndMessage));
                Assert.That(
                    clauses.ToList().All(c => c.TypeDefinitionId == "nsu=urn:test:pump;i=6001"),
                    Is.True);
            });
        }

        /// <summary>
        /// The identifier is a JSON-LD term, so it is expanded in the context
        /// of the node that wrote it. A reference that spells the IRI in full
        /// names the same definition as one that abbreviates it.
        /// </summary>
        [Test]
        public async Task AnExpandedIriNamesTheSameDefinitionAsItsCompactFormAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses =
                await ResolveAsync(SelfContainedDocument("urn:test:events:highTemperature"))
                    .ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_eventIdAndMessage));
        }

        /// <summary>
        /// A resolver that answers a logical identifier hands back a whole
        /// document, so the definition that comes back has to be the one that
        /// declares the identifier that was asked for rather than whatever the
        /// resolver returned.
        /// </summary>
        [Test]
        public async Task AResolverAnsweringAnIdentifierIsCheckedAgainstItAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"urn:test:events:highTemperature\""),
                ("urn:test:events:highTemperature", NestedDefinitionDocument()))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(s_eventIdAndMessage),
                    "The nested definition that declares the identifier is used, not the " +
                    "document root the resolver returned.");
                Assert.That(
                    clauses.ToList().All(c => c.TypeDefinitionId == "nsu=urn:test:pump;i=6001"),
                    Is.True);
            });
        }

        /// <summary>
        /// A document the resolver returns that declares the identifier
        /// nowhere is not the definition that was asked for.
        /// </summary>
        [Test]
        public async Task AResolverAnswerThatDeclaresAnotherIdentityIsRejectedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"urn:test:events:highTemperature\""),
                ("urn:test:events:highTemperature", UnidentifiedDefinitionDocument()))
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A prefix the referring document binds elsewhere expands elsewhere,
        /// so the same short form does not reach a definition it does not name.
        /// </summary>
        [Test]
        public async Task ADifferentlyBoundPrefixNamesADifferentDefinitionAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:other:events:\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"evt:highTemperature\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"," +
                "\"uav:browsePath\":\"EventId\"}]}}}",
                ("./types.tm.jsonld", NestedDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("does not resolve", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        [Test]
        public async Task ARootDefinitionResolvesByItsLogicalIdentifierAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"urn:test:root-event\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./root.tm.jsonld\",\"uav:browsePath\":\"Message\"}]"),
                ("./root.tm.jsonld", RootDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_eventIdAndMessage),
                "The location clause seeds the held set and the root identifier resolves " +
                "against it.");
        }

        /// <summary>
        /// <c>uav:id</c> identifies the Node an affordance projects, which every
        /// event affordance has. Treating it as a definition identifier would
        /// make every event in every document globally addressable.
        /// </summary>
        [Test]
        public async Task AUavIdAloneDoesNotMakeAnAffordanceADefinitionAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"tm:ref\":\"nsu=urn:test:pump;i=6001\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./unidentified.tm.jsonld#/events/highTemperature\"," +
                    "\"uav:browsePath\":\"EventId\"}]"),
                ("./unidentified.tm.jsonld", UnidentifiedDefinitionDocument()))
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("does not resolve", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// Two held documents declaring the same identifier is an authoring
        /// fault, and settling it by whichever was read first would make the
        /// resolved selection depend on document order.
        /// </summary>
        [Test]
        public async Task ADuplicateIdentifierAcrossDocumentsIsAmbiguousAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"tm:ref\":\"evt:highTemperature\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"," +
                    "\"uav:browsePath\":\"EventId\"}," +
                    "{\"tm:ref\":\"./other.tm.jsonld#/events/highTemperature\"," +
                    "\"uav:browsePath\":\"Message\"}]"),
                ("./types.tm.jsonld", NestedDefinitionDocument()),
                ("./other.tm.jsonld", NestedDefinitionDocument("nsu=urn:test:pump;i=6002")))
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("different definitions", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// Two definitions in one document carrying the same identifier is the
        /// same fault reported the same way.
        /// </summary>
        [Test]
        public async Task ADuplicateIdentifierWithinOneDocumentIsAmbiguousAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"tm:ref\":\"evt:highTemperature\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./twins.tm.jsonld#/events/first\"," +
                    "\"uav:browsePath\":\"EventId\"}]"),
                ("./twins.tm.jsonld", TwinDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("different definitions", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        [Test]
        public async Task TheWellKnownBaseEventTypeIsTheLastSourceConsultedAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses =
                await ResolveAsync(Affordance("\"tm:ref\":\"ua:BaseEventType\""))
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(s_wellKnownBaseEventTypeBrowsePaths),
                    "The built-in definition states what the type has, which includes the " +
                    "optional LocalTime field.");
                Assert.That(
                    clauses.ToList().All(c => c.TypeDefinitionId == "i=2041"), Is.True);
            });
        }

        [TestCase("i=2041")]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=2041")]
        [TestCase("http://opcfoundation.org/UA/BaseEventType")]
        public async Task TheWellKnownDefinitionAnswersToItsPinnedAliasesAsync(string alias)
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"" + alias + "\"")).ConfigureAwait(false);

            Assert.That(clauses.Count, Is.EqualTo(9));
        }

        /// <summary>
        /// The catalog is last, so a definition an author shipped under the
        /// same identifier is the one that resolves.
        /// </summary>
        [Test]
        public async Task AnAuthoredDefinitionShadowsTheWellKnownOneAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"ua:BaseEventType\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./base.tm.jsonld\",\"uav:browsePath\":\"EventId\"}]"),
                ("./base.tm.jsonld", ShadowingBaseEventDocument())).ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_messageThenEventId),
                "The author's own BaseEventType definition declares two fields, and the " +
                "explicit clause moves EventId to the end.");
        }

        /// <summary>
        /// The clauses an affordance writes without linking a definition are
        /// the whole selection. The implicit eight-field default is what an
        /// affordance that states nothing falls back to.
        /// </summary>
        [Test]
        public async Task ExplicitClausesWithoutALinkAreTheCompleteSelectionAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"ua:BaseEventType\",\"uav:browsePath\":\"Message\"}," +
                    "{\"tm:ref\":\"ua:BaseEventType\",\"uav:browsePath\":\"Severity\"}]"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(s_messageAndSeverity));
                Assert.That(
                    clauses.ToList().All(c =>
                        c.Source == WotEventSelectClauseSource.Explicit),
                    Is.True);
            });
        }

        /// <summary>
        /// With a link, the same clauses refine the derived baseline instead of
        /// replacing it: the named field moves to the end and the rest stays.
        /// </summary>
        [Test]
        public async Task ExplicitClausesWithALinkOverlayTheDerivedBaselineAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"evt:highTemperature\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"," +
                    "\"uav:browsePath\":\"EventId\"}]"),
                ("./types.tm.jsonld", NestedDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_messageThenEventId));
        }

        /// <summary>
        /// The implicit default stays the eight mandatory fields even though
        /// the built-in definition declares nine: an affordance that states no
        /// selection at all never reaches the resolver.
        /// </summary>
        [Test]
        public void TheImplicitDefaultRemainsTheEightMandatoryFields()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WotEventSelectClauses.Default.Count, Is.EqualTo(8));
                Assert.That(
                    WotEventSelectClauses.Default.ToList().Select(c => c.BrowsePath),
                    Has.None.EqualTo("LocalTime"));
            });
        }

        /// <summary>
        /// A root Thing Model states its identity as <c>id</c> where a Thing
        /// Description would; both name the same definition.
        /// </summary>
        [Test]
        public async Task ARootDefinitionResolvesByItsThingIdentifierAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"urn:test:root-event\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./root.tm.jsonld\",\"uav:browsePath\":\"Message\"}]"),
                ("./root.tm.jsonld", RootDefinitionDocument(idMember: "id")))
                .ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_eventIdAndMessage));
        }

        /// <summary>
        /// An EventType Thing Model that states no identifier at all is
        /// reachable by location and by nothing else.
        /// </summary>
        [Test]
        public async Task ARootWithoutAnIdentifierIsReachableOnlyByLocationAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"./root.tm.jsonld\""),
                ("./root.tm.jsonld", RootDefinitionDocument(idMember: null)))
                .ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_eventIdAndMessage));
        }

        /// <summary>
        /// A reference without a path separator but with a document suffix is
        /// a location, not an identifier, so its failure names the document.
        /// </summary>
        [Test]
        public async Task ASuffixedReferenceIsTreatedAsALocationAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"missing.jsonld\"")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains(
                        "does not resolve in the local document set",
                        StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A reference that has the shape of a location but is not a valid
        /// document URI with a pointer is reported as malformed rather than
        /// looked for.
        /// </summary>
        [Test]
        public async Task AMalformedLocationReferenceIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#events\"")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains(
                        "is not a document URI with an optional RFC 6901 JSON Pointer",
                        StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A document the resolver returns for an identifier can itself declare
        /// that identifier twice, and that is the same ambiguity as two
        /// documents declaring it once each.
        /// </summary>
        [Test]
        public async Task AResolverAnswerThatIsAmbiguousIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"urn:test:events:highTemperature\""),
                ("urn:test:events:highTemperature", TwinDefinitionDocument()))
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains("different definitions", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A sibling document that is not well-formed JSON is not a definition
        /// source; the reference that needed it reports the failure once.
        /// </summary>
        [Test]
        public async Task AMalformedSiblingDocumentIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"./broken.tm.jsonld\""),
                ("./broken.tm.jsonld", "{ not json")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A reference written twice names the document once: the pre-load pass
        /// reads it a single time whatever the affordance count.
        /// </summary>
        [Test]
        public async Task ARepeatedLocationIsLoadedOnceAsync()
        {
            var resolver = new CountingResolver(
                ("./types.tm.jsonld", NestedDefinitionDocument()));

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"}," +
                "\"second\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"}}}"));

            WotConversionResult<WotEventSelectionCatalog> result =
                await new WotEventSelectionResolver(resolver)
                    .ResolveAsync(document)
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Value,
                    Is.Not.Null,
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(resolver.Calls, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// A clause that is not an object, and a <c>tm:ref</c> that is not a
        /// string, name no location for the pre-load pass to read.
        /// </summary>
        [Test]
        public async Task ANonStringReferenceNamesNoLocationAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":42," +
                "\"uav:eventSelectClauses\":[\"not-an-object\"]}}}")
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A resolver bounded to fewer bytes than the sibling holds does not
        /// hand back a truncated definition; the limit is reported.
        /// </summary>
        [Test]
        public async Task ASiblingOverTheByteLimitIsReportedAsync()
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"")));

            var resolver = new WotEventSelectionResolver(
                new StubResolver([("./types.tm.jsonld", NestedDefinitionDocument())]));

            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(
                    document,
                    new WotResolutionContext(new WotResolverOptions
                    {
                        MaxTotalBytes = 8
                    }))
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// An <c>events</c> entry that is not an object declares no definition
        /// and names no location, so it is passed over rather than read.
        /// </summary>
        [Test]
        public async Task ANonObjectEventEntryIsPassedOverAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{" +
                "\"broken\":\"not an affordance\"," +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"tm:ref\":\"evt:highTemperature\"}," +
                "\"highTemperature\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}}}").ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_eventIdAndMessage));
        }

        /// <summary>
        /// <c>@id</c> is a logical identifier only where it is a non-empty
        /// string; anything else identifies nothing.
        /// </summary>
        [TestCase("42")]
        [TestCase("\"\"")]
        [TestCase("{\"@value\":\"x\"}")]
        [TestCase("\"highTemperature\"")]
        [TestCase("\"urn:example: spaced\"")]
        public async Task ANestedIdentifierThatNamesNothingIndexesNothingAsync(string id)
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"tm:ref\":\"evt:highTemperature\"}," +
                "\"highTemperature\":{\"@id\":" + id + "," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}}}")
                .ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("does not resolve", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// An empty reference names nothing at all: not a location, and no
        /// identifier any document declares.
        /// </summary>
        [Test]
        public async Task AnEmptyReferenceNamesNothingAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"\"")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A bare token that resolves nowhere is reported as an identifier that
        /// names no definition, not as a file that could not be opened.
        /// </summary>
        [Test]
        public async Task AnUnknownBareTokenIsReportedAsAnIdentifierAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"pump-overtemperature\"")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains(
                        "does not resolve to a definition. A logical identifier",
                        StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// The held document and a sibling declaring the same identifier is the
        /// ambiguity that names the held document by its pointer alone, since
        /// it has no location of its own.
        /// </summary>
        [Test]
        public async Task AnAmbiguityInvolvingTheHeldDocumentNamesItByPointerAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"tm:ref\":\"evt:highTemperature\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"," +
                "\"uav:browsePath\":\"EventId\"}]}," +
                "\"highTemperature\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6009\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}}}",
                ("./types.tm.jsonld", NestedDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains("different definitions", StringComparison.Ordinal) &&
                    d.Message.Contains("'#/events/highTemperature'", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A sibling that parses but is not a JSON object declares no
        /// definitions; it is passed over rather than read as one.
        /// </summary>
        [Test]
        public async Task ANonObjectSiblingDeclaresNoDefinitionsAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"./array.tm.jsonld#/events/highTemperature\""),
                ("./array.tm.jsonld", "[1,2,3]")).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("does not resolve", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        /// <summary>
        /// A held document whose root is itself an EventType definition names
        /// itself by the empty pointer, so an ambiguity involving it reads as
        /// the document rather than as a member of one.
        /// </summary>
        [Test]
        public async Task AnAmbiguityInvolvingTheHeldRootNamesTheDocumentItselfAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@id\":\"evt:highTemperature\"," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"],\"title\":\"AlarmType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=6100\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"tm:ref\":\"evt:highTemperature\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"./types.tm.jsonld#/events/highTemperature\"," +
                "\"uav:browsePath\":\"EventId\"}]}}}",
                ("./types.tm.jsonld", NestedDefinitionDocument())).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains("different definitions", StringComparison.Ordinal) &&
                    d.Message.Contains("'#'", StringComparison.Ordinal)),
                Is.True,
                Describe(diagnostics));
        }

        private static string Describe(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            return string.Join("; ", diagnostics.Select(d => d.Message));
        }

        private static async Task<ArrayOf<WotResolvedEventSelectClause>> ResolveAsync(
            string documentJson,
            params (string Href, string Json)[] siblings)
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(documentJson));
            var resolver = new WotEventSelectionResolver(new StubResolver(siblings));
            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document)
                .ConfigureAwait(false);

            Assert.That(
                result.Value,
                Is.Not.Null,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.That(
                result.Value!.TryGetSelection(
                    "alarm", out ArrayOf<WotResolvedEventSelectClause> clauses),
                Is.True);
            return clauses;
        }

        private static async Task<IReadOnlyList<WotDiagnostic>> ResolveWithErrorsAsync(
            string documentJson,
            params (string Href, string Json)[] siblings)
        {
            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(documentJson));
            var resolver = new WotEventSelectionResolver(new StubResolver(siblings));
            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document)
                .ConfigureAwait(false);
            return result.Diagnostics;
        }

        private static string Affordance(string members)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"evt\":\"urn:test:events:\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," + members + "}}}";
        }

        /// <summary>
        /// One document that both declares a definition and references it, so
        /// the identifier is resolved against the held document itself.
        /// </summary>
        private static string SelfContainedDocument(string reference)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"tm:ref\":\"" + reference + "\"}," +
                "\"highTemperature\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}}}";
        }

        private static string NestedDefinitionDocument(string id = "nsu=urn:test:pump;i=6001")
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{\"highTemperature\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"" + id + "\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}}}";
        }

        /// <summary>
        /// A definition that carries <c>uav:id</c> and no <c>@id</c>, so it is
        /// reachable by location and by nothing else.
        /// </summary>
        private static string UnidentifiedDefinitionDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{\"highTemperature\":{" +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}}}";
        }

        private static string TwinDefinitionDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"evt\":\"urn:test:events:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpType\"," +
                "\"events\":{" +
                "\"first\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}," +
                "\"second\":{\"@id\":\"evt:highTemperature\"," +
                "\"@type\":\"uav:eventType\",\"uav:id\":\"nsu=urn:test:pump;i=6002\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"Message\":{\"type\":\"string\"}}}}}}";
        }

        private static string RootDefinitionDocument(string? idMember = "@id")
        {
            string identity = idMember is null
                ? string.Empty
                : "\"" + idMember + "\":\"urn:test:root-event\",";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                identity +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"HighTemperatureType\",\"uav:id\":\"nsu=urn:test:pump;i=6003\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}";
        }

        private static string ShadowingBaseEventDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@id\":\"ua:BaseEventType\"," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"BaseEventType\",\"uav:id\":\"i=2041\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}";
        }

        /// <summary>
        /// Serves a fixed set of sibling documents by href, so a reference to a
        /// document the caller holds resolves without any I/O.
        /// </summary>
        private sealed class StubResolver : IWotThingResolver
        {
            public StubResolver((string Href, string Json)[] siblings)
            {
                foreach ((string href, string json) in siblings)
                {
                    m_siblings[href] = json;
                }
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    m_siblings.TryGetValue(reference, out string? json)
                        ? WotResolverResult.FromBytes(
                            Encoding.UTF8.GetBytes(json), "application/tm+json")
                        : WotResolverResult.NotFound);
            }

            private readonly Dictionary<string, string> m_siblings =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// The same stub, counting how often it was asked, so a test can prove
        /// a document reached by two references is read once.
        /// </summary>
        private sealed class CountingResolver : IWotThingResolver
        {
            public CountingResolver(params (string Href, string Json)[] siblings)
            {
                foreach ((string href, string json) in siblings)
                {
                    m_siblings[href] = json;
                }
            }

            public int Calls { get; private set; }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_siblings.TryGetValue(reference, out string? json))
                {
                    return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
                }
                Calls++;
                return new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(
                        Encoding.UTF8.GetBytes(json), "application/tm+json"));
            }

            private readonly Dictionary<string, string> m_siblings =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// The two fields every EventType definition in this fixture declares,
        /// in the order its <c>uav:fieldOrder</c> states them.
        /// </summary>
        private static readonly string[] s_eventIdAndMessage = ["EventId", "Message"];

        /// <summary>
        /// The same two fields after an explicit clause has moved EventId to
        /// the end of the selection.
        /// </summary>
        private static readonly string[] s_messageThenEventId = ["Message", "EventId"];

        /// <summary>
        /// The selection two explicit clauses write on their own, without a
        /// linked definition to refine.
        /// </summary>
        private static readonly string[] s_messageAndSeverity = ["Message", "Severity"];

        /// <summary>
        /// The nine fields the well-known BaseEventType definition declares,
        /// which is the eight mandatory ones plus the optional LocalTime.
        /// </summary>
        private static readonly string[] s_wellKnownBaseEventTypeBrowsePaths =
        [
            "EventId",
            "EventType",
            "SourceNode",
            "SourceName",
            "Time",
            "ReceiveTime",
            "LocalTime",
            "Message",
            "Severity"
        ];
    }
}
