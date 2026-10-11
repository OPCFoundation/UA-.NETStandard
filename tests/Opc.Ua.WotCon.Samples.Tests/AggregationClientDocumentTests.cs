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

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using AggregationClient;

namespace Opc.Ua.WotCon.Samples.Tests
{
    [TestFixture]
    public sealed class AggregationClientDocumentTests
    {
        [Test]
        public void AuthoredIdentityKeepsExactDocumentBytes()
        {
            byte[] content = Encoding.UTF8.GetBytes("{ \"id\" : \"urn:authored\", \"value\" : 1.00 }\r\n");

            byte[] output = AggregationClientRunner.EnsureDocumentIdentity(content, "other-name");

            Assert.That(output, Is.SameAs(content));
            Assert.That(Encoding.UTF8.GetString(output), Does.Contain("\"urn:authored\""));
        }

        [Test]
        public void NativeFragmentGetsAnExplicitPublisherIdentityWithoutChangingNativeFacts()
        {
            byte[] content = Encoding.UTF8.GetBytes(
                """{"uav:id":"nsu=urn:model;i=42","uav:nodes":{"ns=1;i=42":{"value":1.00}},"x-vendor":"kept"}""");

            byte[] output = AggregationClientRunner.EnsureDocumentIdentity(content, "model/type");

            using JsonDocument document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("id").GetString(),
                Is.EqualTo("urn:opcfoundation.org:UA:WotAggregation:document:model%2Ftype"));
            Assert.That(document.RootElement.GetProperty("uav:id").GetString(), Is.EqualTo("nsu=urn:model;i=42"));
            Assert.That(document.RootElement.GetProperty("uav:nodes").GetRawText(),
                Is.EqualTo("""{"ns=1;i=42":{"value":1.00}}"""));
            Assert.That(document.RootElement.GetProperty("x-vendor").GetString(), Is.EqualTo("kept"));
            Assert.That(Encoding.UTF8.GetString(content), Does.Not.Contain("WotAggregation:document"));
        }

        [TestCase("{not json}")]
        [TestCase("[]")]
        [TestCase("null")]
        public void InvalidPublisherDocumentReportsDecodingFailure(string content)
        {
            Assert.That(() => AggregationClientRunner.EnsureDocumentIdentity(
                Encoding.UTF8.GetBytes(content), "invalid"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void PublishedLinksUseSourceIdentitiesWithoutRewritingOpaqueValuesOrForms()
        {
            byte[] content = Encoding.UTF8.GetBytes("""
                {
                  "links":[{"href":"source#/schemaDefinitions/Reading"}],
                  "uav:projects":[{"href":"source","uav:sourceName":"source"}],
                  "properties":{"value":{"tm:ref":"source#/properties/Reading",
                    "forms":[{"href":"source"}],"default":{"tm:ref":"source"}}},
                  "events":{"alarm":{"uav:eventSelectClauses":[{"tm:ref":"source#/schemaDefinitions/Alarm"}]}},
                  "uav:nodes":{"tm:ref":"source"},"x-vendor":{"links":[{"href":"source"}]}
                }
                """);
            var identities = new Dictionary<string, string> { ["source"] = "urn:published:source" };

            byte[] output = AggregationClientRunner.RewriteDocumentReferences(content, identities);

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("urn:published:source#/schemaDefinitions/Reading"));
            Assert.That(root.GetProperty("uav:projects")[0].GetProperty("href").GetString(),
                Is.EqualTo("urn:published:source"));
            Assert.That(root.GetProperty("uav:projects")[0].GetProperty("uav:sourceName").GetString(),
                Is.EqualTo("source"));
            JsonElement value = root.GetProperty("properties").GetProperty("value");
            Assert.That(value.GetProperty("tm:ref").GetString(),
                Is.EqualTo("urn:published:source#/properties/Reading"));
            Assert.That(value.GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo("source"));
            Assert.That(value.GetProperty("default").GetProperty("tm:ref").GetString(), Is.EqualTo("source"));
            Assert.That(root.GetProperty("events").GetProperty("alarm").GetProperty("uav:eventSelectClauses")[0]
                .GetProperty("tm:ref").GetString(), Is.EqualTo("urn:published:source#/schemaDefinitions/Alarm"));
            Assert.That(root.GetProperty("uav:nodes").GetProperty("tm:ref").GetString(), Is.EqualTo("source"));
            Assert.That(root.GetProperty("x-vendor").GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("source"));
        }

        [Test]
        public void SubstitutionTargetsOnlyRootAndAffordanceForms()
        {
            const string input =
                """
                {
                  "forms": [{ "href": "${SOURCE_A_ENDPOINT}" }],
                  "properties": {
                    "value": {
                      "forms": [{ "href": "${SOURCE_A_ENDPOINT}" }],
                      "default": { "href": "${SOURCE_B_ENDPOINT}" }
                    }
                  },
                  "actions": { "start": { "forms": [{ "href": "${SOURCE_B_ENDPOINT}" }] } },
                  "events": { "alarm": { "forms": [{ "href": "${SOURCE_A_ENDPOINT}" }] } },
                  "links": [{ "href": "${SOURCE_A_ENDPOINT}" }],
                  "x-vendor": { "href": "${SOURCE_B_ENDPOINT}" }
                }
                """;
            var options = new AggregationClientOptions();
            byte[] output = AggregationClientRunner.SubstituteEndpoints(input, options);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(options.SourceAEndpoint));
            Assert.That(root.GetProperty("properties").GetProperty("value")
                .GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo(options.SourceAEndpoint));
            Assert.That(root.GetProperty("actions").GetProperty("start")
                .GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo(options.SourceBEndpoint));
            Assert.That(root.GetProperty("events").GetProperty("alarm")
                .GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo(options.SourceAEndpoint));
            Assert.That(root.GetProperty("properties").GetProperty("value")
                .GetProperty("default").GetProperty("href").GetString(), Is.EqualTo("${SOURCE_B_ENDPOINT}"));
            Assert.That(root.GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("${SOURCE_A_ENDPOINT}"));
            Assert.That(root.GetProperty("x-vendor").GetProperty("href").GetString(),
                Is.EqualTo("${SOURCE_B_ENDPOINT}"));
        }

        [TestCase("uav:nodes")]
        [TestCase("uav:nodeSet")]
        [TestCase("uav:metadata")]
        [TestCase("uav:propertyConfiguration")]
        [TestCase("uav:actionConfiguration")]
        [TestCase("uav:eventConfiguration")]
        [TestCase("x-vendor")]
        public void SubstitutionDoesNotInterpretFormsInOpaqueContent(string name)
        {
            string input = $$"""
                {
                  "forms": [{ "href": "${SOURCE_A_ENDPOINT}" }],
                  "{{name}}": { "forms": [{ "href": "${SOURCE_B_ENDPOINT}" }] }
                }
                """;
            var options = new AggregationClientOptions();
            byte[] output = AggregationClientRunner.SubstituteEndpoints(input, options);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(options.SourceAEndpoint));
            Assert.That(document.RootElement.GetProperty(name)
                .GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo("${SOURCE_B_ENDPOINT}"));
        }

        [TestCase("{ \"x-vendor\": { \"href\": \"${SOURCE_A_ENDPOINT}\" } }\r\n")]
        [TestCase("{ \"forms\": [{ \"href\": \"${SOURCE_A_ENDPOINT}/suffix\" }] }\r\n")]
        [TestCase("{ \"title\": \"No placeholders\", \"value\": 1.00 }\r\n")]
        public void DocumentsWithoutSubstitutableFormsKeepTheirOriginalBytes(string input)
        {
            byte[] output = AggregationClientRunner.SubstituteEndpoints(input, new AggregationClientOptions());
            Assert.That(output, Is.EqualTo(Encoding.UTF8.GetBytes(input)));
        }
    }
}
