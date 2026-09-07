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
