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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotSemanticTypeResidueTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void UnmappedTypeAnnotationsSurviveRepeatedReadableRoundTrips(bool onProperty)
        {
            string types = onProperty ? "[\"tm:ThingModel\",\"uav:objectType\"]" :
                "[\"tm:ThingModel\",\"uav:objectType\",\"urn:sem:Sensor\"]";
            string properties = onProperty
                ? /*lang=json,strict*/ """
                  { "Value": {
                    "@type": "urn:sem:Sensor", "type": "number",
                    "uav:browseName": "device:Value", "uav:mapToType": "i=11"
                  } }
                  """
                : "{}";
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "device": "urn:semantic-type#" }
                  ],
                  "@type": {{types}},
                  "title": "Sensor",
                  "uav:browseName": "device:Sensor",
                  "properties": {{properties}}
                }
                """;
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> first = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(first.Success, Is.True, string.Join("; ", first.Diagnostics));
            UANodeSet nodes = first.Value!;

            for (int iteration = 0; iteration < 2; iteration++)
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodes);
                JsonElement owner = onProperty
                    ? restored.RootElement.GetProperty("properties").GetProperty("Value")
                    : restored.RootElement;
                JsonElement actual = owner.GetProperty("@type");
                string[] tokens = actual.ValueKind == JsonValueKind.String
                    ? [actual.GetString()!]
                    : [.. actual.EnumerateArray().Select(token => token.GetString()!)];
                Assert.That(tokens, Does.Contain("urn:sem:Sensor"));
                Assert.That(tokens.Count(token => token == "urn:sem:Sensor"), Is.EqualTo(1));
                WotConversionResult<UANodeSet> converted = WotNodeSetConverter.ToNodeSetResult(restored);
                Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics));
                nodes = converted.Value!;
            }
        }

        [TestCase("actions", "uav:method")]
        [TestCase("events", "uav:eventType")]
        public void InteractionTypeAnnotationsRetainOrderWithoutDuplicatingNativeMarkers(
            string map, string nativeType)
        {
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "device": "urn:semantic-type#" }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Device",
                  "uav:browseName": "device:Device",
                  "{{map}}": {
                    "Interaction": {
                      "@type": ["{{nativeType}}", "urn:sem:First", "urn:sem:Second", "urn:sem:First"],
                      "uav:browseName": "device:Interaction"
                    }
                  }
                }
                """;
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodes);
                string[] tokens =
                [
                    .. restored.RootElement.GetProperty(map).GetProperty("Interaction").GetProperty("@type")
                        .EnumerateArray().Select(token => token.GetString()!)
                ];
                Assert.That(tokens, Is.EqualTo([nativeType, "urn:sem:First", "urn:sem:Second"]));
                nodes = WotNodeSetConverter.ToNodeSet(restored);
            }
        }

        [Test]
        public void TypeAnnotationKeepsItsAffordanceLocalPrefixBinding()
        {
            const string json = /*lang=json,strict*/ """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "device": "urn:semantic-type#",
                      "semantic": "urn:root-semantic#"
                    }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Sensor",
                  "uav:browseName": "device:Sensor",
                  "properties": {
                    "Value": {
                      "@context": { "semantic": "urn:property-semantic#" },
                      "@type": "semantic:Measurement",
                      "uav:browseName": "device:Value",
                      "uav:mapToType": "i=11",
                      "type": "number"
                    }
                  }
                }
                """;
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodes);

            JsonElement property = restored.RootElement.GetProperty("properties").GetProperty("Value");
            Assert.That(property.GetProperty("@type").EnumerateArray().Select(token => token.GetString()),
                Does.Contain("semantic:Measurement"));
            Assert.That(property.GetProperty("@context").GetProperty("semantic").GetString(),
                Is.EqualTo("urn:property-semantic#"));
        }

        [TestCase("[\"uav:method\"]", WotDiagnosticCode.ResidueConflict, false)]
        [TestCase("[\"uav:variable\"]", WotDiagnosticCode.ResidueConflict, false)]
        [TestCase("[42]", WotDiagnosticCode.ResidueInvalid, false)]
        [TestCase("{}", WotDiagnosticCode.ResidueInvalid, false)]
        [TestCase("null", WotDiagnosticCode.ResidueInvalid, false)]
        [TestCase("[\"tm:ThingModel\",\"uav:objectType\"]", WotDiagnosticCode.ResidueConflict, true)]
        public void SemanticTypeResidueCannotOverrideNativeFactsOrSupplyInvalidTokens(
            string invalid, WotDiagnosticCode expected, bool repeatsExistingFacts)
        {
            const string json = /*lang=json,strict*/ """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "device": "urn:semantic-type#" }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType", "urn:sem:Sensor"],
                  "title": "Sensor",
                  "uav:browseName": "device:Sensor"
                }
                """;
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            System.Xml.XmlElement extension =
                nodes.Extensions!.Single(element => element.LocalName == "WoTJsonResidue");
            System.Xml.XmlElement entry = extension.ChildNodes.OfType<System.Xml.XmlElement>()
                .Single(element => element.GetAttribute("Pointer") == "/@type");
            byte[] bytes = Encoding.UTF8.GetBytes(invalid);
#if NET8_0_OR_GREATER
            byte[] digest = SHA256.HashData(bytes);
#else
            using var hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(bytes);
#endif
            entry.SetAttribute("Sha256", CoreUtils.ToHexString(digest).ToLowerInvariant());
            entry.InnerText = Convert.ToBase64String(bytes);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(nodes);
            using WotDocument restored = result.Value!;
            Assert.That(result.Success, Is.EqualTo(repeatsExistingFacts), string.Join("; ", result.Diagnostics));
            if (!repeatsExistingFacts)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == expected && diagnostic.Location?.JsonPointer == "/@type"), Is.True,
                    string.Join("; ", result.Diagnostics));
            }
        }
    }
}
