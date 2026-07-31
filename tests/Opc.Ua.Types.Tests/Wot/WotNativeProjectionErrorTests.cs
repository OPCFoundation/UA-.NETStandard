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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNativeProjectionErrorTests
    {
        [Test]
        public void ReadRejectsProjectionWithWrongAtType()
        {
            using JsonDocument doc = Parse(
                "{\"@type\":\"wrong:type\",\"profileVersion\":\"1.0\",\"nodes\":[]}");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadRejectsProjectionThatIsNotAnObject()
        {
            using JsonDocument doc = Parse("[\"notAnObject\"]");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadRejectsProjectionWithWrongVersion()
        {
            using JsonDocument doc = Parse(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"99.0\",\"nodes\":[]}");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadRejectsMissingNodesArray()
        {
            using JsonDocument doc = Parse(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"}");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadRejectsNodesPropertyThatIsNotAnArray()
        {
            using JsonDocument doc = Parse(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\",\"nodes\":{}}");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadEnforcesNodeCountLimit()
        {
            string twoNodes =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[" +
                "{\"nodeClass\":\"ObjectType\",\"nodeId\":\"ns=1;i=1001\",\"browseName\":\"1:A\"}," +
                "{\"nodeClass\":\"ObjectType\",\"nodeId\":\"ns=1;i=1002\",\"browseName\":\"1:B\"}" +
                "]}";

            using JsonDocument doc = Parse(twoNodes);
            var options = new WotNodeSetConverterOptions { MaxNodeCount = 1 };
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(doc.RootElement, options, diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NodeCountExceeded),
                Is.True);
        }

        [Test]
        public void ReadRejectsUnknownNodeClass()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{\"nodeClass\":\"UnknownClass\",\"nodeId\":\"ns=1;i=1001\"}]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadReportsInvalidAliasEntryMissingAlias()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"aliases\":[{\"value\":\"i=47\"}]," +
                "\"nodes\":[]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True,
                "An alias entry missing the 'alias' field should produce a diagnostic.");
        }

        [Test]
        public void ReadReportsInvalidAliasEntryMissingValue()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"aliases\":[{\"alias\":\"MyAlias\"}]," +
                "\"nodes\":[]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True,
                "An alias entry missing the 'value' field should produce a diagnostic.");
        }

        [Test]
        public void ReadRoundTripsAllNodeClasses()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            byte[] projectionJson = WotNativeProjection.Write(
                source,
                new WotNodeSetConverterOptions(),
                new List<WotDiagnostic>());

            using JsonDocument doc = JsonDocument.Parse(projectionJson);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet restored = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(restored, Is.Not.Null);
            Assert.That(diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error), Is.False);
            Assert.That(restored!.Items, Has.Length.EqualTo(source.Items!.Length));
        }

        [Test]
        public void ReadHandlesMalformedXmlInNodeExtensions()
        {
            UANodeSet source = WotTestData.CreateReconstructableNodeSet();
            byte[] projectionJson = WotNativeProjection.Write(
                source,
                new WotNodeSetConverterOptions(),
                new List<WotDiagnostic>());

            JsonNode projection = JsonNode.Parse(Encoding.UTF8.GetString(projectionJson))!;
            JsonArray nodes = projection["nodes"]!.AsArray();
            int objectTypeIndex = -1;
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (nodes[ii] is JsonObject node &&
                    string.Equals(
                        node["nodeClass"]!.GetValue<string>(),
                        "ObjectType",
                        StringComparison.Ordinal))
                {
                    objectTypeIndex = ii;
                    node["extensions"] = new JsonArray(JsonValue.Create("<not xml at all>"));
                    break;
                }
            }
            Assert.That(objectTypeIndex, Is.GreaterThanOrEqualTo(0));
            string expectedPointer = "/uav:nodes/nodes/" +
                objectTypeIndex.ToString(CultureInfo.InvariantCulture) +
                "/extensions/0";
            string patchedText = projection.ToJsonString();

            using JsonDocument doc = JsonDocument.Parse(patchedText);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement,
                new WotNodeSetConverterOptions(),
                diagnostics);

            Assert.That(result, Is.Null);
            WotDiagnostic diagnostic = diagnostics.SingleOrDefault(d =>
                d.Severity == WotDiagnosticSeverity.Error &&
                d.Code == WotDiagnosticCode.NativeProjectionInvalid &&
                d.Location?.JsonPointer == expectedPointer);
            Assert.That(
                diagnostic,
                Is.Not.Null,
                "Malformed node extension XML should produce one error diagnostic at the extension value.");
            Assert.That(diagnostic!.Message, Does.Contain("native XML fragment is malformed"));
        }

        [Test]
        public void ReadAcceptsValidAliasArray()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"aliases\":[{\"alias\":\"HasComponent\",\"value\":\"i=47\"}," +
                "{\"alias\":\"Organizes\",\"value\":\"i=35\"}]," +
                "\"nodes\":[]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Not.Null);
            Assert.That(diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error), Is.False);
            Assert.That(result!.Aliases, Is.Not.Null);
            Assert.That(result.Aliases, Has.Length.EqualTo(2));
        }

        [Test]
        public void ReadRejectsProjectionWithNullAtType()
        {
            using JsonDocument doc = Parse(
                "{\"profileVersion\":\"1.0\",\"nodes\":[]}");
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Null);
            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True);
        }

        [Test]
        public void ReadAcceptsEmptyNodesArray()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\",\"nodes\":[]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Not.Null);
            Assert.That(diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error), Is.False);
            Assert.That(result!.Items, Is.Empty);
        }

        [Test]
        public void ReadAcceptsValidDataTypeNode()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{" +
                "\"nodeClass\":\"DataType\"," +
                "\"nodeId\":\"ns=1;i=3001\"," +
                "\"browseName\":\"1:MyMode\"," +
                "\"definition\":{" +
                "\"name\":\"1:MyMode\"," +
                "\"fields\":[{\"name\":\"Stopped\",\"value\":0}," +
                "{\"name\":\"Running\",\"value\":1}]}" +
                "}]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            UANodeSet result = WotNativeProjection.Read(
                doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(result, Is.Not.Null);
            UADataType dt = result!.Items?.OfType<UADataType>().FirstOrDefault();
            Assert.That(dt, Is.Not.Null);
            Assert.That(dt!.Definition, Is.Not.Null);
            Assert.That(dt.Definition!.Field, Has.Length.EqualTo(2));
        }

        [Test]
        public void ReadReportsDataTypeDefinitionFieldMissingName()
        {
            string json =
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{" +
                "\"nodeClass\":\"DataType\"," +
                "\"nodeId\":\"ns=1;i=3001\"," +
                "\"browseName\":\"1:Mode\"," +
                "\"definition\":{" +
                "\"name\":\"1:Mode\"," +
                "\"fields\":[{\"value\":0}]}" +
                "}]}";

            using JsonDocument doc = Parse(json);
            var diagnostics = new List<WotDiagnostic>();

            WotNativeProjection.Read(doc.RootElement, new WotNodeSetConverterOptions(), diagnostics);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionInvalid),
                Is.True,
                "A DataType definition field missing 'name' should produce a diagnostic.");
        }

        private static JsonDocument Parse(string json)
        {
            return JsonDocument.Parse(json);
        }
    }
}
