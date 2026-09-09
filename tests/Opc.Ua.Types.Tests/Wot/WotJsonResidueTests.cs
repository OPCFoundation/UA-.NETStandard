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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using SysXmlDocument = System.Xml.XmlDocument;
using SysXmlElement = System.Xml.XmlElement;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotJsonResidueTests
    {
        private const string s_ns = WotNodeSetConverter.VocabularyNamespace;

        [TestCase(WotNodeSetPreservationMode.Never)]
        [TestCase(WotNodeSetPreservationMode.Always)]
        public void OpaqueValuesRetainTheirExactLexicalRepresentationAcrossRoundTrips(
            WotNodeSetPreservationMode preservation)
        {
            const string opaque = /*lang=json,strict*/ """{ "a\u0062" : "\u0041", "n":1e+003, "items" : [ 1.00, -0, "\/" ] }""";
            const string alternative = /*lang=json,strict*/ """{"ab":"A","n":1000,"items":[1,0,"/"]}""";
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "ua": "http://opcfoundation.org/UA/",
                      "device": "urn:opaque-lexical#",
                      "vendor": "urn:opaque-vendor#"
                    }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Device",
                  "uav:browseName": "device:Device",
                  "uav:metadata": {{opaque}},
                  "vendor:payload": [
                    {"uav:metadata": {{opaque}}},
                    {"uav:metadata": {{alternative}}}
                  ],
                  "properties": {
                    "Value": {
                      "type": "number",
                      "uav:mapToType": "i=11",
                      "uav:browseName": "device:Value",
                      "uav:propertyConfiguration": {{alternative}}
                    }
                  }
                }
                """;
            var options = new WotNodeSetConverterOptions { PreservationMode = preservation };
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json), options);
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodes, options: options);
                JsonElement property = restored.RootElement.GetProperty("properties").GetProperty("Value");

                Assert.That(restored.RootElement.GetProperty("uav:metadata").GetRawText(), Is.EqualTo(opaque));
                Assert.That(property.GetProperty("uav:propertyConfiguration").GetRawText(), Is.EqualTo(alternative));
                JsonElement payload = restored.RootElement.GetProperty("vendor:payload");
                Assert.That(payload[0].GetProperty("uav:metadata").GetRawText(), Is.EqualTo(opaque));
                Assert.That(payload[1].GetProperty("uav:metadata").GetRawText(), Is.EqualTo(alternative));
                Assert.That(property.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=11"));

                nodes = WotNodeSetConverter.ToNodeSet(restored, options);
            }
        }

        [TestCase(WotNodeSetPreservationMode.Never)]
        [TestCase(WotNodeSetPreservationMode.Always)]
        public void PropertyLinkResidueRemainsOnItsPropertyInsteadOfCreatingARootTypeLink(
            WotNodeSetPreservationMode preservation)
        {
            const string opaque = /*lang=json,strict*/ """{ "text" : "\u0041", "values" : [ 1.00, 2e+003 ] }""";
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "ua": "http://opcfoundation.org/UA/",
                      "device": "urn:link-owner#",
                      "vendor": "urn:vendor#"
                    }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Device",
                  "uav:browseName": "device:Device",
                  "properties": {
                    "Value": {
                      "type": "number", "uav:mapToType": "i=11", "uav:browseName": "device:Value",
                      "links": [{
                        "rel": "ua:HasTypeDefinition", "href": "nsu=http://opcfoundation.org/UA/;i=68",
                        "vendor:note": "belongs to Value",
                        "uav:metadata": {{opaque}}
                      }]
                    }
                  }
                }
                """;
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            var options = new WotNodeSetConverterOptions { PreservationMode = preservation };
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodes, options: options);
                JsonElement property = restored.RootElement.GetProperty("properties").GetProperty("Value");
                JsonElement[] annotated = [.. property.GetProperty("links").EnumerateArray().Where(link => link.TryGetProperty("vendor:note", out _))];
                Assert.That(annotated, Has.Length.EqualTo(1));
                Assert.That(annotated[0].GetProperty("vendor:note").GetString(), Is.EqualTo("belongs to Value"));
                Assert.That(annotated[0].GetProperty("rel").GetString(), Is.EqualTo("ua:HasTypeDefinition"));
                Assert.That(annotated[0].GetProperty("uav:metadata").GetRawText(), Is.EqualTo(opaque));
                if (restored.RootElement.TryGetProperty("links", out JsonElement rootLinks))
                {
                    Assert.That(rootLinks.EnumerateArray().Any(link => link.TryGetProperty("vendor:note", out _)),
                        Is.False);
                }
                nodes = WotNodeSetConverter.ToNodeSet(restored, options);
            }
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        public void RawOpaqueInsertionStillEnforcesTheCombinedDocumentDepth(int depth, bool accepted)
        {
            const string opaque = /*lang=json,strict*/ """{ "x" : { "value" : "\u0041" } }""";
            byte[] generated = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"a":{"b":{}}}""");
            var nodes = new UANodeSet
            {
                Extensions = [CreateResidueExtension("1.0", CreateResidueMember("/a/b/uav:metadata", opaque))]
            };
            var diagnostics = new List<WotDiagnostic>();

            byte[] output = WotJsonResidue.Apply(
                generated, nodes, new WotNodeSetConverterOptions { MaxJsonDepth = depth }, diagnostics);

            if (accepted)
            {
                Assert.That(diagnostics, Is.Empty);
                using var result = JsonDocument.Parse(output);
                Assert.That(result.RootElement.GetProperty("a").GetProperty("b").GetProperty("uav:metadata").GetRawText(),
                    Is.EqualTo(opaque));
            }
            else
            {
                Assert.That(diagnostics.Any(item => item.Code == WotDiagnosticCode.ResidueInvalid), Is.True);
                Assert.That(output, Is.EqualTo(generated));
            }
        }

        [Test]
        public void ASubsequentEntryCannotBeSilentlyHiddenByAnOpaqueRawValue()
        {
            var nodes = new UANodeSet
            {
                Extensions =
                [
                    CreateResidueExtension("1.0",
                        CreateResidueMember("/uav:metadata", /*lang=json,strict*/ """{"x":1}"""),
                        CreateResidueMember("/uav:metadata/y", "2"))
                ]
            };
            var diagnostics = new List<WotDiagnostic>();

            byte[] output = WotJsonResidue.Apply(
                Encoding.UTF8.GetBytes("{}"), nodes, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics.Any(item => item.Code == WotDiagnosticCode.ResidueConflict), Is.True);
            using var result = JsonDocument.Parse(output);
            Assert.That(result.RootElement.GetProperty("uav:metadata").GetProperty("y").GetInt32(), Is.EqualTo(2));
        }

        [Test]
        public void EquivalentGeneratedOpaqueValueRetainsItsAuthoredNumericSpelling()
        {
            const string original = /*lang=json,strict*/ """{ "n": 1 }""";
            var nodes = new UANodeSet
            {
                Extensions = [CreateResidueExtension("1.0", CreateResidueMember("/uav:metadata", original))]
            };
            var diagnostics = new List<WotDiagnostic>();

            byte[] output = WotJsonResidue.Apply(
                Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"uav:metadata":{"n":1.0}}"""),
                nodes, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            using var result = JsonDocument.Parse(output);
            Assert.That(result.RootElement.GetProperty("uav:metadata").GetRawText(), Is.EqualTo(original));
        }

        [Test]
        public void ApplyWithNullExtensionsReturnsOriginalBytes()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            var nodeSet = new UANodeSet { Extensions = null };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(json));
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyWithNoMatchingExtensionReturnsOriginalBytes()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            var doc = new SysXmlDocument { XmlResolver = null };
            SysXmlElement unrelated = doc.CreateElement("vendor", "Custom", "urn:vendor");
            var nodeSet = new UANodeSet { Extensions = [unrelated] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(json));
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyRejectsUnsupportedVersion()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement ext = CreateResidueExtension(
                "99.0",
                CreateResidueMember("/extra", "\"hello\""));
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(json));
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsNonBase64Encoding()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/extra", "\"hello\"");
            member.SetAttribute("Encoding", "hex");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsInvalidBase64Content()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement ext = CreateResidueExtension("1.0");
            SysXmlElement member = ext.OwnerDocument!.CreateElement("uav", "Member", s_ns);
            member.SetAttribute("Pointer", "/extra");
            member.SetAttribute("Encoding", "base64");
            member.SetAttribute("Sha256", new string('0', 64));
            member.InnerText = "not!valid!base64===";
            ext.AppendChild(member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsDigestMismatch()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember(
                "/extra",
                "\"hello\"",
                sha256Override: new string('a', 64));
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsInvalidJsonPointerWithoutLeadingSlash()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("noleadingslash", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsPointerExceedingMaxDepth()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            string deepPointer = "/" + string.Join("/", Enumerable.Repeat("a", 130));
            SysXmlElement member = CreateResidueMember(deepPointer, "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 128 };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, options, diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyRejectsOversizedResidue()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            string largeJson = "\"" + new string('x', 200) + "\"";
            SysXmlElement member = CreateResidueMember("/extra", largeJson);
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var options = new WotNodeSetConverterOptions { MaxJsonDocumentSize = 100 };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, options, diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.JsonDocumentTooLarge),
                Is.True);
        }

        [Test]
        public void ApplyAddsNewMemberToObjectDocument()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/vendor:extra", /*lang=json,strict*/ "{\"key\":\"value\"}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("vendor:extra"));
            Assert.That(resultJson, Does.Contain("\"key\""));
        }

        [Test]
        public void ApplyAppendsToLinksArrayWithDashPointer()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"links\":[{\"rel\":\"existing\",\"href\":\"urn:x\"}]}");
            const string linkJson = /*lang=json,strict*/ "{\"rel\":\"extra\",\"href\":\"urn:y\"}";
            SysXmlElement member = CreateResidueMember("/links/-", linkJson);
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("urn:y"));
            Assert.That(resultJson, Does.Contain("urn:x"));
        }

        [Test]
        public void ApplyReportsConflictWhenMemberValueDiffers()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"vendor:x\":\"original\"}");
            SysXmlElement member = CreateResidueMember("/vendor:x", "\"conflicting\"");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        /// <summary>
        /// WoT Binding Section 9.4 asks whether a residue entry holds the same
        /// <em>value</em> as the member already there, and RFC 8785 is what
        /// answers it. A reordered object, an equivalent escape and a different
        /// number spelling are spellings of one value, not conflicts.
        /// </summary>
        [TestCase(/*lang=json,strict*/ "{\"a\":1,\"b\":2}", /*lang=json,strict*/ "{\"b\":2,\"a\":1}", TestName =
            "ResidueEqualityIgnoresMemberOrder")]
        [TestCase("\"caf\\u00e9\"", "\"caf\u00e9\"", TestName =
            "ResidueEqualityIgnoresEquivalentEscapes")]
        [TestCase("1.0", "1", TestName = "ResidueEqualityIgnoresATrailingZero")]
        [TestCase("1e2", "100.0", TestName = "ResidueEqualityIgnoresExponentForm")]
        [TestCase(/*lang=json,strict*/ "{\"a\":[1.0,2e0]}", /*lang=json,strict*/ "{\"a\":[1,2]}", TestName =
            "ResidueEqualityReachesIntoArrays")]
        public void ApplyReportsNoConflictForTwoSpellingsOfOneValue(
            string existing, string residue)
        {
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"vendor:x\":" + existing + "}");
            SysXmlElement member = CreateResidueMember("/vendor:x", residue);
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Where(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.Empty,
                "The two are the same JSON value under RFC 8785, so nothing is in conflict.");
        }

        [TestCase(/*lang=json,strict*/ "{\"a\":1,\"b\":2}", /*lang=json,strict*/ "{\"b\":2,\"a\":3}", TestName =
            "ResidueConflictSurvivesReordering")]
        [TestCase("1.0", "1.5", TestName = "ResidueConflictSurvivesNumberNormalization")]
        [TestCase(/*lang=json,strict*/ "{\"a\":[1,2]}", /*lang=json,strict*/ "{\"a\":[2,1]}", TestName =
            "ResidueConflictKeepsArrayOrderSignificant")]
        [TestCase("\"1\"", "1", TestName = "ResidueConflictSeparatesAStringFromANumber")]
        public void ApplyStillReportsAConflictForARealDifference(
            string existing, string residue)
        {
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"vendor:x\":" + existing + "}");
            SysXmlElement member = CreateResidueMember("/vendor:x", residue);
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True,
                "Canonicalizing two values never makes two different values one.");
        }

        [Test]
        public void ApplyReportsAConflictWhenAValueCannotBeCanonicalized()
        {
            // A literal an IEEE-754 double cannot hold is outside the
            // interoperable domain RFC 8785 is defined over, so the two are
            // compared as written instead: that can report a conflict the
            // scheme would not, and never reports two values as one.
            byte[] json = WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     "{\"title\":\"T\",\"vendor:x\":9007199254740993}");
            SysXmlElement member = CreateResidueMember("/vendor:x", "9007199254740992");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ApplyReportsInvalidTargetPointerForNonObjectRoot()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/title/nested", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d =>
                    d.Code is WotDiagnosticCode.ResidueInvalid or
                    WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ApplyHandlesInvalidJsonInGeneratedDocument()
        {
            byte[] invalidJson = WotTestData.Utf8("{ not valid json");
            SysXmlElement member = CreateResidueMember("/extra", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(
                invalidJson, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(invalidJson));
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyHandlesNullJsonTokenAsGeneratedDocument()
        {
            byte[] nullJson = WotTestData.Utf8("null");
            SysXmlElement member = CreateResidueMember("/extra", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(
                nullJson, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(nullJson));
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyInvalidJsonInResidueEntryIsSkipped()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            byte[] badJson = WotTestData.Utf8("{broken json");
            string sha256 = ComputeSha256Hex(badJson);
            SysXmlElement ext = CreateResidueExtension("1.0");
            SysXmlDocument doc = ext.OwnerDocument!;
            SysXmlElement member = doc.CreateElement("uav", "Member", s_ns);
            member.SetAttribute("Pointer", "/extra");
            member.SetAttribute("Encoding", "base64");
            member.SetAttribute("Sha256", sha256);
            member.InnerText = Convert.ToBase64String(badJson);
            ext.AppendChild(member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ReplacePreservesUnrelatedExtensions()
        {
            var doc = new SysXmlDocument { XmlResolver = null };
            SysXmlElement unrelated = doc.CreateElement("vendor", "Custom", "urn:vendor");

            var nodeSet = new UANodeSet { Extensions = [unrelated] };
            byte[] trivialJson = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            using var document = WotDocument.Parse(trivialJson);
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(nodeSet.Extensions, Is.Not.Null);
            Assert.That(
                nodeSet.Extensions!.Any(e =>
                    string.Equals(e.LocalName, "Custom", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void ReplaceDropsExistingResidueExtension()
        {
            SysXmlElement residue = CreateResidueExtension("1.0", CreateResidueMember("/old", "1"));
            var nodeSet = new UANodeSet { Extensions = [residue] };

            byte[] emptyDocument = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            using var document = WotDocument.Parse(emptyDocument);
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(nodeSet.Extensions, Is.Null.Or.Empty);
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ReplaceReportsOversizedResidueWhenCapturing()
        {
            string largeUnknown = "\"" + new string('x', 200) + "\"";
            byte[] docJson = WotTestData.Utf8("{\"title\":\"T\",\"vendor:big\":" + largeUnknown + "}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var options = new WotNodeSetConverterOptions { MaxJsonDocumentSize = 50 };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, options, diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.JsonDocumentTooLarge),
                Is.True);
        }

        [Test]
        public void ReplaceRoundTripsUnknownRootMembersViaApply()
        {
            byte[] docJson = WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     "{\"title\":\"T\",\"vendor:meta\":{\"count\":42,\"tag\":\"test\"}}");
            using var document = WotDocument.Parse(docJson);

            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();
            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostics, Is.Empty);

            byte[] baseJson = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            var applyDiagnostics = new List<WotDiagnostic>();
            byte[] result = WotJsonResidue.Apply(
                baseJson, nodeSet, new WotNodeSetConverterOptions(), applyDiagnostics);

            Assert.That(applyDiagnostics, Is.Empty);
            string resultText = Encoding.UTF8.GetString(result);
            Assert.That(resultText, Does.Contain("vendor:meta"));
            Assert.That(resultText, Does.Contain("42"));
        }

        [Test]
        public void ReplaceCapturableBrowseNameWithNsuPrefix()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"properties\":{\"prop\":{" +
                "\"uav:browseName\":\"nsu=urn:test;MyProp\"," +
                "\"vendor:extra\":99}}}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            SysXmlElement residue = nodeSet.Extensions!
                .FirstOrDefault(e => string.Equals(e.LocalName, "WoTJsonResidue", StringComparison.Ordinal));
            Assert.That(residue, Is.Not.Null);
            string xml = residue!.OuterXml;
            Assert.That(xml, Does.Contain("MyProp"));
        }

        [Test]
        public void ReplaceCapturableBrowseNameWithoutColonUsesFullName()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"properties\":{\"prop\":{" +
                "\"uav:browseName\":\"NoPrefixName\"," +
                "\"vendor:extra\":99}}}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ReplaceCapturableBrowseNameWithNsuPrefixMissingSemicolon()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"properties\":{\"prop\":{" +
                "\"uav:browseName\":\"nsu=urn:testwithnosemicolon\"," +
                "\"vendor:extra\":99}}}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyCreatesIntermediateObjectForDeepPointer()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/nested/deep", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("\"nested\""));
            Assert.That(resultJson, Does.Contain("\"deep\""));
            Assert.That(resultJson, Does.Contain("42"));
        }

        [Test]
        public void ApplySetsArrayElementAtNumericIndex()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"items\":[\"a\",\"b\",\"c\"]}");
            SysXmlElement member = CreateResidueMember("/items/3", "\"d\"");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("\"d\""));
        }

        [Test]
        public void ApplyMultipleEntriesWithinSingleResidueExtension()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement ext = CreateResidueExtension(
                "1.0",
                CreateResidueMember("/vendor:a", "1"),
                CreateResidueMember("/vendor:b", "2"));
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("vendor:a"));
            Assert.That(resultJson, Does.Contain("vendor:b"));
        }

        [Test]
        public void ApplyEntryTraversesArrayParentByNumericIndex()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"matrix\":[[1,2],[3,4]]}");
            SysXmlElement member = CreateResidueMember("/matrix/0/2", "99");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("99"));
        }

        [Test]
        public void ApplyEntryNoConflictWhenValueMatchesExistingMember()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"vendor:x\":\"original\"}");
            SysXmlElement member = CreateResidueMember("/vendor:x", "\"original\"");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("original"));
        }

        [Test]
        public void ApplyEntryArrayIndexConflictEmitsResidueConflict()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"items\":[\"existing-value\"]}");
            SysXmlElement member = CreateResidueMember("/items/0", "\"different-value\"");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ApplyEntryOutOfRangeArrayIndexEmitsResidueInvalid()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"items\":[\"a\",\"b\",\"c\"]}");
            SysXmlElement member = CreateResidueMember("/items/5", "\"x\"");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyLinkEntryCreatesNewLinkAndLinksArrayWhenAbsent()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateLinkResidueMember("my-rel", "urn:x", "{}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("my-rel"));
            Assert.That(resultJson, Does.Contain("urn:x"));
        }

        [Test]
        public void ApplyLinkEntryAddsExtrasToNewLink()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateLinkResidueMember(
                "custom:rel",
                "urn:new",
                                     /*lang=json,strict*/
                                     "{\"custom-field\":\"custom-value\"}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("custom-field"));
            Assert.That(resultJson, Does.Contain("custom-value"));
        }

        [Test]
        public void ApplyLinkEntryMergesExtrasIntoExistingExactMatchLink()
        {
            byte[] json = WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     "{\"title\":\"T\",\"links\":[{\"rel\":\"my-rel\",\"href\":\"urn:x\"}]}");
            SysXmlElement member = CreateLinkResidueMember(
                "my-rel",
                "urn:x",
                                     /*lang=json,strict*/
                                     "{\"extra-field\":\"extra-value\"}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            string resultJson = Encoding.UTF8.GetString(result);
            Assert.That(resultJson, Does.Contain("extra-field"));
        }

        [Test]
        public void ApplyLinkEntryFindsLinkByRefIdWhenRelDiffers()
        {
            byte[] json = WotTestData.Utf8(
                "{\"title\":\"T\",\"links\":" +
                "[{\"rel\":\"server-rel\",\"href\":\"urn:x\",\"uav:refId\":\"my-ref\"}]}");
            SysXmlElement member = CreateLinkResidueMember(
                "client-rel",
                "urn:x",
                "{}",
                refId: "my-ref");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyLinkEntryReportsConflictForNonArrayLinksKey()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"links\":\"not-an-array\"}");
            SysXmlElement member = CreateLinkResidueMember("my-rel", "urn:x", "{}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ApplyLinkEntryReportsInvalidWhenExtrasValueIsNotJsonObject()
        {
            byte[] json = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\"}");
            SysXmlElement member = CreateLinkResidueMember("my-rel", "urn:x", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ApplyLinkEntryReportsConflictWhenRefIdMismatch()
        {
            byte[] json = WotTestData.Utf8(
                "{\"title\":\"T\",\"links\":" +
                "[{\"rel\":\"my-rel\",\"href\":\"urn:x\",\"uav:refId\":\"old-id\"}]}");
            SysXmlElement member = CreateLinkResidueMember(
                "my-rel",
                "urn:x",
                "{}",
                refId: "new-id");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ApplyLinkEntryReportsConflictWhenExtraPropertyMismatch()
        {
            byte[] json = WotTestData.Utf8(
                "{\"title\":\"T\",\"links\":" +
                "[{\"rel\":\"my-rel\",\"href\":\"urn:x\",\"custom\":\"existing-val\"}]}");
            SysXmlElement member = CreateLinkResidueMember(
                "my-rel",
                "urn:x",
                                     /*lang=json,strict*/
                                     "{\"custom\":\"different-val\"}");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueConflict),
                Is.True);
        }

        [Test]
        public void ReplaceCaptureSetsNonArrayContextAsResidueEntry()
        {
            byte[] docJson = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"@context\":\"urn:custom-ctx\"}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            string xml = nodeSet.Extensions![0].OuterXml;
            Assert.That(xml, Does.Contain("@context"));
        }

        [Test]
        public void ReplaceCapturesNonObjectAffordanceMapAsWholeEntry()
        {
            byte[] docJson = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"properties\":\"not-an-object\"}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ReplaceCapturesCollidingAffordanceNamesWithUniqueSuffix()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"properties\":{" +
                "\"a\":{\"uav:browseName\":\"1:SameName\",\"vendor:x\":1}," +
                "\"b\":{\"uav:browseName\":\"2:SameName\",\"vendor:y\":2}}}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            string xml = nodeSet.Extensions![0].OuterXml;
            Assert.That(xml, Does.Contain("SameName"));
            Assert.That(xml, Does.Contain("SameName_2"));
        }

        [Test]
        public void ReplaceCapturesNonArrayLinksAsWholeEntry()
        {
            byte[] docJson = WotTestData.Utf8(/*lang=json,strict*/ "{\"title\":\"T\",\"links\":\"string-not-array\"}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ReplaceCapturesMappedLinkExtrasForNsGeneratedNamespacePrefix()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"links\":" +
                "[{\"rel\":\"ns123:ref\",\"href\":\"urn:x\",\"custom-field\":\"custom-value\"}]}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            string xml = nodeSet.Extensions![0].OuterXml;
            Assert.That(xml, Does.Contain("ns123:ref"));
        }

        [Test]
        public void ReplaceAndApplyRoundTripsLinkExtrasForMappedLink()
        {
            byte[] docJson = WotTestData.Utf8(
                "{\"title\":\"T\",\"links\":" +
                "[{\"rel\":\"ua:NonHierarchicalReferences\",\"href\":\"urn:x\",\"vendor:score\":42}]}");
            using var document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);

            byte[] baseJson = WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     "{\"title\":\"T\",\"links\":[{\"rel\":\"ua:NonHierarchicalReferences\",\"href\":\"urn:x\"}]}");
            var applyDiagnostics = new List<WotDiagnostic>();
            byte[] result = WotJsonResidue.Apply(
                baseJson, nodeSet, new WotNodeSetConverterOptions(), applyDiagnostics);

            Assert.That(applyDiagnostics, Is.Empty);
            string resultStr = Encoding.UTF8.GetString(result);
            Assert.That(resultStr, Does.Contain("vendor:score"));
        }

        private static SysXmlElement CreateResidueExtension(
            string version,
            params SysXmlElement[] members)
        {
            var doc = new SysXmlDocument { XmlResolver = null };
            SysXmlElement root = doc.CreateElement("uav", "WoTJsonResidue", s_ns);
            root.SetAttribute("Version", version);
            foreach (SysXmlElement m in members)
            {
                root.AppendChild(doc.ImportNode(m, true));
            }

            return root;
        }

        private static SysXmlElement CreateResidueMember(
            string pointer,
            string json,
            string sha256Override = null)
        {
            var doc = new SysXmlDocument { XmlResolver = null };
            SysXmlElement member = doc.CreateElement("uav", "Member", s_ns);
            member.SetAttribute("Pointer", pointer);
            member.SetAttribute("Encoding", "base64");
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            member.SetAttribute("Sha256", sha256Override ?? ComputeSha256Hex(bytes));
            member.InnerText = Convert.ToBase64String(bytes);
            return member;
        }

        private static SysXmlElement CreateLinkResidueMember(
            string rel,
            string href,
            string extrasJson,
            string refId = null,
            string refName = null)
        {
            var doc = new SysXmlDocument { XmlResolver = null };
            SysXmlElement member = doc.CreateElement("uav", "Member", s_ns);
            member.SetAttribute("Pointer", "/links/-");
            member.SetAttribute("Encoding", "base64");
            member.SetAttribute("LinkRel", rel);
            if (href != null)
            {
                member.SetAttribute("LinkHref", href);
            }

            if (refId != null)
            {
                member.SetAttribute("LinkRefId", refId);
            }

            if (refName != null)
            {
                member.SetAttribute("LinkRefName", refName);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(extrasJson);
            member.SetAttribute("Sha256", ComputeSha256Hex(bytes));
            member.InnerText = Convert.ToBase64String(bytes);
            return member;
        }

        private static string ComputeSha256Hex(byte[] data)
        {
#if NETFRAMEWORK
            // SHA256.HashData is not available on .NET Framework.
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
#else
            byte[] hash = SHA256.HashData(data);
#endif
            return string.Concat(
                Array.ConvertAll(hash, b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
