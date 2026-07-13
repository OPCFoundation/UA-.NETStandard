/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Additional coverage unit tests for the <see cref="JsonDecoder"/> class
    /// targeting infrastructure, mapping tables, message decoding and matrix
    /// variant paths not exercised by <see cref="JsonDecoderTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonDecoderCoverageTests
    {
        [Test]
        public void ConstructorWithStreamParsesDocument()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{ ""Value"": 42 }"));
            using var decoder = new JsonDecoder(stream, NewContext());
            Assert.That(decoder.ReadInt32(JsonProperties.Value), Is.EqualTo(42));
        }

        [Test]
        public void ConstructorWithNullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonDecoder("{}", null));
        }

        [Test]
        public void ConstructorWithInvalidJsonThrows()
        {
            ServiceMessageContext context = NewContext();
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new JsonDecoder(@"{ ""Value"": ", context));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void CloseTwiceThrowsObjectDisposedException()
        {
            var decoder = new JsonDecoder("{}", NewContext());
            decoder.Close();
            Assert.Throws<ObjectDisposedException>(decoder.Close);
        }

        [Test]
        public void SetMappingTablesWithUpdateAppendsUrisToContext()
        {
            ServiceMessageContext context = NewContext();
            using var decoder = new JsonDecoder("{}", context, new JsonDecoderOptions
            {
                UpdateNamespaceTable = true
            });

            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://test.org/ns/");
            var serverUris = new StringTable();
            serverUris.Append("http://test.org/srv/");

            decoder.SetMappingTables(namespaceUris, serverUris);

            Assert.That(context.NamespaceUris.GetIndex("http://test.org/ns/"), Is.EqualTo(1));
            Assert.That(context.ServerUris.GetIndex("http://test.org/srv/"), Is.Zero);
        }

        [Test]
        public void SetMappingTablesWithoutUpdateDoesNotAppendUnknownUri()
        {
            ServiceMessageContext context = NewContext();
            using var decoder = new JsonDecoder("{}", context);

            int before = context.NamespaceUris.Count;
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://unknown.org/ns/");

            decoder.SetMappingTables(namespaceUris, null);

            Assert.That(context.NamespaceUris.Count, Is.EqualTo(before));
            Assert.That(context.NamespaceUris.GetIndex("http://unknown.org/ns/"), Is.EqualTo(-1));
        }

        [Test]
        public void SetMappingTablesWithNullTablesLeavesContextUnchanged()
        {
            ServiceMessageContext context = NewContext();
            using var decoder = new JsonDecoder("{}", context);

            int namespaces = context.NamespaceUris.Count;
            int servers = context.ServerUris.Count;

            decoder.SetMappingTables(null, null);

            Assert.That(context.NamespaceUris.Count, Is.EqualTo(namespaces));
            Assert.That(context.ServerUris.Count, Is.EqualTo(servers));
        }

        [Test]
        public void DecodeMessageThrowsWhenMaxMessageSizeExceeded()
        {
            ServiceMessageContext context = NewContext();
            context.MaxMessageSize = 4;
            byte[] buffer = new byte[64];

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => JsonDecoder.DecodeMessage<Argument>(buffer, context));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void DecodeMessageRoundTripsEncodeable()
        {
            ServiceMessageContext context = NewContext();
            var argument = new Argument
            {
                Name = "hello",
                ValueRank = -1,
                DataType = new NodeId(11u)
            };

            byte[] buffer = new byte[8192];
            ArraySegment<byte> encoded = JsonEncoder.EncodeMessage(argument, buffer, context);
            var sequence = new ReadOnlySequence<byte>(encoded.Array, encoded.Offset, encoded.Count);

            Argument decoded = JsonDecoder.DecodeMessage<Argument>(sequence, context);

            Assert.That(decoded.Name, Is.EqualTo("hello"));
            Assert.That(decoded.DataType, Is.EqualTo(new NodeId(11u)));
        }

        [Test]
        public void ReadEnumeratedReturnsEnumValueFromVerboseString()
        {
            using JsonDecoder reader = NewDecoder(Body(@"""Running_2"""));
            EnumValue result = reader.ReadEnumerated(JsonProperties.Value);
            Assert.That(result.Value, Is.EqualTo(2));
            Assert.That(result.Symbol, Is.EqualTo("Running"));
        }

        [Test]
        public void ReadEnumeratedReturnsEnumValueFromNumber()
        {
            using JsonDecoder reader = NewDecoder(Body("7"));
            EnumValue result = reader.ReadEnumerated(JsonProperties.Value);
            Assert.That(result.Value, Is.EqualTo(7));
            Assert.That(result.Symbol, Is.Null);
        }

        [Test]
        public void ReadEnumeratedArrayReturnsEnumValues()
        {
            using JsonDecoder reader = NewDecoder(Body(@"[ ""Red_1"", ""Blue_2"" ]"));
            ArrayOf<EnumValue> result = reader.ReadEnumeratedArray(JsonProperties.Value);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[0].Symbol, Is.EqualTo("Red"));
            Assert.That(result[1].Value, Is.EqualTo(2));
        }

        [Test]
        public void ReadEncodeableReturnsArgument()
        {
            using JsonDecoder reader = NewDecoder(Body(/*lang=json,strict*/ """{ "Name": "temp", "ValueRank": -1 }"""));
            Argument result = reader.ReadEncodeable<Argument>(JsonProperties.Value);
            Assert.That(result.Name, Is.EqualTo("temp"));
            Assert.That(result.ValueRank, Is.EqualTo(-1));
        }

        [Test]
        public void HasFieldDistinguishesPresentAndAbsentFields()
        {
            using JsonDecoder reader = NewDecoder(/*lang=json,strict*/ """{ "A": 1 }""");
            reader.PushNamespace("urn:ignored");
            reader.PopNamespace();
            Assert.That(reader.HasField("A"), Is.True);
            Assert.That(reader.HasField("B"), Is.False);
        }

        [Test]
        public void ReadSwitchFieldResolvesFieldFromSwitchFieldIndex()
        {
            using JsonDecoder reader = NewDecoder(/*lang=json,strict*/ """{ "SwitchField": 2 }""");
            var switches = new List<string> { "A", "B", "C" };
            uint value = reader.ReadSwitchField(switches, out string fieldName);
            Assert.That(value, Is.EqualTo(2));
            Assert.That(fieldName, Is.EqualTo("B"));
        }

        [Test]
        public void ReadSwitchFieldResolvesIndexFromPresentField()
        {
            // The field-name fallback is only reached when SwitchField is present
            // but not numeric; otherwise an absent SwitchField is treated as index 0.
            using JsonDecoder reader = NewDecoder(/*lang=json,strict*/ """{ "SwitchField": "x", "B": 5 }""");
            var switches = new List<string> { "A", "B", "C" };
            uint value = reader.ReadSwitchField(switches, out string fieldName);
            Assert.That(value, Is.EqualTo(2));
            Assert.That(fieldName, Is.EqualTo("B"));
        }

        [Test]
        public void ReadSwitchFieldWithNullElementReturnsZero()
        {
            using JsonDecoder reader = NewDecoder("null");
            var switches = new List<string> { "A", "B", "C" };
            uint value = reader.ReadSwitchField(switches, out string fieldName);
            Assert.That(value, Is.Zero);
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void ReadEncodingMaskReadsExplicitMask()
        {
            using JsonDecoder reader = NewDecoder(/*lang=json,strict*/ """{ "EncodingMask": 6 }""");
            var masks = new List<string> { "A", "B", "C" };
            Assert.That(reader.ReadEncodingMask(masks), Is.EqualTo(6u));
        }

        [Test]
        public void ReadEncodingMaskComputesMaskFromPresentFields()
        {
            // The field-name fallback is only reached when EncodingMask is present
            // but not numeric; otherwise an absent EncodingMask yields mask 0.
            using JsonDecoder reader = NewDecoder(/*lang=json,strict*/ """{ "EncodingMask": "x", "A": 1, "C": 1 }""");
            var masks = new List<string> { "A", "B", "C" };
            Assert.That(reader.ReadEncodingMask(masks), Is.EqualTo(5u));
        }

        [Test]
        public void ReadEncodingMaskWithNullElementReturnsZero()
        {
            using JsonDecoder reader = NewDecoder("null");
            var masks = new List<string> { "A", "B", "C" };
            Assert.That(reader.ReadEncodingMask(masks), Is.Zero);
        }

        [Test]
        public void ReadVariantDecodesBooleanMatrix()
        {
            using JsonDecoder reader = NewDecoder(Body(
                /*lang=json,strict*/ """{ "UaType": 1, "Value": [ true, false, false, true ], "Dimensions": [ 2, 2 ] }"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);

            MatrixOf<bool> matrix = result.GetBooleanMatrix();
            int[] expectedDimensions = [2, 2];
            bool[] expectedValues = [true, false, false, true];
            Assert.That(matrix.Dimensions, Is.EqualTo(expectedDimensions));
            Assert.That(matrix.ToArrayOf(), Is.EqualTo(expectedValues.ToArrayOf()));
        }

        [Test]
        public void ReadVariantDecodesInt32Matrix()
        {
            using JsonDecoder reader = NewDecoder(Body(
                /*lang=json,strict*/ """{ "UaType": 6, "Value": [ 1, 2, 3, 4 ], "Dimensions": [ 2, 2 ] }"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);

            MatrixOf<int> matrix = result.GetInt32Matrix();
            int[] expectedDimensions = [2, 2];
            int[] expectedValues = [1, 2, 3, 4];
            Assert.That(matrix.Dimensions, Is.EqualTo(expectedDimensions));
            Assert.That(matrix.ToArrayOf(), Is.EqualTo(expectedValues.ToArrayOf()));
        }

        [Test]
        public void ReadVariantDecodesDoubleMatrix()
        {
            using JsonDecoder reader = NewDecoder(Body(
                /*lang=json,strict*/ """{ "UaType": 11, "Value": [ 1.5, 2.5, 3.5, 4.5 ], "Dimensions": [ 2, 2 ] }"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);

            MatrixOf<double> matrix = result.GetDoubleMatrix();
            int[] expectedDimensions = [2, 2];
            double[] expectedValues = [1.5, 2.5, 3.5, 4.5];
            Assert.That(matrix.Dimensions, Is.EqualTo(expectedDimensions));
            Assert.That(matrix.ToArrayOf(), Is.EqualTo(expectedValues.ToArrayOf()));
        }

        [Test]
        public void ReadVariantDecodesStringMatrix()
        {
            using JsonDecoder reader = NewDecoder(Body(
                /*lang=json,strict*/ """{ "UaType": 12, "Value": [ "a", "b", "c", "d" ], "Dimensions": [ 2, 2 ] }"""));
            Variant result = reader.ReadVariant(JsonProperties.Value);

            MatrixOf<string> matrix = result.GetStringMatrix();
            int[] expectedDimensions = [2, 2];
            string[] expectedValues = ["a", "b", "c", "d"];
            Assert.That(matrix.Dimensions, Is.EqualTo(expectedDimensions));
            Assert.That(matrix.ToArrayOf(), Is.EqualTo(expectedValues.ToArrayOf()));
        }

        private static string Body(string value)
        {
            return $$"""
            {
                "{{JsonProperties.Value}}": {{value}}
            }
            """;
        }

        private static ServiceMessageContext NewContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);
            messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(EncodeableFactory).Assembly)
                .Commit();
            return messageContext;
        }

        private static JsonDecoder NewDecoder(string json, bool strict = false)
        {
            return new JsonDecoder(json, NewContext(), new JsonDecoderOptions
            {
                ParseStrict = strict
            });
        }
    }
}
