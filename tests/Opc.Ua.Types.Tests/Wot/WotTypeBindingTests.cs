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

using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// WoT Binding Section 5.2.1 lets a document bind the node it projects to
    /// a type that already exists, so a converter reuses that type instead of
    /// defining a second one of the same shape.
    /// </summary>
    [TestFixture]
    public sealed class WotTypeBindingTests
    {
        /// <summary>
        /// The definitive form still resolves through the Section 5.1.5 local
        /// context. A caller that supplies none cannot resolve it, so Section
        /// 5.2.1 fails the projection rather than emitting an unverified
        /// HasTypeDefinition - the dangling reference would be exactly the
        /// silently mistyped node the clause exists to prevent. The bound case
        /// is covered by <c>WotTypeBindingResolutionTests</c>, which supplies a
        /// local context.
        /// </summary>
        [Test]
        public void ADefinitiveLinkWithoutALocalContextIsReportedUnresolved()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[{\"rel\":\"ua:HasTypeDefinition\"," +
                "\"href\":\"nsu=urn:test:pump;i=1042\"}]");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.UnresolvedTypeBinding &&
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True,
                "An identifier that cannot be resolved must be reported, not trusted.");
        }

        /// <summary>
        /// Without a binding the projected node keeps the BaseObjectType
        /// default, so the new path cannot change untyped documents.
        /// </summary>
        [Test]
        public void ADocumentWithNoTypeBindingKeepsTheBaseObjectTypeDefault()
        {
            WotConversionResult<UANodeSet> result = Convert(string.Empty);

            Assert.That(TypeDefinitionOf(result.Value).Value,
                Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        /// <summary>
        /// A link that is not a type binding must be left alone.
        /// </summary>
        [Test]
        public void AnUnrelatedLinkDoesNotBindAType()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[{\"rel\":\"ua:Organizes\"," +
                "\"href\":\"nsu=urn:test:pump;i=1042\"}]");

            Assert.That(TypeDefinitionOf(result.Value).Value,
                Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        /// <summary>
        /// A Node has exactly one HasTypeDefinition, so two binding links are
        /// a defect in the document rather than a choice for the converter.
        /// </summary>
        [Test]
        public void TwoHasTypeDefinitionLinksAreReportedAsAmbiguous()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[" +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"nsu=urn:test:pump;i=1042\"}," +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"nsu=urn:test:pump;i=1043\"}]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True,
                "Two type bindings must be reported, not silently resolved.");
        }

        /// <summary>
        /// When the document is ambiguous the converter must not pick one of
        /// the two candidates.
        /// </summary>
        [Test]
        public void AnAmbiguousBindingDoesNotSilentlyPickACandidate()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[" +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"nsu=urn:test:pump;i=1042\"}," +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"nsu=urn:test:pump;i=1043\"}]");

            Assert.That(TypeDefinitionOf(result.Value).Value,
                Is.EqualTo(WotVocabulary.BaseObjectType),
                "An ambiguous document must not be bound to either candidate.");
        }

        /// <summary>
        /// Ambiguity dominates. Where several binding links are declared the
        /// converter is not entitled to choose one, so it must not also judge
        /// one arbitrary candidate and report a second, misleading defect.
        /// </summary>
        [Test]
        public void AnAmbiguousBindingIsNotAlsoReportedAsInvalid()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[" +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"   \"}," +
                "{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"nsu=urn:test:pump;i=1043\"}]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True,
                "Several binding links must be reported as ambiguous.");
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidTypeBinding),
                Is.False,
                "An ambiguous document must not also be judged on one candidate.");
        }

        /// <summary>
        /// A binding link with no usable identifier is a defect: the whole
        /// point of the definitive form is that it identifies exactly one Node.
        /// </summary>
        [TestCase("\"href\":\"\"")]
        [TestCase("\"href\":\"   \"")]
        public void ABindingLinkWithoutAnIdentifierIsReported(string href)
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"links\":[{\"rel\":\"ua:HasTypeDefinition\"," + href + "}]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidTypeBinding),
                Is.True);
        }

        private static Reference TypeDefinitionOf(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items.First(i => i is UAObject);
            return root.References.First(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", System.StringComparison.Ordinal));
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:Pump\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                (string.IsNullOrEmpty(members) ? string.Empty : "," + members) + "}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
