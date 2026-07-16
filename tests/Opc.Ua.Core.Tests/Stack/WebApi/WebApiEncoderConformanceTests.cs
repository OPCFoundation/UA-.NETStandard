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
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.WebApi
{
    /// <summary>
    /// Conformance tests that pin the in-stack <see cref="JsonEncoder"/>
    /// output shape against the OPC UA OpenAPI Mapping spec
    /// (<c>opc.ua.openapi.allservices.json</c>, Part 6 §G.3) so the
    /// HTTPS REST binding's wire format is bit-compatible with what
    /// generated OpenAPI clients expect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests are deliberately tight: they exercise the
    /// well-known built-in encoders (<c>StatusCode</c>,
    /// <c>LocalizedText</c>, <c>NodeId</c>, <c>QualifiedName</c>,
    /// <c>ExtensionObject</c>, <c>Variant</c>, <c>DataValue</c>,
    /// <c>DiagnosticInfo</c>) and assert that property names match
    /// the spec's component schemas exactly. A failure here means the
    /// REST binding will produce JSON that drifts from spec.
    /// </para>
    /// <para>
    /// Failures should be addressed by adjusting encoder options or
    /// adding a targeted REST-binding encoder profile — never by
    /// silently reshaping output for one consumer.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WebApiEncoderConformance")]
    [Parallelizable]
    public class WebApiEncoderConformanceTests
    {
        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.Create(NUnitTelemetryContext.Create());
        }

        private static JsonDocument EncodeAndParse(
            JsonEncoderOptions options,
            Action<JsonEncoder> write)
        {
            ServiceMessageContext context = CreateContext();
            using var memory = new MemoryStream();
            using (var encoder = new JsonEncoder(memory, context, options))
            {
                write(encoder);
            }
            return JsonDocument.Parse(memory.ToArray());
        }

        [Test]
        public void StatusCodeGoodEmitsEmptyObject()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteStatusCode("Sc", StatusCodes.Good));

            // Compact omits StatusCode = Good entirely (default-value suppression).
            Assert.That(doc.RootElement.TryGetProperty("Sc", out _), Is.False);
        }

        [Test]
        public void StatusCodeGoodVerboseEmitsEmptyObjectPerSpec()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteStatusCode("Sc", StatusCodes.Good));

            // Verbose keeps the property; Good is represented as `{}` per Part 6 §5.1.2.
            Assert.That(doc.RootElement.TryGetProperty("Sc", out JsonElement sc), Is.True);
            Assert.That(sc.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(sc.TryGetProperty("Code", out _), Is.False);
        }

        [Test]
        public void StatusCodeNonGoodCompactEmitsCodeOnly()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteStatusCode("Sc", StatusCodes.BadInternalError));

            Assert.That(doc.RootElement.TryGetProperty("Sc", out JsonElement sc), Is.True);
            Assert.That(sc.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(sc.TryGetProperty("Code", out JsonElement code), Is.True,
                "spec component schema StatusCode requires 'Code'");
            Assert.That(code.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(sc.TryGetProperty("Symbol", out _), Is.False,
                "Compact must omit Symbol per JsonEncoderOptions.Compact.OmitStatusCodeSymbol");
        }

        [Test]
        public void StatusCodeNonGoodVerboseEmitsCodeAndSymbol()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteStatusCode("Sc", StatusCodes.BadInternalError));

            JsonElement sc = doc.RootElement.GetProperty("Sc");
            Assert.That(sc.TryGetProperty("Code", out _), Is.True);
            Assert.That(sc.TryGetProperty("Symbol", out JsonElement symbol), Is.True,
                "Verbose must include Symbol per spec component schema");
            Assert.That(symbol.ValueKind, Is.EqualTo(JsonValueKind.String));
        }

        [Test]
        public void LocalizedTextEmitsLocaleAndText()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteLocalizedText("Lt", new LocalizedText("en-US", "Hello")));

            JsonElement lt = doc.RootElement.GetProperty("Lt");
            Assert.That(lt.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(lt.GetProperty("Locale").GetString(), Is.EqualTo("en-US"));
            Assert.That(lt.GetProperty("Text").GetString(), Is.EqualTo("Hello"));
        }

        [Test]
        public void NodeIdEmitsAsStringNotObject()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteNodeId("N", new NodeId(42u, 2)));

            JsonElement n = doc.RootElement.GetProperty("N");
            Assert.That(n.ValueKind, Is.EqualTo(JsonValueKind.String),
                "spec component schema declares NodeId as string with format 'UaNodeId'");
        }

        [Test]
        public void QualifiedNameEmitsAsStringNotObject()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteQualifiedName("Q", new QualifiedName("MyName", 2)));

            JsonElement q = doc.RootElement.GetProperty("Q");
            Assert.That(q.ValueKind, Is.EqualTo(JsonValueKind.String),
                "spec component schema declares QualifiedName as string with format 'UaQualifiedName'");
        }

        [Test]
        public void ExtensionObjectEmitsUaTypeIdAndUaBody()
        {
            var extObj = new ExtensionObject(new NodeId(42u, 0), new ByteString(new byte[] { 1, 2, 3, 4 }));

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteExtensionObject("Ext", extObj));

            JsonElement ext = doc.RootElement.GetProperty("Ext");
            Assert.That(ext.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(ext.TryGetProperty("UaTypeId", out _), Is.True,
                "spec ExtensionObject schema requires 'UaTypeId'");
            Assert.That(ext.TryGetProperty("UaBody", out _), Is.True,
                "spec ExtensionObject schema requires 'UaBody'");
        }

        [Test]
        public void VariantScalarEmitsUaTypeAndValueButNoDimensions()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteVariant("V", new Variant(42)));

            JsonElement v = doc.RootElement.GetProperty("V");
            Assert.That(v.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(v.TryGetProperty("UaType", out JsonElement uaType), Is.True,
                "spec Variant schema requires 'UaType' (BuiltInType byte)");
            Assert.That(uaType.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(v.TryGetProperty("Value", out _), Is.True,
                "spec Variant schema requires 'Value'");
            Assert.That(v.TryGetProperty("Dimensions", out _), Is.False,
                "scalar Variant must not emit 'Dimensions'");
        }

        [Test]
        public void VariantMatrixEmitsDimensions()
        {
            MatrixOf<int> matrix = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteVariant("V", new Variant(matrix)));

            JsonElement v = doc.RootElement.GetProperty("V");
            Assert.That(v.TryGetProperty("UaType", out _), Is.True);
            Assert.That(v.TryGetProperty("Value", out _), Is.True);
            Assert.That(v.TryGetProperty("Dimensions", out JsonElement d), Is.True,
                "matrix Variant must emit 'Dimensions' per spec");
            Assert.That(d.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(d.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void DataValueInlinesVariantUaTypeAndValue()
        {
            DateTimeUtc src = DateTimeUtc.Now;
            var dv = new DataValue(
                new Variant(42),
                StatusCodes.GoodEntryInserted,
                src);

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteDataValue("Dv", dv));

            JsonElement v = doc.RootElement.GetProperty("Dv");
            Assert.That(v.ValueKind, Is.EqualTo(JsonValueKind.Object));

            // Per spec, DataValue inlines the Variant fields:
            //   { UaType, Value, Dimensions?, StatusCode?, SourceTimestamp?, ... }
            Assert.That(v.TryGetProperty("UaType", out _), Is.True,
                "DataValue must inline Variant 'UaType' (spec component schema)");
            Assert.That(v.TryGetProperty("Value", out _), Is.True,
                "DataValue must inline Variant 'Value'");
            Assert.That(v.TryGetProperty("StatusCode", out _), Is.True);
            Assert.That(v.TryGetProperty("SourceTimestamp", out JsonElement srcTs), Is.True);
            Assert.That(srcTs.ValueKind, Is.EqualTo(JsonValueKind.String),
                "spec declares SourceTimestamp as string format='date-time'");
        }

        [Test]
        public void DataValueGoodStatusOmittedInCompact()
        {
            var dv = new DataValue(new Variant(42), StatusCodes.Good);

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteDataValue("Dv", dv));

            JsonElement v = doc.RootElement.GetProperty("Dv");
            Assert.That(v.TryGetProperty("StatusCode", out _), Is.False,
                "Compact must omit StatusCode = Good (default-value suppression)");
        }

        [Test]
        public void DiagnosticInfoEmitsSpecPropertyNames()
        {
            var di = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4,
                AdditionalInfo = "extra",
                InnerStatusCode = StatusCodes.BadInternalError
            };

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Verbose,
                e => e.WriteDiagnosticInfo("Di", di));

            JsonElement d = doc.RootElement.GetProperty("Di");
            Assert.That(d.ValueKind, Is.EqualTo(JsonValueKind.Object));

            // Property names per spec DiagnosticInfo component schema.
            Assert.That(d.GetProperty("SymbolicId").GetInt32(), Is.EqualTo(1));
            Assert.That(d.GetProperty("NamespaceUri").GetInt32(), Is.EqualTo(2));
            Assert.That(d.GetProperty("Locale").GetInt32(), Is.EqualTo(3));
            Assert.That(d.GetProperty("LocalizedText").GetInt32(), Is.EqualTo(4));
            Assert.That(d.GetProperty("AdditionalInfo").GetString(), Is.EqualTo("extra"));
            Assert.That(d.TryGetProperty("InnerStatusCode", out _), Is.True);
        }

        [Test]
        public void DiagnosticInfoCompactOmitsUnsetIntegerSlots()
        {
            var di = new DiagnosticInfo
            {
                SymbolicId = -1,
                NamespaceUri = -1,
                Locale = -1,
                LocalizedText = -1,
                AdditionalInfo = "info"
            };

            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteDiagnosticInfo("Di", di));

            JsonElement d = doc.RootElement.GetProperty("Di");
            Assert.That(d.TryGetProperty("SymbolicId", out _), Is.False);
            Assert.That(d.TryGetProperty("NamespaceUri", out _), Is.False);
            Assert.That(d.TryGetProperty("Locale", out _), Is.False);
            Assert.That(d.TryGetProperty("LocalizedText", out _), Is.False);
            Assert.That(d.GetProperty("AdditionalInfo").GetString(), Is.EqualTo("info"));
        }

        [Test]
        public void NodeIdNamespaceIndexZeroOmitsNsPrefix()
        {
            using JsonDocument doc = EncodeAndParse(
                JsonEncoderOptions.Compact,
                e => e.WriteNodeId("N", new NodeId(42u, 0)));

            string text = doc.RootElement.GetProperty("N").GetString()!;
            Assert.That(text, Is.EqualTo("i=42"),
                "namespace-zero NodeIds must use the bare 'i=…' form per Part 6 §5.4.2.10");
        }
    }
}
