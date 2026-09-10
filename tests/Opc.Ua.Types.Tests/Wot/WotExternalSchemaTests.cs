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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// <c>uav:externalSchema</c> names a second description of the same data.
    /// It is resolved only through providers a host configured, it never
    /// changes the DataType the Binding derives, and a description that
    /// disagrees with the canonical DataSchema is reported rather than applied.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotExternalSchemaTests
    {
        private const string Reference = "https://example.com/schemas/speed.json";

        private const string WideNumericPartialSchema =
            "{\"type\":\"number\",\"minimum\":18446744073709551615," +
            "\"multipleOf\":0.00000000000000000000000000000000000001}";

        private const string StringFacetPartialSchema =
                                 /*lang=json,strict*/
                                 """{"format":"custom","contentEncoding":"base64","pattern":"","uniqueItems":false}""";

        /// <summary>
        /// A converter given no provider fetches nothing at all, which is the
        /// only safe default for an arbitrary IRI in a document a consumer did
        /// not write.
        /// </summary>
        [Test]
        public async Task WithoutAProviderNothingIsFetchedAsync()
        {
            var probe = new CountingProvider("{}");
            var resolver = new WotExternalSchemaResolver();

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.NotEvaluated));
                Assert.That(result.ProviderIndex, Is.EqualTo(-1));
                Assert.That(probe.Calls, Is.Zero);
                Assert.That(resolver.ProviderCount, Is.Zero);
            });
        }

        [Test]
        public async Task AnAgreeingSchemaIsCompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(result.ProviderIndex, Is.Zero);
                Assert.That(result.Detail, Is.Null);
            });
        }

        [Test]
        public async Task ADisagreeingJsonTypeIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"string\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Detail, Does.Contain("type"));
            });
        }

        /// <summary>
        /// A definitive DataType in the external schema that names a different
        /// DataType than the affordance maps to is the sharpest disagreement
        /// there is, because it is the one statement that could otherwise read
        /// as a redefinition.
        /// </summary>
        [Test]
        public async Task ADisagreeingDataTypeIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"uav:mapToType\":\"i=12\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Detail, Does.Contain("uav:mapToType"));
            });
        }

        [Test]
        public async Task ADisagreeingDataTypeIdIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"uav:dataTypeId\":\"i=12\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Detail, Does.Contain("uav:dataTypeId"));
        }

        /// <summary>
        /// A member the canonical DataSchema declares and the external one does
        /// not is a disagreement about the shape of the data.
        /// </summary>
        [Test]
        public async Task AMissingMemberIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(
                                         /*lang=json,strict*/
                                         "{\"type\":\"object\",\"properties\":{\"Other\":{\"type\":\"string\"}}}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"Speed\":{\"type\":\"number\"}}}"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Detail, Does.Contain("Speed"));
            });
        }

        [Test]
        public async Task AMemberOfADifferentTypeIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(
                                         /*lang=json,strict*/
                                         "{\"type\":\"object\",\"properties\":{\"Speed\":{\"type\":\"string\"}}}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"Speed\":{\"type\":\"number\"}}}"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Detail, Does.Contain("Speed"));
        }

        [Test]
        public async Task MatchingMembersAreCompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(
                                         /*lang=json,strict*/
                                         "{\"type\":\"object\",\"properties\":{\"Speed\":{\"type\":\"number\"}}}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"Speed\":{\"type\":\"number\"}}}"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
        }

        [Test]
        public async Task ANestedFieldTypeDisagreementIsIncompatibleAsync()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(
                                     /*lang=json,strict*/
                                     """
                {
                  "type": "object",
                  "properties": {
                    "Nested": { "type": "object", "properties": { "Value": { "type": "string" } } }
                  }
                }
                """));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(
                                         /*lang=json,strict*/
                                         """
                    {
                      "type": "object",
                      "properties": {
                        "Nested": { "type": "object", "properties": { "Value": { "type": "boolean" } } }
                      }
                    }
                    """),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
            Assert.That(result.Detail, Does.Contain("Nested").And.Contain("Value"));
            Assert.That(result.ProviderIndex, Is.Zero);
        }

        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"array","items":{"type":"string"}}""",
                                 /*lang=json,strict*/
                                 """{"type":"array","items":{"type":"boolean"}}""",
            false, "/items/type")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"array","items":{"type":"object","properties":{"Value":{"type":"string"}}}}""",
                                 /*lang=json,strict*/
                                 """{"type":"array","items":{"type":"object","properties":{"Value":{"type":"boolean"}}}}""",
            false, "/items/properties/Value/type")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"number","minimum":2}""",
                                 /*lang=json,strict*/
                                 """{"type":"number","minimum":1}""",
            false, "/minimum")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"number","minimum":1.0,"enum":[2.0,1.0]}""",
                                 /*lang=json,strict*/
                                 """{"enum":[1,2],"minimum":1,"type":"number"}""",
            true, "")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"A":{},"B":{}},"required":["B","A"]}""",
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"B":{},"A":{}},"required":["A","B"]}""",
            true, "")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"A":{},"B":{}},"uav:fieldOrder":["B","A"]}""",
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"A":{},"B":{}},"uav:fieldOrder":["A","B"]}""",
            false, "/uav:fieldOrder")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"A":{}}}""",
                                 /*lang=json,strict*/
                                 """{"type":"object","properties":{"A":{},"B":{}}}""",
            false, "/properties/B")]
        [TestCase(
                                 /*lang=json,strict*/
                                 """{"type":"integer","enum":[18446744073709551615,0]}""",
                                 /*lang=json,strict*/
                                 """{"type":"integer","enum":[0,18446744073709551615]}""",
            true, "")]
        [TestCase(/*lang=json,strict*/ """{"type":"array","items":false}""", /*lang=json,strict*/ """{"type":"array","items":false}""", true, "")]
        public async Task NestedExternalContractsCompareCompleteSemanticShapesAsync(
            string external,
            string canonical,
            bool compatible,
            string difference)
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider("""{"type":"object","properties":{"Nested":""" + external + "}}"));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse("""{"type":"object","properties":{"Nested":""" + canonical + "}}"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(compatible
                ? WotExternalSchemaOutcome.Compatible
                : WotExternalSchemaOutcome.Incompatible), result.Detail);
            if (compatible)
            {
                Assert.That(result.Detail, Is.Null);
            }
            else
            {
                Assert.That(result.Detail, Does.Contain("/properties/Nested" + difference));
            }
        }

        [Test]
        public async Task ExternalDataTypeIdentityAliasesAreEquivalentAsync()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(
                                     /*lang=json,strict*/
                                     """{"type":"number","uav:dataTypeId":"nsu=http://opcfoundation.org/UA/;i=11"}"""));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Schema("number"), "i=11", new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible), result.Detail);
        }

        [TestCase(/*lang=json,strict*/ """{"type":7}""", "/type")]
        [TestCase(/*lang=json,strict*/ """{"type":"unknown"}""", "/type")]
        [TestCase(/*lang=json,strict*/ """{"properties":{"A":7}}""", "/properties/A")]
        [TestCase(/*lang=json,strict*/ """{"required":[1]}""", "/required")]
        [TestCase(/*lang=json,strict*/ """{"properties":{"A":{},"B":{}},"uav:fieldOrder":["A"]}""", "/uav:fieldOrder")]
        [TestCase(/*lang=json,strict*/ """{"properties":{"A":{}},"uav:fieldOrder":["A","A"]}""", "/uav:fieldOrder")]
        [TestCase(/*lang=json,strict*/ """{"items":7}""", "/items")]
        public async Task MalformedPartialExternalContractsCannotBeCertifiedAsync(string schema, string pointer)
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(schema));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Parse("{}"), string.Empty, new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
            Assert.That(result.Detail, Does.Contain(pointer));
        }

        [TestCase(/*lang=json,strict*/ """{"type":"array","items":{"$ref":"urn:missing"}}""",
                                 /*lang=json,strict*/
                                 """{"type":"array"}""", "/items/$ref", false)]
        [TestCase(/*lang=json,strict*/ """{"type":"array","items":{"$ref":"urn:missing"}}""",
                                 /*lang=json,strict*/
                                 """{"type":"array"}""", "/items/$ref", true)]
        [TestCase(/*lang=json,strict*/ """{"type":"number","minimum":"zero"}""",
                                 /*lang=json,strict*/
                                 """{"type":"number"}""", "/minimum", false)]
        [TestCase(/*lang=json,strict*/ """{"type":"number","minimum":"zero"}""",
                                 /*lang=json,strict*/
                                 """{"type":"number"}""", "/minimum", true)]
        [TestCase(/*lang=json,strict*/ """{"oneOf":[{"type":"number","minimum":false}]}""",
            "{}", "/oneOf/0/minimum", false)]
        [TestCase(/*lang=json,strict*/ """{"oneOf":[{"type":"number","minimum":false}]}""",
            "{}", "/oneOf/0/minimum", true)]
        [TestCase(/*lang=json,strict*/ """{"anyOf":[{"type":"array","items":{"$ref":"#"}}]}""",
            "{}", "/anyOf/0/items/$ref", false)]
        [TestCase(/*lang=json,strict*/ """{"allOf":[{"properties":{"Hidden":{"$ref":"urn:missing"}}}]}""",
            "{}", "/allOf/0/properties/Hidden/$ref", true)]
        [TestCase(/*lang=json,strict*/ """{"additionalProperties":{"$ref":"urn:missing"}}""",
            "{}", "/additionalProperties/$ref", false)]
        [TestCase(/*lang=json,strict*/ """{"additionalProperties":{"minimum":"zero"}}""",
            "{}", "/additionalProperties/minimum", true)]
        public async Task UnmatchedSchemaBranchesAreValidatedIndependentlyAsync(
            string invalidSchema,
            string partialSchema,
            string pointer,
            bool invalidCanonical)
        {
            var provider = new CountingProvider(invalidCanonical ? partialSchema : invalidSchema);
            var resolver = new WotExternalSchemaResolver(provider);
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Parse(invalidCanonical ? invalidSchema : partialSchema),
                string.Empty, new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible), result.Detail);
            Assert.That(result.Detail, Does.Contain(pointer));
            Assert.That(result.Detail, Does.Contain(invalidCanonical ? "canonical schema" : "external schema"));
            Assert.That(provider.Calls, Is.EqualTo(1));
        }

        [TestCase("maximum", "false")]
        [TestCase("exclusiveMinimum", "null")]
        [TestCase("exclusiveMaximum", "\"ten\"")]
        [TestCase("multipleOf", "true")]
        [TestCase("multipleOf", "0")]
        [TestCase("multipleOf", "-1")]
        [TestCase("minLength", "-1")]
        [TestCase("maxLength", "1.5")]
        [TestCase("minItems", "\"many\"")]
        [TestCase("maxItems", "null")]
        [TestCase("minProperties", "false")]
        [TestCase("maxProperties", "1.00000000000000000000000000000000000001")]
        [TestCase("format", "7")]
        [TestCase("contentEncoding", "[]")]
        [TestCase("pattern", "false")]
        [TestCase("uniqueItems", "1")]
        [TestCase("additionalProperties", "\"false\"")]
        [TestCase("uav:valueRank", "\"1\"")]
        [TestCase("uav:arrayDimensions", "[-1]")]
        [TestCase("uav:mapToType", "11")]
        [TestCase("uav:dataTypeId", "false")]
        public async Task SupportedFacetTypesAreCheckedWithoutAMatchingFacetAsync(string term, string value)
        {
            string schema = "{\"" + term + "\":" + value + "}";
            var resolver = new WotExternalSchemaResolver(new CountingProvider(schema));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Parse("{}"), string.Empty, new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
            Assert.That(result.Detail, Does.Contain("/" + term));
        }

        [TestCase(WideNumericPartialSchema)]
        [TestCase(/*lang=json,strict*/ """{"minItems":1.0,"maxLength":1e1000,"minProperties":-0.0e999999}""")]
        [TestCase(StringFacetPartialSchema)]
        [TestCase(/*lang=json,strict*/ """{"type":"array","items":{},"additionalProperties":true}""")]
        public async Task ValidPartialFacetsRemainCompatibleAsync(string schema)
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(schema));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Parse("{}"), string.Empty, new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible), result.Detail);
            Assert.That(result.Detail, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownExtensionValuesAreNotInterpretedAsSchemasAsync(bool annotatedCanonical)
        {
            const string annotated = /*lang=json,strict*/ """
                {
                  "type": "number",
                  "vendor:opaque": { "$ref": "urn:missing", "minimum": "zero", "properties": 7 },
                  "default": { "$ref": "urn:business", "minimum": "zero" }
                }
                """;
            const string partial = /*lang=json,strict*/ """{"type":"number"}""";
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(annotatedCanonical ? partial : annotated));
            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Parse(annotatedCanonical ? annotated : partial),
                string.Empty, new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible), result.Detail);
        }

        /// <summary>
        /// The first provider that holds the reference settles it, which is the
        /// same first-source precedence the local context follows.
        /// </summary>
        [Test]
        public async Task TheFirstProviderThatHoldsItSettlesItAsync()
        {
            var first = new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}");
            var second = new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}");
            var resolver = new WotExternalSchemaResolver(first, second);

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(result.ProviderIndex, Is.Zero);
                Assert.That(resolver.ProviderCount, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// A provider that does not hold the reference is skipped rather than
        /// ending the walk.
        /// </summary>
        [Test]
        public async Task ALaterProviderAnswersWhenAnEarlierOneDoesNotAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(null), new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(result.ProviderIndex, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Two providers holding different bytes for one reference is reported.
        /// Order still settles which one is read, but a federation whose members
        /// disagree about a schema is a fact its operator needs.
        /// </summary>
        [Test]
        public async Task ProvidersThatDisagreeAreAmbiguousAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"),
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"string\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Ambiguous));
                Assert.That(result.ProviderIndex, Is.Zero);
                Assert.That(result.Detail, Does.Contain("different bytes"));
            });
        }

        /// <summary>
        /// Two providers holding the same bytes agree, so there is nothing to
        /// report.
        /// </summary>
        [Test]
        public async Task ProvidersThatAgreeAreNotAmbiguousAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"),
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
        }

        [Test]
        public async Task NoProviderHoldingItIsUnresolvedAsync()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(null));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(result.Detail, Does.Contain("No configured provider"));
            });
        }

        /// <summary>
        /// A provider that answers with a media type this Binding does not read
        /// as a DataSchema has not answered the question.
        /// </summary>
        [Test]
        public async Task AnUnreadableMediaTypeIsUnresolvedAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider("<xml/>", "application/xml"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(result.Detail, Does.Contain("media type"));
            });
        }

        [Test]
        public async Task AReadableMediaTypeWithParametersIsAcceptedAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(
                                         /*lang=json,strict*/
                                         "{\"type\":\"number\"}", "application/schema+json; charset=utf-8"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(
                    result.ContentType, Is.EqualTo("application/schema+json; charset=utf-8"));
                Assert.That(WotExternalSchemaResolver.ReadableContentTypes.Count, Is.EqualTo(5));
            });
        }

        [Test]
        public async Task ABareFragmentNamesNothingAProviderCouldHoldAsync()
        {
            var probe = new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}");
            var resolver = new WotExternalSchemaResolver(probe);

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                "#/definitions/Speed",
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(probe.Calls, Is.Zero, "No provider is asked to guess.");
            });
        }

        [Test]
        public async Task AnEmptyReferenceNamesNothingAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                string.Empty,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
        }

        /// <summary>
        /// A relative path is read against the document that referenced it, so
        /// a provider can be asked for one.
        /// </summary>
        [Test]
        public async Task ARelativePathIsAskedForAsync()
        {
            var probe = new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}");
            var resolver = new WotExternalSchemaResolver(probe);

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                "schemas/speed.json",
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(probe.Calls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task SchemaBytesCountAgainstTheSharedByteBudgetAsync()
        {
            var context = new WotResolutionContext(
                new WotResolverOptions { MaxDocumentBytes = 4 });
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Schema("number"), "i=11", context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(result.Detail, Does.Contain("per-document limit"));
                Assert.That(
                    context.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.ResolverLimitExceeded),
                    Is.True);
            });
        }

        [Test]
        public async Task SchemaResolutionCountsAgainstTheSharedDocumentBudgetAsync()
        {
            var context = new WotResolutionContext(
                new WotResolverOptions { MaxDocuments = 1 });
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult first = await resolver.ResolveAndCompareAsync(
                Reference, Schema("number"), "i=11", context).ConfigureAwait(false);
            WotExternalSchemaResult second = await resolver.ResolveAndCompareAsync(
                "https://example.com/schemas/other.json",
                Schema("number"),
                "i=11",
                context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Compatible));
                Assert.That(second.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(second.Detail, Does.Contain("maximum document count"));
            });
        }

        /// <summary>
        /// A reference already being resolved is a cycle, and the shared
        /// context is what sees it - which is the point of threading one
        /// context through every kind of resolution a conversion performs.
        /// </summary>
        [Test]
        public async Task AReferenceAlreadyBeingResolvedIsACycleAsync()
        {
            var context = new WotResolutionContext();
            Assert.That(
                context.TryEnter(WotResolutionKind.Schema, Reference, out _), Is.True);
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference, Schema("number"), "i=11", context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(result.Detail, Does.Contain("cycle"));
            });
        }

        [Test]
        public async Task ANonJsonAnswerIsUnresolvedAsync()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider("not json"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Unresolved));
                Assert.That(result.Detail, Does.Contain("not JSON"));
            });
        }

        [Test]
        public async Task AJsonScalarIsNotADataSchemaAsync()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider("42"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                "i=11",
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Detail, Does.Contain("not a JSON object"));
            });
        }

        [Test]
        public void TheResolverRejectsNullArguments()
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider("{}"));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new WotExternalSchemaResolver(null!),
                    Throws.ArgumentNullException);
                Assert.That(
                    async () => await resolver.ResolveAndCompareAsync(
                        null!, Schema("number"), "i=11", new WotResolutionContext())
                        .ConfigureAwait(false),
                    Throws.ArgumentNullException);
                Assert.That(
                    async () => await resolver.ResolveAndCompareAsync(
                        Reference, Schema("number"), "i=11", null!).ConfigureAwait(false),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// The conversion reports a compatible external schema and leaves the
        /// DataType exactly where the canonical DataSchema put it.
        /// </summary>
        [Test]
        public async Task AConversionReportsACompatibleSchemaAndKeepsItsDataTypeAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"uav:externalSchema\":\"" +
                Reference +
                "\"}",
                new WotExternalSchemaResolver(
                    new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}")))
                .ConfigureAwait(false);

            UAVariable speed = result.Value!.Items!.OfType<UAVariable>().First();
            Assert.Multiple(() =>
            {
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.UnsupportedSchema &&
                            d.Severity == WotDiagnosticSeverity.Info),
                    Is.True);
                Assert.That(
                    result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False);
            });
        }

        /// <summary>
        /// An incompatible external schema is an error, and still leaves the
        /// DataType where the canonical DataSchema put it: the external
        /// description never redefines the Variable.
        /// </summary>
        [Test]
        public async Task AConversionReportsAnIncompatibleSchemaWithoutApplyingItAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"uav:externalSchema\":\"" +
                Reference +
                "\"}",
                new WotExternalSchemaResolver(
                    new CountingProvider(/*lang=json,strict*/ "{\"type\":\"string\",\"uav:mapToType\":\"i=12\"}")))
                .ConfigureAwait(false);

            UAVariable speed = result.Value!.Items!.OfType<UAVariable>().First();
            Assert.Multiple(() =>
            {
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.ExternalSchemaIncompatible &&
                            d.Severity == WotDiagnosticSeverity.Error),
                    Is.True);
            });
        }

        [Test]
        public async Task AConversionReportsAnUnresolvedSchemaAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:externalSchema\":\"" + Reference + "\"}",
                new WotExternalSchemaResolver(new CountingProvider(null)))
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.ExternalSchemaUnresolved &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                Is.True);
        }

        [Test]
        public async Task AConversionReportsAmbiguousProvidersAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:externalSchema\":\"" + Reference + "\"}",
                new WotExternalSchemaResolver(
                    new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}"),
                    new CountingProvider(/*lang=json,strict*/ "{\"type\":\"integer\"}")))
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.ExternalSchemaAmbiguous &&
                        d.Severity == WotDiagnosticSeverity.Warning),
                Is.True);
        }

        /// <summary>
        /// A conversion with an empty provider set reports the reference the
        /// way it did before providers existed: carried, not inlined.
        /// </summary>
        [Test]
        public async Task AConversionWithNoProviderReportsTheReferenceAsCarriedAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:externalSchema\":\"" + Reference + "\"}",
                new WotExternalSchemaResolver()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.UnsupportedSchema &&
                            d.Severity == WotDiagnosticSeverity.Warning &&
                            d.Message.Contains("not inlined", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(
                    result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.False);
            });
        }

        /// <summary>
        /// A property that names its BrowseName explicitly is still matched to
        /// its own resolved schema, so two affordances never swap answers.
        /// </summary>
        [Test]
        public async Task AnExplicitBrowseNameStillMatchesItsOwnAnswerAsync()
        {
            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "{\"type\":\"number\",\"uav:browseName\":\"pump:Velocity\"," +
                "\"uav:externalSchema\":\"" +
                Reference +
                "\"}",
                new WotExternalSchemaResolver(
                    new CountingProvider(/*lang=json,strict*/ "{\"type\":\"string\"}"))).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.ExternalSchemaIncompatible),
                Is.True);
        }

        /// <summary>
        /// A result names its own reason, and one that names none reads its
        /// sentence from the outcome, so a caller displaying a result never has
        /// to write the sentence itself and a diagnostic is never empty.
        /// </summary>
        [TestCase(WotExternalSchemaOutcome.NotEvaluated, "neither fetched nor evaluated")]
        [TestCase(WotExternalSchemaOutcome.Unresolved, "could not be resolved")]
        [TestCase(WotExternalSchemaOutcome.Compatible, "agrees with the canonical")]
        [TestCase(WotExternalSchemaOutcome.Incompatible, "describes different data")]
        [TestCase(WotExternalSchemaOutcome.Ambiguous, "More than one provider holds")]
        public void AResultThatNamesNoReasonReadsItFromItsOutcome(
            WotExternalSchemaOutcome outcome, string expected)
        {
            var result = new WotExternalSchemaResult
            {
                Reference = Reference,
                Outcome = outcome
            };

            Assert.Multiple(() =>
            {
                Assert.That(result.Detail, Is.Null);
                Assert.That(result.Reason, Does.Contain(expected));
                Assert.That(result.Reason, Does.Contain(Reference));
            });
        }

        /// <summary>
        /// A result that does name a reason reports that reason, because the
        /// resolver knows more about what went wrong than the outcome alone
        /// does.
        /// </summary>
        [Test]
        public async Task AResultThatNamesAReasonReportsItAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":\"string\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Schema("number"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Reason, Is.EqualTo(result.Detail));
                Assert.That(result.Reason, Does.Contain("states type"));
            });
        }

        /// <summary>
        /// Omitted member maps remain partial contracts. A present malformed
        /// map cannot establish schema compatibility.
        /// </summary>
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\"}", /*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"S\":{}}}",
            TestName = "ExternalDeclaresNoMembers")]
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":7}",
                                 /*lang=json,strict*/
                                 "{\"type\":\"object\",\"properties\":{\"S\":{}}}",
            TestName = "ExternalMemberMapIsNotAnObject")]
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"S\":{}}}",
                                 /*lang=json,strict*/
                                 "{\"type\":\"object\",\"properties\":7}",
            TestName = "CanonicalMemberMapIsNotAnObject")]
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"S\":{}}}", /*lang=json,strict*/ "{\"type\":\"object\"}",
            TestName = "CanonicalDeclaresNoMembers")]
        public async Task ASchemaWithNoMemberMapIsNotComparedMemberwiseAsync(
            string external, string canonical)
        {
            var resolver = new WotExternalSchemaResolver(new CountingProvider(external));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(canonical),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                bool malformed = external.Contains("\"properties\":7", StringComparison.Ordinal) ||
                    canonical.Contains("\"properties\":7", StringComparison.Ordinal);
                Assert.That(result.Outcome, Is.EqualTo(malformed
                    ? WotExternalSchemaOutcome.Incompatible
                    : WotExternalSchemaOutcome.Compatible));
                if (malformed)
                {
                    Assert.That(result.Detail, Does.Contain("/properties"));
                }
                else
                {
                    Assert.That(result.Detail, Is.Null);
                }
            });
        }

        /// <summary>
        /// A present invalid type term is a malformed schema, not an omitted
        /// comparison channel.
        /// </summary>
        [Test]
        public async Task ATermThatIsNotAStringStatesNothingToCompareAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(/*lang=json,strict*/ "{\"type\":7,\"unit\":\"m/s\"}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse(/*lang=json,strict*/ "{\"type\":\"string\",\"unit\":\"m/s\"}"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
            Assert.That(result.Detail, Does.Contain("/type"));
        }

        /// <summary>
        /// An empty <c>uav:externalSchema</c> names nothing a provider could
        /// hold, so nothing was fetched for it and no answer was recorded. It
        /// is reported as carried - the same as a reference no provider was
        /// configured for - rather than as an answer the conversion does not
        /// have.
        /// </summary>
        [Test]
        public async Task AnEmptyReferenceIsReportedAsCarriedAsync()
        {
            var provider = new CountingProvider(/*lang=json,strict*/ "{\"type\":\"number\"}");

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                                     /*lang=json,strict*/
                                     "{\"type\":\"number\",\"uav:externalSchema\":\"\"}",
                new WotExternalSchemaResolver(provider)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    provider.Calls,
                    Is.Zero,
                    "An empty reference names nothing, so no provider was asked.");
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.UnsupportedSchema &&
                            d.Severity == WotDiagnosticSeverity.Warning &&
                            d.Message.Contains("not inlined", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// A non-schema canonical value cannot certify an external schema as
        /// compatible. The earlier case identity is retained as negative coverage.
        /// </summary>
        [Test]
        public async Task ACanonicalSchemaThatIsNotAnObjectStatesNothingToCompareAsync()
        {
            var resolver = new WotExternalSchemaResolver(
                new CountingProvider(
                    "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                    "\"properties\":{\"Speed\":{\"type\":\"number\"}}}"));

            WotExternalSchemaResult result = await resolver.ResolveAndCompareAsync(
                Reference,
                Parse("7"),
                string.Empty,
                new WotResolutionContext()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WotExternalSchemaOutcome.Incompatible));
                Assert.That(result.Detail, Does.Contain("canonical schema"));
            });
        }

        private static System.Text.Json.JsonElement Schema(string type)
        {
            return Parse("{\"type\":\"" + type + "\"}");
        }

        private static System.Text.Json.JsonElement Parse(string json)
        {
            // Cloned so the element survives the JsonDocument it was read from.
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            string propertySchema,
            WotExternalSchemaResolver schemaResolver)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1042\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Speed\":" +
                propertySchema +
                "}}");

            using var document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, null, schemaResolver).ConfigureAwait(false);
        }

        /// <summary>
        /// A provider that answers with fixed bytes and counts how often it was
        /// asked, so a test can prove that nothing was fetched.
        /// </summary>
        private sealed class CountingProvider(string? body, string? contentType = null)
            : IWotSchemaResolver
        {
            public int Calls { get; private set; }

            public ValueTask<WotResolverResult> ResolveSchemaAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                Calls++;
                return new ValueTask<WotResolverResult>(
                    body is null
                        ? WotResolverResult.NotFound
                        : WotResolverResult.FromBytes(
                            WotTestData.Utf8(body), contentType));
            }
        }
    }
}
