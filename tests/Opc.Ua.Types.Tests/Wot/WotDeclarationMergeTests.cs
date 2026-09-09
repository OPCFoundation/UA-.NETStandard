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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// WoT Binding Section 5.2.1: a member of a document bound to an existing
    /// type whose exact BrowseName is one the type already declares populates
    /// that declaration rather than adding a second Node beside it. Section 6.8
    /// then decides whether a member the type does not declare is admitted at
    /// all.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDeclarationMergeTests
    {
        private const string PumpNamespace = "urn:test:pump";
        private const string TankTypeId = "nsu=urn:test:pump;i=1042";
        private const string BaseTypeId = "nsu=urn:test:pump;i=1000";

        /// <summary>
        /// The declaration reaches the Variable through <c>HasProperty</c> and
        /// types it as a <c>PropertyType</c> holding a <c>Double</c>. The
        /// member says only that it is a number, so it becomes that declared
        /// Node rather than a generic component beside it.
        /// </summary>
        [Test]
        public async Task AMemberNamingAMandatoryDeclarationPopulatesItAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Speed\":{\"type\":\"number\"}",
                MandatorySpeedModel()).ConfigureAwait(false);

            UAVariable speed = VariableNamed(result.Value!, "1:Speed");
            Assert.Multiple(() =>
            {
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(
                    TypeDefinitionOf(speed), Is.EqualTo(WotVocabulary.PropertyType));
                Assert.That(
                    OwnershipOf(speed),
                    Is.EqualTo("HasProperty"),
                    "The declaration is reached by HasProperty, so the populated Node is.");
                Assert.That(
                    RootReferenceTypeTo(result.Value!, speed.NodeId!),
                    Is.EqualTo("HasProperty"));
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
            });
        }

        /// <summary>
        /// One member is one Node. A populated declaration must not also leave
        /// the generic sibling the unbound projection would have produced.
        /// </summary>
        [Test]
        public async Task APopulatedDeclarationLeavesNoDuplicateSiblingAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Speed\":{\"type\":\"number\"}",
                MandatorySpeedModel()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Value!.Items!.OfType<UAVariable>()
                        .Count(v => string.Equals(v.BrowseName, "1:Speed", StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    RootReferencesTo(result.Value!, "1:Speed"),
                    Is.EqualTo(1),
                    "One member is reached once, by the reference the declaration states.");
            });
        }

        /// <summary>
        /// An action populates the Method the type declares, and the projected
        /// Method points at that declaration - which is what tells a Client the
        /// two are the same Method rather than two Methods of one name.
        /// </summary>
        [Test]
        public async Task AnActionNamingADeclaredMethodPopulatesItAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                properties: null,
                MandatoryResetModel(),
                actions: "\"Reset\":{}").ConfigureAwait(false);

            UAMethod reset = result.Value!.Items!.OfType<UAMethod>()
                .Single(m => string.Equals(m.BrowseName, "1:Reset", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(
                    reset.MethodDeclarationId,
                    Is.EqualTo("ns=1;s=/nsu=urn%3Atest%3Apump;TankType/nsu=urn%3Atest%3Apump;Reset"),
                    "The instance points at the Method declaration of its type.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                            d.Message.Contains("Method", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// An event affordance populates the EventType the bound type declares
        /// it raises.
        /// </summary>
        [Test]
        public async Task AnEventNamingADeclaredEventTypePopulatesItAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                properties: null,
                MandatoryEventModel(),
                actions: null,
                events: "\"Overheated\":{\"data\":{\"type\":\"object\"," +
                    "\"properties\":{\"EventId\":{\"type\":\"string\"," +
                    "\"contentEncoding\":\"base64\"}}}}").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                        d.Message.Contains("Event", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// An inherited declaration is a declaration of the bound type, so a
        /// member naming one populates it exactly as it would one the type
        /// states itself.
        /// </summary>
        [Test]
        public async Task AMemberNamingAnInheritedDeclarationPopulatesItAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Serial\":{\"type\":\"string\"}",
                InheritingModels()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    OwnershipOf(VariableNamed(result.Value!, "1:Serial")),
                    Is.EqualTo("HasProperty"));
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                            d.Message.Contains("inherited", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// <c>uav:includeInherited: false</c> narrows the question to the
        /// declarations the bound type states itself, so an inherited one is
        /// not a declaration this document is populating.
        /// </summary>
        [Test]
        public async Task IncludeInheritedFalseSeesOnlyDirectDeclarationsAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Serial\":{\"type\":\"string\"}",
                InheritingModels(),
                actions: null,
                events: null,
                extraRootTerms: "\"uav:includeInherited\":false,").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.False);
                Assert.That(
                    OwnershipOf(VariableNamed(result.Value!, "1:Serial")),
                    Is.EqualTo("HasComponent"),
                    "Nothing was populated, so the generic projection stands.");
            });
        }

        /// <summary>
        /// <c>uav:includeInherited: true</c> asks for the effective closure,
        /// which is also what a document that says nothing gets.
        /// </summary>
        [Test]
        public async Task IncludeInheritedTrueSeesTheEffectiveClosureAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Serial\":{\"type\":\"string\"}",
                InheritingModels(),
                actions: null,
                events: null,
                extraRootTerms: "\"uav:includeInherited\":true,").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                Is.True);
        }

        /// <summary>
        /// A member projected as a Variable cannot populate a Method
        /// declaration: the two are different NodeClasses, so emitting it
        /// anyway would put a second Node under a name the type has spoken for.
        /// </summary>
        [Test]
        public async Task AMemberOfTheWrongNodeClassIsReportedAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Reset\":{\"type\":\"string\"}",
                MandatoryResetModel()).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DeclarationMismatch),
                Is.True);
        }

        /// <summary>
        /// A member that states a DataType the declaration contradicts is not
        /// describing the declared Node at all.
        /// </summary>
        [Test]
        public async Task AMemberContradictingTheDeclaredDataTypeIsReportedAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=12\"}",
                MandatorySpeedModel()).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationMismatch &&
                        d.Message.Contains("DataType", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// A member that states a ValueRank the declaration contradicts is
        /// reported for the same reason.
        /// </summary>
        [Test]
        public async Task AMemberContradictingTheDeclaredValueRankIsReportedAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Speed\":{\"type\":\"number\",\"uav:valueRank\":1}",
                MandatorySpeedModel()).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationMismatch &&
                        d.Message.Contains("ValueRank", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// A type that declares a Variable and a Method of one name does not
        /// say which one a member of that name populates, so the document is
        /// reported rather than silently resolved.
        /// </summary>
        [Test]
        public async Task TwoDeclarationsOfOneNameAreAmbiguousAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Reset\":{\"type\":\"string\"}",
                AmbiguousModel()).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.DeclarationAmbiguous),
                Is.True);
        }

        /// <summary>
        /// A member the type does not declare is an ordinary extension where
        /// the document did not close its content.
        /// </summary>
        [Test]
        public async Task AMemberMatchingNoDeclarationIsAdmittedByDefaultAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Colour\":{\"type\":\"string\"}",
                MandatorySpeedModel()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.False);
            });
        }

        /// <summary>
        /// <c>uav:additionalProperties: true</c> is the same permission stated
        /// explicitly.
        /// </summary>
        [Test]
        public async Task AdditionalPropertiesTrueAdmitsAnUndeclaredMemberAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Colour\":{\"type\":\"string\"}",
                MandatorySpeedModel(),
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":true,").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UndeclaredMember),
                Is.False);
        }

        /// <summary>
        /// <c>uav:additionalProperties: false</c> closes the instance against
        /// members the resolved effective type does not declare.
        /// </summary>
        [Test]
        public async Task AdditionalPropertiesFalseRejectsAnUndeclaredMemberAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Colour\":{\"type\":\"string\"}",
                MandatorySpeedModel(),
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":false,").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.UndeclaredMember &&
                        d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
        }

        /// <summary>
        /// A closed document whose declared member <em>is</em> declared passes,
        /// so the rule rejects a member rather than the term.
        /// </summary>
        [Test]
        public async Task AdditionalPropertiesFalseAdmitsADeclaredMemberAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Speed\":{\"type\":\"number\"}",
                MandatorySpeedModel(),
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":false,").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False);
        }

        /// <summary>
        /// A closed document whose bound type's declarations cannot be read at
        /// all fails explicitly. Passing would let a member the type never
        /// declared through on the strength of nothing having been consulted.
        /// </summary>
        [Test]
        public async Task AdditionalPropertiesFalseWithoutTheCapabilityFailsAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Colour\":{\"type\":\"string\"}",
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":false,",
                new DeclarationlessResolver()).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationsUnavailable &&
                        d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
        }

        /// <summary>
        /// A document that binds to no type has no declared set to close
        /// against, so the term describes the projected type and rejects
        /// nothing.
        /// </summary>
        [Test]
        public void AdditionalPropertiesFalseOnAnUnboundDocumentRejectsNothing()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                "\"uav:id\":\"" + TankTypeId + "\"," +
                "\"uav:additionalProperties\":false," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Colour\":{\"type\":\"string\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False);
        }

        /// <summary>
        /// The synchronous conversion resolves nothing, so a document whose
        /// binding it cannot resolve is reported as an unresolved dependency.
        /// The point is that it never blocks on the asynchronous resolution
        /// instead.
        /// </summary>
        [Test]
        public void TheSynchronousConversionReportsTheUnresolvedDependency()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"uav:additionalProperties\":false," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Speed\":{\"type\":\"number\"}}," +
                "\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"" +
                TankTypeId + "\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding &&
                        d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
        }

        /// <summary>
        /// The composite uses the first source that holds the type, which is
        /// the same precedence node resolution follows: a loaded AddressSpace
        /// must not contribute declarations to a type a sibling document
        /// already defines.
        /// </summary>
        [Test]
        public async Task TheCompositeUsesTheFirstSourceThatHoldsTheTypeAsync()
        {
            using WotDocument sibling = WotDocument.Parse(MandatorySpeedModel()[0]);
            var composite = new WotCompositeNodeResolver(
                new WotDocumentNodeResolver([sibling]),
                new FallbackDeclarationResolver());

            WotTypeDeclarationSet? set = await composite
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(
                    Names(set!.Declarations),
                    Is.EqualTo(s_speedOnly).AsCollection,
                    "The sibling answered, so the fallback never contributed.");
                Assert.That(composite.OffersDeclarations(), Is.True);
            });
        }

        /// <summary>
        /// A composite of parts that offer nothing says so, which is what lets
        /// a closed document fail explicitly rather than pass unevaluated.
        /// </summary>
        [Test]
        public void ACompositeWithoutTheCapabilityReportsThat()
        {
            var composite = new WotCompositeNodeResolver(new DeclarationlessResolver());

            Assert.That(composite.OffersDeclarations(), Is.False);
        }

        /// <summary>
        /// A composite whose parts all offer the capability but none of which
        /// holds the type answers <c>null</c>, which is different from offering
        /// nothing.
        /// </summary>
        [Test]
        public async Task ACompositeThatHoldsNoSuchTypeAnswersNothingAsync()
        {
            var composite = new WotCompositeNodeResolver(
                new WotDocumentNodeResolver([]), new FallbackDeclarationResolver());

            WotTypeDeclarationSet? set = await composite
                .ResolveDeclarationsAsync("nsu=urn:test:pump;i=1", WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.That(set, Is.Null);
        }

        /// <summary>
        /// Two Thing Models that extend each other are a cycle, not a
        /// hierarchy. The walk stops and says the closure is incomplete rather
        /// than running forever or reporting a partial answer as whole.
        /// </summary>
        [Test]
        public async Task ACyclicSupertypeChainReportsAnIncompleteClosureAsync()
        {
            using WotDocument first = WotDocument.Parse(TypeModel(
                TankTypeId, "TankType", "\"Speed\":{\"type\":\"number\"}", BaseTypeId));
            using WotDocument second = WotDocument.Parse(TypeModel(
                BaseTypeId, "BaseType", "\"Serial\":{\"type\":\"string\"}", TankTypeId));
            var resolver = new WotDocumentNodeResolver([first, second]);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set!.IsComplete, Is.False);
                Assert.That(set.Detail, Does.Contain("cycle"));
                Assert.That(
                    Names(set.Declarations),
                    Is.EqualTo(s_serialAndSpeed).AsCollection);
            });
        }

        /// <summary>
        /// A supertype the local context does not hold leaves the closure
        /// incomplete, so a member matching nothing here cannot be concluded
        /// undeclared.
        /// </summary>
        [Test]
        public async Task AnUnheldSupertypeReportsAnIncompleteClosureAsync()
        {
            using WotDocument only = WotDocument.Parse(TypeModel(
                TankTypeId, "TankType", "\"Speed\":{\"type\":\"number\"}", BaseTypeId));
            var resolver = new WotDocumentNodeResolver([only]);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.That(set!.IsComplete, Is.False);
            Assert.That(set.Detail, Does.Contain("not held"));
        }

        /// <summary>
        /// An incomplete closure makes the closed-content rule unevaluable
        /// rather than making an undeclared member pass.
        /// </summary>
        [Test]
        public async Task AdditionalPropertiesFalseWithAnIncompleteClosureFailsAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertInstanceAsync(
                "\"Colour\":{\"type\":\"string\"}",
                [TypeModel(TankTypeId, "TankType", "\"Speed\":{\"type\":\"number\"}", BaseTypeId)],
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":false,").ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationsUnavailable),
                Is.True);
        }

        /// <summary>
        /// A declaration that was read is a fact about the bound type whether
        /// or not the rest of the closure answered. Skipping it because some
        /// other supertype could not be read is what produces the duplicate
        /// sibling Section 5.2.1 forbids - under a name the type has already
        /// spoken for, and reached by the wrong ReferenceType.
        /// </summary>
        [Test]
        public async Task APartialClosureStillPopulatesTheDeclarationsItKnowsAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Speed\":{\"type\":\"number\"}",
                actions: null,
                events: null,
                extraRootTerms: string.Empty,
                new PartialDeclarationResolver(inherited: false)).ConfigureAwait(false);

            UAVariable speed = VariableNamed(result.Value!, "1:Speed");
            Assert.Multiple(() =>
            {
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(OwnershipOf(speed), Is.EqualTo("HasProperty"));
                Assert.That(
                    RootReferencesTo(result.Value!, "1:Speed"),
                    Is.EqualTo(1),
                    "The member populates the declaration rather than sitting beside it.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True);
            });
        }

        /// <summary>
        /// A declaration inherited from a supertype that <em>was</em> read is
        /// applied too, even though the walk stopped short of the rest.
        /// </summary>
        [Test]
        public async Task APartialClosurePopulatesAKnownInheritedDeclarationAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Speed\":{\"type\":\"number\"}",
                actions: null,
                events: null,
                extraRootTerms: string.Empty,
                new PartialDeclarationResolver(inherited: true)).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.DeclarationPopulated &&
                        d.Message.Contains("inherited", StringComparison.Ordinal)),
                Is.True);
        }

        /// <summary>
        /// An open document states no closed-content rule, so the incompleteness
        /// does not refuse the projection - but it is never silent either,
        /// because silence reads exactly like a type that declares nothing.
        /// </summary>
        [Test]
        public async Task AnOpenDocumentWithAPartialClosureIsWarnedAboutAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Speed\":{\"type\":\"number\"}",
                actions: null,
                events: null,
                extraRootTerms: string.Empty,
                new PartialDeclarationResolver(inherited: false)).ConfigureAwait(false);

            WotDiagnostic? unavailable = result.Diagnostics.FirstOrDefault(
                d => d.Code == WotDiagnosticCode.DeclarationsUnavailable);
            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Not.Null);
                Assert.That(unavailable, Is.Not.Null);
                Assert.That(
                    unavailable!.Severity,
                    Is.EqualTo(WotDiagnosticSeverity.Warning));
                Assert.That(
                    unavailable.Message,
                    Does.Contain("could not be browsed"),
                    "The reason the closure is partial is carried through to the author.");
            });
        }

        /// <summary>
        /// An incomplete closure that names no reason is still reported: the
        /// gap is what matters, and a local context that offers no explanation
        /// must not therefore be silent.
        /// </summary>
        [Test]
        public async Task APartialClosureWithNoStatedReasonIsStillReportedAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Speed\":{\"type\":\"number\"}",
                actions: null,
                events: null,
                extraRootTerms: string.Empty,
                new PartialDeclarationResolver(inherited: false, detail: null))
                .ConfigureAwait(false);

            WotDiagnostic? unavailable = result.Diagnostics.FirstOrDefault(
                d => d.Code == WotDiagnosticCode.DeclarationsUnavailable);
            Assert.Multiple(() =>
            {
                Assert.That(unavailable, Is.Not.Null);
                Assert.That(unavailable!.Severity, Is.EqualTo(WotDiagnosticSeverity.Warning));
                Assert.That(unavailable.Message, Does.Contain("are incomplete"));
            });
        }

        /// <summary>
        /// A conversion given no local context at all offers no declarations,
        /// which is a different answer from one that offers them and holds
        /// none.
        /// </summary>
        [Test]
        public async Task AConversionWithNoLocalContextOffersNoDeclarationsAsync()
        {
            using WotDocument document = WotDocument.Parse(
                InstanceJson("\"Speed\":{\"type\":\"number\"}", null, null, string.Empty));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Not.Null);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationsUnavailable),
                    Is.False,
                    "The document binds to no type, so there is no rule to evaluate.");
            });
        }
        [Test]
        public async Task APartialClosureDoesNotCallAMemberUndeclaredAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertWithResolverAsync(
                "\"Speed\":{\"type\":\"number\"},\"Colour\":{\"type\":\"string\"}",
                actions: null,
                events: null,
                extraRootTerms: "\"uav:additionalProperties\":false,",
                new PartialDeclarationResolver(inherited: false)).ConfigureAwait(false);

            WotDiagnostic? unavailable = result.Diagnostics.FirstOrDefault(
                d => d.Code == WotDiagnosticCode.DeclarationsUnavailable);
            Assert.Multiple(() =>
            {
                Assert.That(unavailable, Is.Not.Null);
                Assert.That(unavailable!.Severity, Is.EqualTo(WotDiagnosticSeverity.Error));
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.UndeclaredMember),
                    Is.False,
                    "Whether the type declares 'Colour' was never established.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.DeclarationPopulated),
                    Is.True,
                    "The declaration that was read is still applied.");
            });
        }

        /// <summary>
        /// The direct scope is the type's own declarations, without the ones it
        /// inherits, and it is complete because it never walked anything.
        /// </summary>
        [Test]
        public async Task TheDirectScopeIsTheTypesOwnDeclarationsAsync()
        {
            using WotDocument derived = WotDocument.Parse(InheritingModels()[0]);
            using WotDocument baseModel = WotDocument.Parse(InheritingModels()[1]);
            var resolver = new WotDocumentNodeResolver([derived, baseModel]);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Names(set!.Declarations),
                    Is.EqualTo(s_speedOnly).AsCollection);
                Assert.That(set.IsComplete, Is.True);
                Assert.That(set.Supertypes.Count, Is.Zero);
            });
        }

        /// <summary>
        /// A type document that has already listed what it inherits is not
        /// asked for it a second time, which is what
        /// <c>uav:includeInherited</c> means on the authoring side.
        /// </summary>
        [Test]
        public async Task ATypeThatAlreadyListsInheritedMembersIsNotWalkedAsync()
        {
            using WotDocument derived = WotDocument.Parse(TypeModel(
                TankTypeId,
                "TankType",
                "\"Speed\":{\"type\":\"number\"}",
                BaseTypeId,
                extraRootTerms: "\"uav:includeInherited\":true,"));
            var resolver = new WotDocumentNodeResolver([derived]);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    set!.IsComplete,
                    Is.True,
                    "Nothing was walked, so nothing was missing.");
                Assert.That(set.Supertypes.Count, Is.Zero);
            });
        }

        /// <summary>
        /// The declaration view reports every piece of metadata a caller needs
        /// to populate a member without browsing the type again.
        /// </summary>
        [Test]
        public async Task ADeclarationCarriesItsFullMetadataAsync()
        {
            using WotDocument model = WotDocument.Parse(MandatorySpeedModel()[0]);
            var resolver = new WotDocumentNodeResolver([model]);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TankTypeId, WotDeclarationScope.Direct)
                .ConfigureAwait(false);

            WotTypeDeclaration speed = Only(set!.Declarations);
            Assert.Multiple(() =>
            {
                Assert.That(speed.NamespaceUri, Is.EqualTo(PumpNamespace));
                Assert.That(speed.BrowseName, Is.EqualTo("Speed"));
                Assert.That(speed.Kind, Is.EqualTo(WotDeclarationKind.Variable));
                Assert.That(speed.ReferenceTypeName, Is.EqualTo("HasProperty"));
                Assert.That(
                    speed.TypeDefinitionNodeId, Is.EqualTo(WotVocabulary.PropertyType));
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(speed.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(speed.ArrayDimensions.Count, Is.Zero);
                Assert.That(speed.ModellingRule, Is.EqualTo(WotModellingRule.Mandatory));
                Assert.That(speed.IsMandatory, Is.True);
                Assert.That(speed.IsInherited, Is.False);
                Assert.That(speed.DeclaringTypeNodeId, Is.EqualTo(TankTypeId));
                Assert.That(speed.NodeId, Is.EqualTo("nsu=urn:test:pump;s=/nsu=urn%3Atest%3Apump;TankType/nsu=urn%3Atest%3Apump;Speed"));
                Assert.That(speed.MethodDeclarationNodeId, Is.Empty);
            });
        }

        /// <summary>
        /// A placeholder declaration is mandatory in the sense that matters
        /// here: an instance has to carry at least one member of the pattern.
        /// </summary>
        [Test]
        public void MandatoryPlaceholderCountsAsMandatory()
        {
            var declaration = new WotTypeDeclaration
            {
                NamespaceUri = PumpNamespace,
                BrowseName = "Any",
                Kind = WotDeclarationKind.Variable,
                DeclaringTypeNodeId = TankTypeId,
                ModellingRule = WotModellingRule.MandatoryPlaceholder
            };

            Assert.That(declaration.IsMandatory, Is.True);
        }

        [Test]
        public void ModellingRuleNamesAndIdentifiersMapOntoOneEnumeration()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("Mandatory"),
                    Is.EqualTo(WotModellingRule.Mandatory));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("Optional"),
                    Is.EqualTo(WotModellingRule.Optional));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("MandatoryPlaceholder"),
                    Is.EqualTo(WotModellingRule.MandatoryPlaceholder));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("OptionalPlaceholder"),
                    Is.EqualTo(WotModellingRule.OptionalPlaceholder));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("ExposesItsArray"),
                    Is.EqualTo(WotModellingRule.ExposesItsArray));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule("Nonsense"),
                    Is.EqualTo(WotModellingRule.None));
                Assert.That(
                    WotTypeDeclarations.ToModellingRule(null),
                    Is.EqualTo(WotModellingRule.None));
                Assert.That(
                    WotTypeDeclarations.FromModellingRuleId(
                        WotVocabulary.ModellingRuleMandatory),
                    Is.EqualTo(WotModellingRule.Mandatory));
                Assert.That(
                    WotTypeDeclarations.FromModellingRuleId(
                        WotVocabulary.ModellingRuleOptional),
                    Is.EqualTo(WotModellingRule.Optional));
                Assert.That(
                    WotTypeDeclarations.FromModellingRuleId(
                        WotVocabulary.ModellingRuleMandatoryPlaceholder),
                    Is.EqualTo(WotModellingRule.MandatoryPlaceholder));
                Assert.That(
                    WotTypeDeclarations.FromModellingRuleId(
                        WotVocabulary.ModellingRuleOptionalPlaceholder),
                    Is.EqualTo(WotModellingRule.OptionalPlaceholder));
                Assert.That(
                    WotTypeDeclarations.FromModellingRuleId("i=1"),
                    Is.EqualTo(WotModellingRule.None));
            });
        }

        [Test]
        public void ComparingDeclarationsOrdersByNamespaceThenNameThenKind()
        {
            WotTypeDeclaration variable = Declaration(PumpNamespace, "A", WotDeclarationKind.Variable);
            WotTypeDeclaration method = Declaration(PumpNamespace, "A", WotDeclarationKind.Method);
            WotTypeDeclaration later = Declaration(PumpNamespace, "B", WotDeclarationKind.Variable);
            WotTypeDeclaration other = Declaration("urn:zzz", "A", WotDeclarationKind.Variable);

            Assert.Multiple(() =>
            {
                Assert.That(WotTypeDeclarations.Compare(variable, later), Is.LessThan(0));
                Assert.That(WotTypeDeclarations.Compare(variable, other), Is.LessThan(0));
                Assert.That(WotTypeDeclarations.Compare(variable, method), Is.LessThan(0));
                Assert.That(WotTypeDeclarations.Compare(variable, variable), Is.Zero);
                Assert.That(
                    () => WotTypeDeclarations.Compare(null!, variable),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => WotTypeDeclarations.Compare(variable, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void IndexingRejectsANullDocument()
        {
            var index = new WotDocumentDeclarationIndex();

            Assert.That(() => index.Add(null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// A Thing Description declares nothing - it populates declarations
        /// rather than making them - so indexing one contributes no type.
        /// </summary>
        [Test]
        public void AThingDescriptionContributesNoDeclarations()
        {
            var index = new WotDocumentDeclarationIndex();
            using WotDocument instance = WotDocument.Parse(
                InstanceJson("\"Speed\":{\"type\":\"number\"}", null, null, string.Empty));

            index.Add(instance);

            Assert.Multiple(() =>
            {
                Assert.That(
                    index.Resolve("nsu=urn:test:pump;i=5001", WotDeclarationScope.Direct),
                    Is.Null);
                Assert.That(index.Resolve(string.Empty, WotDeclarationScope.Direct), Is.Null);
            });
        }

        private static WotTypeDeclaration Declaration(
            string namespaceUri, string browseName, WotDeclarationKind kind)
        {
            return new WotTypeDeclaration
            {
                NamespaceUri = namespaceUri,
                BrowseName = browseName,
                Kind = kind,
                DeclaringTypeNodeId = TankTypeId
            };
        }

        private static UAVariable VariableNamed(UANodeSet nodeSet, string browseName)
        {
            return nodeSet.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, browseName, StringComparison.Ordinal));
        }

        private static string TypeDefinitionOf(UANode node)
        {
            return node.References!.First(r =>
                r.IsForward &&
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                .Value!;
        }

        private static string OwnershipOf(UANode node)
        {
            return node.References!.First(r => !r.IsForward).ReferenceType!;
        }

        private static string RootReferenceTypeTo(UANodeSet nodeSet, string nodeId)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObject);
            return root.References!.First(r =>
                r.IsForward && string.Equals(r.Value, nodeId, StringComparison.Ordinal))
                .ReferenceType!;
        }

        private static int RootReferencesTo(UANodeSet nodeSet, string browseName)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObject);
            HashSet<string> targets = [.. nodeSet.Items!
                .Where(i => string.Equals(i.BrowseName, browseName, StringComparison.Ordinal))
                .Select(i => i.NodeId!)];
            return root.References!.Count(r => r.IsForward && targets.Contains(r.Value!));
        }

        private static byte[][] MandatorySpeedModel()
        {
            return
            [
                TypeModel(
                    TankTypeId,
                    "TankType",
                    "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                    "\"uav:modellingRule\":\"Mandatory\"," +
                    "\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"i=68\"}]}")
            ];
        }

        private static byte[][] MandatoryResetModel()
        {
            return
            [
                TypeModel(
                    TankTypeId,
                    "TankType",
                    properties: null,
                    supertype: null,
                    extraRootTerms: string.Empty,
                    actions: "\"Reset\":{\"uav:modellingRule\":\"Mandatory\"}")
            ];
        }

        private static byte[][] MandatoryEventModel()
        {
            return
            [
                TypeModel(
                    TankTypeId,
                    "TankType",
                    properties: null,
                    supertype: null,
                    extraRootTerms: string.Empty,
                    actions: null,
                    events: "\"Overheated\":{\"uav:modellingRule\":\"Mandatory\"," +
                        "\"data\":{\"type\":\"object\",\"properties\":{}}}")
            ];
        }

        private static byte[][] AmbiguousModel()
        {
            return
            [
                TypeModel(
                    TankTypeId,
                    "TankType",
                    "\"Reset\":{\"type\":\"string\"}",
                    supertype: null,
                    extraRootTerms: string.Empty,
                    actions: "\"Reset\":{}")
            ];
        }

        private static byte[][] InheritingModels()
        {
            return
            [
                TypeModel(
                    TankTypeId, "TankType", "\"Speed\":{\"type\":\"number\"}", BaseTypeId),
                TypeModel(
                    BaseTypeId,
                    "BaseType",
                    "\"Serial\":{\"type\":\"string\"," +
                    "\"uav:modellingRule\":\"Mandatory\"," +
                    "\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"i=68\"}]}")
            ];
        }

        private static byte[] TypeModel(
            string nodeId,
            string browseName,
            string? properties,
            string? supertype = null,
            string extraRootTerms = "",
            string? actions = null,
            string? events = null)
        {
            string links = supertype is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"tm:extends\",\"href\":\"" + supertype + "\"}]";
            return WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"" + browseName + "\"," +
                "\"uav:browseName\":\"pump:" + browseName + "\"," +
                "\"uav:id\":\"" + nodeId + "\"," +
                extraRootTerms +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                (properties is null ? string.Empty : ",\"properties\":{" + properties + "}") +
                (actions is null ? string.Empty : ",\"actions\":{" + actions + "}") +
                (events is null ? string.Empty : ",\"events\":{" + events + "}") +
                links + "}");
        }

        private static byte[] InstanceJson(
            string? properties, string? actions, string? events, string extraRootTerms)
        {
            return WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\",\"pump:TankType\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                extraRootTerms +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                (properties is null ? string.Empty : ",\"properties\":{" + properties + "}") +
                (actions is null ? string.Empty : ",\"actions\":{" + actions + "}") +
                (events is null ? string.Empty : ",\"events\":{" + events + "}") +
                "}");
        }

        private static Task<WotConversionResult<UANodeSet>> ConvertInstanceAsync(
            string? properties,
            byte[][] models,
            string? actions = null,
            string? events = null,
            string extraRootTerms = "")
        {
            return ConvertWithResolverAsync(
                properties, actions, events, extraRootTerms, BuildResolver(models));
        }

        /// <summary>
        /// Builds the sibling half of the local context. The resolver indexes
        /// eagerly and keeps no reference into the parsed documents, so they
        /// are released as soon as it exists.
        /// </summary>
        private static WotDocumentNodeResolver BuildResolver(byte[][] models)
        {
            var documents = new List<WotDocument>();
            try
            {
                foreach (byte[] model in models)
                {
                    documents.Add(WotDocument.Parse(model));
                }
                return new WotDocumentNodeResolver(documents);
            }
            finally
            {
                foreach (WotDocument document in documents)
                {
                    document.Dispose();
                }
            }
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

        private static WotTypeDeclaration Only(ArrayOf<WotTypeDeclaration> declarations)
        {
            Assert.That(declarations.Count, Is.EqualTo(1));
            foreach (WotTypeDeclaration declaration in declarations)
            {
                return declaration;
            }
            throw new InvalidOperationException("unreachable");
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertWithResolverAsync(
            string? properties,
            string? actions,
            string? events,
            string extraRootTerms,
            IWotNodeResolver resolver)
        {
            using WotDocument document = WotDocument.Parse(
                InstanceJson(properties, actions, events, extraRootTerms));
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);
        }

        /// <summary>
        /// A local context that holds the bound type and answers with part of
        /// its closure: one declaration it did read, and the reason the rest is
        /// missing. It is what an AddressSpace looks like when a node manager
        /// refuses a browse.
        /// </summary>
        private sealed class PartialDeclarationResolver
            : IWotNodeResolver, IWotTypeDeclarationResolver
        {
            public PartialDeclarationResolver(
                bool inherited,
                string? detail = "The supertype 'nsu=urn:test:pump;i=1000' could not be browsed.")
            {
                m_inherited = inherited;
                m_detail = detail;
            }

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, PumpNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    string.Equals(browseName, "TankType", StringComparison.Ordinal)
                        ? new ArrayOf<WotResolvedNode>(
                            [new WotResolvedNode(TankTypeId, WotExpectedNodeClass.ObjectType)])
                        : ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    string.Equals(expandedNodeId, TankTypeId, StringComparison.Ordinal)
                        ? new WotResolvedNode(TankTypeId, WotExpectedNodeClass.ObjectType)
                        : null);
            }

            public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
                string typeNodeId,
                WotDeclarationScope scope,
                CancellationToken cancellationToken = default)
            {
                if (!string.Equals(typeNodeId, TankTypeId, StringComparison.Ordinal))
                {
                    return new ValueTask<WotTypeDeclarationSet?>((WotTypeDeclarationSet?)null);
                }
                return new ValueTask<WotTypeDeclarationSet?>(new WotTypeDeclarationSet
                {
                    TypeNodeId = TankTypeId,
                    Declarations = new[]
                    {
                        new WotTypeDeclaration
                        {
                            NamespaceUri = PumpNamespace,
                            BrowseName = "Speed",
                            Kind = WotDeclarationKind.Variable,
                            DeclaringTypeNodeId = m_inherited ? BaseTypeId : TankTypeId,
                            NodeId = "nsu=urn:test:pump;s=/nsu=urn%3Atest%3Apump;TankType/nsu=urn%3Atest%3Apump;Speed",
                            ReferenceTypeName = "HasProperty",
                            TypeDefinitionNodeId = WotVocabulary.PropertyType,
                            DataType = "i=11",
                            ValueRank = ValueRanks.Scalar,
                            ModellingRule = WotModellingRule.Mandatory,
                            IsInherited = m_inherited
                        }
                    }.ToArrayOf(),
                    Supertypes = m_inherited
                        ? new[] { BaseTypeId }.ToArrayOf()
                        : ArrayOf<string>.Empty,
                    IsComplete = false,
                    Detail = m_detail
                });
            }

            private readonly bool m_inherited;
            private readonly string? m_detail;
        }

        /// <summary>
        /// A local context that resolves names but reports no declarations at
        /// all, which is what an implementation written before the capability
        /// existed looks like.
        /// </summary>
        private sealed class DeclarationlessResolver : IWotNodeResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, PumpNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    string.Equals(browseName, "TankType", StringComparison.Ordinal)
                        ? new ArrayOf<WotResolvedNode>(
                            [new WotResolvedNode(TankTypeId, WotExpectedNodeClass.ObjectType)])
                        : ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
            }
        }

        /// <summary>
        /// A second source that would answer for the same type, used to prove
        /// the composite never consults it once an earlier source has.
        /// </summary>
        private sealed class FallbackDeclarationResolver
            : IWotNodeResolver, IWotTypeDeclarationResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(true);
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
            }

            public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
                string typeNodeId,
                WotDeclarationScope scope,
                CancellationToken cancellationToken = default)
            {
                if (!string.Equals(typeNodeId, TankTypeId, StringComparison.Ordinal))
                {
                    return new ValueTask<WotTypeDeclarationSet?>((WotTypeDeclarationSet?)null);
                }
                return new ValueTask<WotTypeDeclarationSet?>(new WotTypeDeclarationSet
                {
                    TypeNodeId = typeNodeId,
                    Declarations = new ArrayOf<WotTypeDeclaration>(
                    [
                        new WotTypeDeclaration
                        {
                            NamespaceUri = PumpNamespace,
                            BrowseName = "FromFallback",
                            Kind = WotDeclarationKind.Variable,
                            DeclaringTypeNodeId = typeNodeId
                        }
                    ])
                });
            }
        }

        /// <summary>
        /// The one declaration the TankType document itself states.
        /// </summary>
        private static readonly string[] s_speedOnly = ["Speed"];

        /// <summary>
        /// The declarations of the whole cycle, base first, which is the order
        /// the walk merges them in.
        /// </summary>
        private static readonly string[] s_serialAndSpeed = ["Serial", "Speed"];
    }
}
