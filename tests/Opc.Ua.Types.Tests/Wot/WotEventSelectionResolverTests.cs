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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The linked-EventType event selection of WoT Binding Section 6.1: the
    /// fast path that derives one select clause per leaf of the definition an
    /// affordance links to with <c>tm:ref</c>, the explicit overlay that
    /// refines it, and the errors a consumer reports rather than deriving a
    /// partial selection.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventSelectionResolverTests
    {
        [Test]
        public async Task AFastPathDerivesOneClausePerLeafInTheStatedOrderAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\""),
                ("./types.tm.jsonld", AlarmTypeDocument())).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(new[]
                    {
                        string.Empty,
                        "EventId",
                        "EnabledState",
                        "EnabledState/Id",
                        "pump:Temperature"
                    }),
                    "The leaves are walked in the order uav:fieldOrder states, the ConditionId " +
                    "member yields the empty path, a state Variable's Name is dropped and a " +
                    "member's uav:browseName supplies the exact QualifiedName.");
                Assert.That(
                    clauses.ToList().All(c => c.TypeDefinitionId == "nsu=urn:test:pump;i=6001"),
                    Is.True,
                    "Every derived clause carries the linked definition's uav:id.");
                Assert.That(
                    clauses.ToList().All(c =>
                        c.Source == WotEventSelectClauseSource.LinkedEventType),
                    Is.True);
            });
        }

        [Test]
        public async Task ANestedLeafMaterializesTheNestedMemberAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\""),
                ("./types.tm.jsonld", AlarmTypeDocument())).ConfigureAwait(false);

            ArrayOf<ArrayOf<string>> members =
                WotEventSelectClauses.GetMaterializedMemberPaths(clauses);

            Assert.That(
                members.ToList().Select(WotEventSelectClauses.FormatMemberPath),
                Is.EqualTo(s_nestedAlarmMemberPaths));
        }

        [Test]
        public async Task AThingModelRootIsAnEventTypeDefinitionAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance("\"tm:ref\":\"./base-event.tm.jsonld\""),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(s_baseEventBrowsePaths));
                Assert.That(clauses[0].TypeDefinitionId, Is.EqualTo("i=2041"));
            });
        }

        [Test]
        public void AnAffordanceThatStatesNoSelectionTakesTheImplicitDefault()
        {
            using WotDocument document = Parse(Affordance("\"title\":\"Alarm\""));

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotEventSelectionResolver.StatesSelection(document.Events["alarm"]),
                    Is.False,
                    "An affordance that names no EventType and writes no clause states " +
                    "nothing that has to be resolved.");
                Assert.That(WotEventSelectClauses.Default.Count, Is.EqualTo(8));
                Assert.That(
                    WotEventSelectClauses.Default.ToList().All(c =>
                        c.Source == WotEventSelectClauseSource.ImplicitDefault &&
                        c.TypeDefinitionId == WotEventSelectClauses.BaseEventTypeId),
                    Is.True);
            });
        }

        [Test]
        public async Task AnExplicitClauseReplacesTheBaselineEntryItNamesAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                    "\"uav:browsePath\":\"EventId\"}]"),
                ("./types.tm.jsonld", AlarmTypeDocument()),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Is.EqualTo(new[]
                    {
                        string.Empty,
                        "EnabledState",
                        "EnabledState/Id",
                        "pump:Temperature",
                        "EventId"
                    }),
                    "The clause replaces the baseline entry that fills the same member and " +
                    "is appended in the order it is written.");
                Assert.That(
                    clauses[clauses.Count - 1].TypeDefinitionId,
                    Is.EqualTo("i=2041"),
                    "The appended clause carries the identity of the definition it names, " +
                    "not the identity of the affordance's own EventType.");
                Assert.That(
                    clauses[clauses.Count - 1].Source,
                    Is.EqualTo(WotEventSelectClauseSource.Explicit));
            });
        }

        [Test]
        public async Task AnExplicitClauseAddsAFieldTheBaselineDoesNotCarryAsync()
        {
            ArrayOf<WotResolvedEventSelectClause> clauses = await ResolveAsync(
                Affordance(
                    "\"tm:ref\":\"./base-event.tm.jsonld\"," +
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"," +
                    "\"uav:browsePath\":\"pump:Temperature\"}]," +
                    "\"data\":{\"type\":\"object\"," +
                    "\"uav:fieldOrder\":[\"EventId\",\"Message\",\"Temperature\"]," +
                    "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                    "\"Message\":{\"type\":\"string\"}," +
                    "\"Temperature\":{\"type\":\"number\"}}}"),
                ("./types.tm.jsonld", AlarmTypeDocument()),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            Assert.That(
                clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_addedTemperatureBrowsePaths),
                "A field the linked definition does not declare is added by a clause that " +
                "names the EventType which does declare it, and the affordance's own data " +
                "is the effective schema the result is held to.");
        }

        [Test]
        public async Task ClausesThatMaterializeOneMemberAreRejectedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                    "\"uav:browsePath\":\"Message\"}," +
                    "{\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"," +
                    "\"uav:browsePath\":\"Message\"}]"),
                ("./types.tm.jsonld", AlarmTypeDocument()),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            AssertReports(diagnostics, "materialized member path");
        }

        [TestCase(
            "\"tm:ref\":\"./missing.tm.jsonld\"",
            "does not resolve in the local document set",
            TestName = "AnUnresolvableDocumentIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/absent\"",
            "does not resolve to a definition",
            TestName = "AnUnresolvablePointerIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/properties/speed\"",
            "does not carry uav:eventType",
            TestName = "ATargetOfTheWrongKindIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/noIdentity\"",
            "carries no uav:id",
            TestName = "AMissingIdentityIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/noData\"",
            "not an object DataSchema",
            TestName = "AMissingDataSchemaIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/scalarData\"",
            "not an object DataSchema",
            TestName = "ANonObjectDataSchemaIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/selectsItself\"",
            "does not select them",
            TestName = "ADefinitionThatSelectsIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/noOrder\"",
            "uav:fieldOrder",
            TestName = "AMissingFieldOrderIsReported")]
        [TestCase(
            "\"tm:ref\":\"./types.tm.jsonld#/events/badMemberName\"",
            "not a legal unqualified BrowseName",
            TestName = "AnUnqualifiableMemberNameIsReported")]
        [TestCase(
            "\"tm:ref\":\"./cycle-a.tm.jsonld\"",
            "acyclic",
            TestName = "AReferenceCycleIsReported")]
        public async Task AnInvalidFastPathIsReportedRatherThanPartiallyDerivedAsync(
            string reference, string fragment)
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(reference),
                ("./types.tm.jsonld", AlarmTypeDocument()),
                ("./cycle-a.tm.jsonld", CycleDocument("./cycle-b.tm.jsonld")),
                ("./cycle-b.tm.jsonld", CycleDocument("./cycle-a.tm.jsonld")))
                .ConfigureAwait(false);

            AssertReports(diagnostics, fragment);
        }

        [Test]
        public async Task AClauseNamingAFieldItsEventTypeDoesNotDeclareIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"uav:eventSelectClauses\":[" +
                    "{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                    "\"uav:browsePath\":\"pump:Temperature\"}]"),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            AssertReports(diagnostics, "does not declare");
        }

        [Test]
        public async Task AResolvedSelectionAgreesWithTheAffordanceEffectiveDataAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./base-event.tm.jsonld\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}}}",
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            AssertReports(diagnostics, "declares no such member");
        }

        [Test]
        public async Task ABoundedResolverStopsAtTheConfiguredDepthAsync()
        {
            var options = new WotNodeSetConverterOptions();
            options.MaxResolverDepth = 2;

            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"./chain-0.tm.jsonld\""),
                options,
                ("./chain-0.tm.jsonld", ChainDocument("./chain-1.tm.jsonld")),
                ("./chain-1.tm.jsonld", ChainDocument("./chain-2.tm.jsonld")),
                ("./chain-2.tm.jsonld", ChainDocument("./chain-3.tm.jsonld")),
                ("./chain-3.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            AssertReports(diagnostics, "bounded");
        }

        /// <summary>
        /// The NodeId clause form was removed rather than kept as a second
        /// spelling, so nothing reads it and a document that writes it is told
        /// that a clause carries exactly a reference and a path.
        /// </summary>
        [Test]
        public async Task ANodeIdClauseIsNoLongerAcceptedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance(
                    "\"uav:eventSelectClauses\":[" +
                    "{\"uav:typeDefinitionId\":\"i=2041\"," +
                    "\"uav:browsePath\":\"EventId\"}]"),
                ("./base-event.tm.jsonld", BaseEventDocument())).ConfigureAwait(false);

            AssertReports(diagnostics, "uav:typeDefinitionId");
        }

        /// <summary>
        /// One instance resolves document after document: everything a call
        /// accumulates — the documents it opened above all — belongs to that
        /// call, so the second caller derives its own selection instead of
        /// reading a document the first call closed.
        /// </summary>
        [Test]
        public async Task OneResolverResolvesTwoDocumentsThatShareADefinitionAsync()
        {
            var resolver = new WotEventSelectionResolver(
                new StubResolver(("./types.tm.jsonld", AlarmTypeDocument())));
            string documentJson = Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"");

            ArrayOf<WotResolvedEventSelectClause> first =
                await ResolveOnceAsync(resolver, documentJson).ConfigureAwait(false);
            ArrayOf<WotResolvedEventSelectClause> second =
                await ResolveOnceAsync(resolver, documentJson).ConfigureAwait(false);

            Assert.That(
                second.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(first.ToList().Select(c => c.BrowsePath)).AsCollection);
        }

        /// <summary>
        /// The same instance resolving two documents at once: each call gets
        /// its own resolution context, so nothing one call opened, cached or
        /// reported reaches the other.
        /// </summary>
        [Test]
        public async Task OneResolverResolvesTwoDocumentsConcurrentlyAsync()
        {
            var resolver = new WotEventSelectionResolver(
                new StubResolver(("./types.tm.jsonld", AlarmTypeDocument())));
            string documentJson = Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"");

            ArrayOf<WotResolvedEventSelectClause>[] resolved = await Task.WhenAll(
                ResolveOnceAsync(resolver, documentJson),
                ResolveOnceAsync(resolver, documentJson)).ConfigureAwait(false);

            Assert.That(
                resolved[1].ToList().Select(c => c.BrowsePath),
                Is.EqualTo(resolved[0].ToList().Select(c => c.BrowsePath)).AsCollection);
        }

        /// <summary>
        /// Within one call a document two affordances reference is read once,
        /// which is what the cache is for; across calls it is read again,
        /// because the first call closed what it read.
        /// </summary>
        [Test]
        public async Task ADocumentIsReadOncePerCallAsync()
        {
            var stub = new StubResolver(("./types.tm.jsonld", AlarmTypeDocument()));
            var resolver = new WotEventSelectionResolver(stub);
            string documentJson =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"}," +
                "\"second\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./types.tm.jsonld#/events/alarm\"}}}";

            using (WotDocument document = Parse(documentJson))
            {
                WotConversionResult<WotEventSelectionCatalog> result = await resolver
                    .ResolveAsync(document)
                    .ConfigureAwait(false);
                Assert.That(result.Value, Is.Not.Null, Describe(result.Diagnostics));
                Assert.That(result.Value!.AffordanceNames.Count, Is.EqualTo(2));
            }
            Assert.That(stub.Requests, Is.EqualTo(1), "One call reads one document once.");

            using (WotDocument document = Parse(documentJson))
            {
                WotConversionResult<WotEventSelectionCatalog> result = await resolver
                    .ResolveAsync(document)
                    .ConfigureAwait(false);
                Assert.That(result.Value, Is.Not.Null, Describe(result.Diagnostics));
            }
            Assert.That(
                stub.Requests,
                Is.EqualTo(2),
                "The next call reads it again rather than reusing a closed document.");
        }

        /// <summary>
        /// A reference that does not resolve is reported once per call, however
        /// many affordances name it, and is reported again by the next call
        /// rather than being silently remembered as unresolvable.
        /// </summary>
        [Test]
        public async Task AnUnresolvableReferenceIsReportedOncePerCallAsync()
        {
            var resolver = new WotEventSelectionResolver(new StubResolver());
            string documentJson =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./missing.tm.jsonld\"}," +
                "\"second\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"./missing.tm.jsonld\"}}}";

            for (int call = 0; call < 2; call++)
            {
                using WotDocument document = Parse(documentJson);
                WotConversionResult<WotEventSelectionCatalog> result = await resolver
                    .ResolveAsync(document)
                    .ConfigureAwait(false);

                Assert.That(result.Value, Is.Null);
                Assert.That(
                    result.Diagnostics.Count(d =>
                        d.Message.Contains("does not resolve", StringComparison.Ordinal)),
                    Is.EqualTo(1),
                    Describe(result.Diagnostics));
            }
        }

        /// <summary>
        /// A resolution context is shared with every other phase of a
        /// conversion, so what those phases reported is theirs: it neither
        /// fails an otherwise clean selection nor is emitted a second time
        /// here, and the caller's own diagnostics are left as they were.
        /// </summary>
        [Test]
        public async Task AnUnrelatedContextErrorNeitherFailsNorRepeatsAsync()
        {
            var context = new WotResolutionContext(
                new WotNodeSetConverterOptions().ToResolverOptions());
            Assert.That(
                context.TryAddBytes("urn:unrelated", int.MaxValue, out _),
                Is.False,
                "The seeded error stands in for one an earlier phase reported.");

            using WotDocument document = Parse(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\""));
            var resolver = new WotEventSelectionResolver(
                new StubResolver(("./types.tm.jsonld", AlarmTypeDocument())));

            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document, context)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Not.Null, Describe(result.Diagnostics));
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(
                    context.Diagnostics,
                    Has.Count.EqualTo(1),
                    "The caller's diagnostics are neither cleared nor added to.");
            });
        }

        /// <summary>
        /// A local error still fails the call, and is reported exactly once
        /// even though the shared context already carries one of its own.
        /// </summary>
        [Test]
        public async Task ALocalErrorStillFailsTheCallExactlyOnceAsync()
        {
            var context = new WotResolutionContext(
                new WotNodeSetConverterOptions().ToResolverOptions());
            Assert.That(context.TryAddBytes("urn:unrelated", int.MaxValue, out _), Is.False);

            using WotDocument document = Parse(
                Affordance("\"tm:ref\":\"./missing.tm.jsonld\""));
            var resolver = new WotEventSelectionResolver(new StubResolver());

            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document, context)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
                Assert.That(
                    result.Diagnostics[0].Message,
                    Does.Contain("does not resolve"));
            });
        }

        /// <summary>
        /// A bound the context blocks on is recorded where it happens and is
        /// not restated by copying the context out afterwards.
        /// </summary>
        [Test]
        public async Task ABlockedLimitIsReportedOnceAsync()
        {
            var options = new WotNodeSetConverterOptions();
            options.MaxResolverDocumentBytes = 64;

            IReadOnlyList<WotDiagnostic> diagnostics = await ResolveWithErrorsAsync(
                Affordance("\"tm:ref\":\"./types.tm.jsonld#/events/alarm\""),
                options,
                ("./types.tm.jsonld", AlarmTypeDocument())).ConfigureAwait(false);

            Assert.That(
                diagnostics.Count(d => d.Code == WotDiagnosticCode.ResolverLimitExceeded),
                Is.EqualTo(1),
                Describe(diagnostics));
        }

        private static string Describe(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            return string.Join("; ", diagnostics.Select(d => d.Message));
        }

        private static void AssertReports(
            IReadOnlyList<WotDiagnostic> diagnostics, string fragment)
        {
            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains(fragment, StringComparison.Ordinal)),
                Is.True,
                string.Join("; ", diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// Resolves one document with a caller-supplied resolver instance, so a
        /// test can hand the same instance more than one document.
        /// </summary>
        private static async Task<ArrayOf<WotResolvedEventSelectClause>> ResolveOnceAsync(
            WotEventSelectionResolver resolver,
            string documentJson)
        {
            using WotDocument document = Parse(documentJson);
            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null, Describe(result.Diagnostics));
            Assert.That(
                result.Value!.TryGetSelection(
                    "alarm", out ArrayOf<WotResolvedEventSelectClause> clauses),
                Is.True);
            Assert.That(
                result.Value.TryGetLinkedData("alarm", out ReadOnlyMemory<byte> linked),
                Is.True,
                "The linked definition's schema is carried out of the document it came from, " +
                "which is closed when the call ends.");
            Assert.That(linked.Length, Is.GreaterThan(0));
            return clauses;
        }

        private static async Task<ArrayOf<WotResolvedEventSelectClause>> ResolveAsync(
            string documentJson,
            params (string Href, string Json)[] siblings)
        {
            using WotDocument document = Parse(documentJson);
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

        private static Task<IReadOnlyList<WotDiagnostic>> ResolveWithErrorsAsync(
            string documentJson,
            params (string Href, string Json)[] siblings)
        {
            return ResolveWithErrorsAsync(documentJson, new WotNodeSetConverterOptions(), siblings);
        }

        private static async Task<IReadOnlyList<WotDiagnostic>> ResolveWithErrorsAsync(
            string documentJson,
            WotNodeSetConverterOptions options,
            params (string Href, string Json)[] siblings)
        {
            using WotDocument document = Parse(documentJson);
            var resolver = new WotEventSelectionResolver(new StubResolver(siblings), options);
            WotConversionResult<WotEventSelectionCatalog> result = await resolver
                .ResolveAsync(document)
                .ConfigureAwait(false);

            Assert.That(
                result.Value,
                Is.Null,
                "A selection a consumer cannot derive is reported rather than returned.");
            return result.Diagnostics;
        }

        private static WotDocument Parse(string json)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(json));
        }

        private static string Affordance(string members)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":\"tm:ThingModel\",\"title\":\"Pump\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," + members + "}}}";
        }

        private static string BaseEventDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"BaseEventType\",\"uav:id\":\"i=2041\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Message\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}";
        }

        private static string ChainDocument(string next)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"Chain\",\"tm:ref\":\"" + next + "\"}";
        }

        private static string CycleDocument(string next)
        {
            return ChainDocument(next);
        }

        private static string AlarmTypeDocument()
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"PumpAlarms\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\"}}," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=6001\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"ConditionId\",\"EventId\",\"EnabledState\"," +
                "\"Temperature\"]," +
                "\"properties\":{" +
                "\"ConditionId\":{\"type\":\"string\"}," +
                "\"EventId\":{\"type\":\"string\"}," +
                "\"EnabledState\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"Name\",\"Id\"]," +
                "\"properties\":{\"Name\":{\"type\":\"string\"}," +
                "\"Id\":{\"type\":\"boolean\"}}}," +
                "\"Temperature\":{\"uav:browseName\":\"pump:Temperature\"," +
                "\"type\":\"number\"}}}}," +
                "\"noIdentity\":{\"@type\":\"uav:eventType\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}," +
                "\"noData\":{\"@type\":\"uav:eventType\",\"uav:id\":\"i=3001\"}," +
                "\"scalarData\":{\"@type\":\"uav:eventType\",\"uav:id\":\"i=3002\"," +
                "\"data\":{\"type\":\"string\"}}," +
                "\"selectsItself\":{\"@type\":\"uav:eventType\",\"uav:id\":\"i=3003\"," +
                "\"uav:eventSelectClauses\":[{\"tm:ref\":\"./base-event.tm.jsonld\"," +
                "\"uav:browsePath\":\"EventId\"}]," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}}}}," +
                "\"noOrder\":{\"@type\":\"uav:eventType\",\"uav:id\":\"i=3004\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Message\":{\"type\":\"string\"}}}}," +
                "\"badMemberName\":{\"@type\":\"uav:eventType\",\"uav:id\":\"i=3005\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"properties\":{\"pump:Temperature\":{\"type\":\"number\"}}}}" +
                "}}";
        }

        /// <summary>
        /// Serves the sibling documents a reference names, and nothing else: a
        /// reference resolves through the local document context of
        /// WoT Binding Section 5.1.5 and is never dereferenced over the network.
        /// </summary>
        private sealed class StubResolver : IWotThingResolver
        {
            public StubResolver(params (string Href, string Json)[] documents)
            {
                foreach ((string href, string json) in documents)
                {
                    m_documents[href] = json;
                }
            }

            /// <summary>
            /// Gets how many times a document was asked for, which is what
            /// tells a per-call cache from a per-instance one.
            /// </summary>
            public int Requests => Volatile.Read(ref m_requests);

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_requests);
                return new ValueTask<WotResolverResult>(
                    m_documents.TryGetValue(reference, out string json)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                        : WotResolverResult.NotFound);
            }

            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);
            private int m_requests;
        }

        /// <summary>
        /// The member paths a nested alarm definition materializes, parent
        /// before child, which is the order the derivation walks them in.
        /// </summary>
        private static readonly string[] s_nestedAlarmMemberPaths =
        [
            "ConditionId",
            "EventId",
            "EnabledState.Name",
            "EnabledState.Id",
            "Temperature"
        ];

        /// <summary>
        /// The two fields the sibling BaseEventType Thing Model declares.
        /// </summary>
        private static readonly string[] s_baseEventBrowsePaths = ["EventId", "Message"];

        /// <summary>
        /// The baseline plus the field an explicit clause adds, which is
        /// appended after the fields the linked definition declares.
        /// </summary>
        private static readonly string[] s_addedTemperatureBrowsePaths =
            ["EventId", "Message", "pump:Temperature"];
    }
}
