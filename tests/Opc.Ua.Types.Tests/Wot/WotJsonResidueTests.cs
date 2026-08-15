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

        [Test]
        public void ApplyWithNullExtensionsReturnsOriginalBytes()
        {
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
            var nodeSet = new UANodeSet { Extensions = null };
            var diagnostics = new List<WotDiagnostic>();

            byte[] result = WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.EqualTo(json));
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyWithNoMatchingExtensionReturnsOriginalBytes()
        {
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/vendor:extra", "{\"key\":\"value\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"links\":[{\"rel\":\"existing\",\"href\":\"urn:x\"}]}");
            string linkJson = "{\"rel\":\"extra\",\"href\":\"urn:y\"}";
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"vendor:x\":\"original\"}");
            SysXmlElement member = CreateResidueMember("/vendor:x", "\"conflicting\"");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
            SysXmlElement member = CreateResidueMember("/title/nested", "42");
            SysXmlElement ext = CreateResidueExtension("1.0", member);
            var nodeSet = new UANodeSet { Extensions = [ext] };
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Apply(json, nodeSet, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ResidueInvalid ||
                    d.Code == WotDiagnosticCode.ResidueConflict),
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
            byte[] badJson = WotTestData.Utf8("{broken json");
            string sha256 = ComputeSha256Hex(badJson);
            SysXmlElement ext = CreateResidueExtension("1.0");
            var doc = ext.OwnerDocument!;
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
            byte[] trivialJson = WotTestData.Utf8("{\"title\":\"T\"}");
            using WotDocument document = WotDocument.Parse(trivialJson);
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

            byte[] emptyDocument = WotTestData.Utf8("{\"title\":\"T\"}");
            using WotDocument document = WotDocument.Parse(emptyDocument);
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
            using WotDocument document = WotDocument.Parse(docJson);
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
                "{\"title\":\"T\",\"vendor:meta\":{\"count\":42,\"tag\":\"test\"}}");
            using WotDocument document = WotDocument.Parse(docJson);

            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();
            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostics, Is.Empty);

            byte[] baseJson = WotTestData.Utf8("{\"title\":\"T\"}");
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
            using WotDocument document = WotDocument.Parse(docJson);
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
            using WotDocument document = WotDocument.Parse(docJson);
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
            using WotDocument document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ApplyCreatesIntermediateObjectForDeepPointer()
        {
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"items\":[\"a\",\"b\",\"c\"]}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"matrix\":[[1,2],[3,4]]}");
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
            byte[] json = WotTestData.Utf8("{\"vendor:x\":\"original\"}");
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
            byte[] json = WotTestData.Utf8("{\"items\":[\"existing-value\"]}");
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
            byte[] json = WotTestData.Utf8("{\"items\":[\"a\",\"b\",\"c\"]}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
            SysXmlElement member = CreateLinkResidueMember(
                "custom:rel",
                "urn:new",
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
                "{\"title\":\"T\",\"links\":[{\"rel\":\"my-rel\",\"href\":\"urn:x\"}]}");
            SysXmlElement member = CreateLinkResidueMember(
                "my-rel",
                "urn:x",
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\",\"links\":\"not-an-array\"}");
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
            byte[] json = WotTestData.Utf8("{\"title\":\"T\"}");
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
            byte[] docJson = WotTestData.Utf8("{\"title\":\"T\",\"@context\":\"urn:custom-ctx\"}");
            using WotDocument document = WotDocument.Parse(docJson);
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
            byte[] docJson = WotTestData.Utf8("{\"title\":\"T\",\"properties\":\"not-an-object\"}");
            using WotDocument document = WotDocument.Parse(docJson);
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
            using WotDocument document = WotDocument.Parse(docJson);
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
            byte[] docJson = WotTestData.Utf8("{\"title\":\"T\",\"links\":\"string-not-array\"}");
            using WotDocument document = WotDocument.Parse(docJson);
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
            using WotDocument document = WotDocument.Parse(docJson);
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
            using WotDocument document = WotDocument.Parse(docJson);
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodeSet, document, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(nodeSet.Extensions, Is.Not.Null.And.Not.Empty);

            byte[] baseJson = WotTestData.Utf8(
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
