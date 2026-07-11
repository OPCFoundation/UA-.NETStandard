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

using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Round-trip tests for the typed
    /// <see cref="IEncoder.WriteEncodeableMatrix{T}(string, MatrixOf{T})"/>
    /// and the parameterless
    /// <see cref="IDecoder.ReadEncodeableMatrix{T}(string)"/> overload added
    /// alongside the matrix-rank data type field source generation work.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncodeableMatrixRoundTripTests
    {
        private static MatrixOf<MatrixSample> CreateSample()
        {
            MatrixSample[] elements =
            [
                new MatrixSample { Value = 1 },
                new MatrixSample { Value = 2 },
                new MatrixSample { Value = 3 },
                new MatrixSample { Value = 4 },
                new MatrixSample { Value = 5 },
                new MatrixSample { Value = 6 }
            ];
            return new MatrixOf<MatrixSample>(elements, [2, 3]);
        }

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var ctx = ServiceMessageContext.CreateEmpty(telemetry);
            ctx.Factory.AddEncodeableType(typeof(MatrixSample));
            return ctx;
        }

        private static void AssertMatricesEqual(
            MatrixOf<MatrixSample> expected,
            MatrixOf<MatrixSample> actual)
        {
            Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.Dimensions, Is.EqualTo(expected.Dimensions));
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual.Span[i].Value, Is.EqualTo(expected.Span[i].Value));
            }
        }

        /// <summary>
        /// Verifies BinaryEncoder.WriteEncodeableMatrix &lt;-&gt;
        /// BinaryDecoder.ReadEncodeableMatrix&lt;T&gt;(name) round trip.
        /// </summary>
        [Test]
        public void BinaryEncoderReadEncodeableMatrixNoEncodingIdRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            MatrixOf<MatrixSample> input = CreateSample();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(ctx))
            {
                encoder.WriteEncodeableMatrix("Matrix", input);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, ctx);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");

            AssertMatricesEqual(input, output);
        }

        /// <summary>
        /// Verifies the binary read returns a null
        /// <see cref="MatrixOf{T}"/> when the encoded payload is the
        /// canonical "no value" marker (-1 dimension length).
        /// </summary>
        [Test]
        public void BinaryReadEncodeableMatrixNoEncodingIdReturnsDefaultForNull()
        {
            ServiceMessageContext ctx = CreateContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(ctx))
            {
                encoder.WriteEncodeableMatrix("Matrix", default(MatrixOf<MatrixSample>));
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, ctx);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");

            Assert.That(output.IsNull, Is.True);
        }

        /// <summary>
        /// Verifies XmlEncoder.WriteEncodeableMatrix &lt;-&gt;
        /// XmlDecoder.ReadEncodeableMatrix&lt;T&gt;(name) round trip.
        /// </summary>
        [Test]
        public void XmlEncoderReadEncodeableMatrixNoEncodingIdRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            MatrixOf<MatrixSample> input = CreateSample();

            string xml;
            using (var encoder = new XmlEncoder(ctx))
            {
                encoder.PushNamespace(Namespaces.OpcUaXsd);
                encoder.WriteEncodeableMatrix("Matrix", input);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");
            decoder.PopNamespace();

            AssertMatricesEqual(input, output);
        }

        /// <summary>
        /// Verifies XmlEncoder.WriteEncodeableMatrix &lt;-&gt;
        /// XmlDecoder.ReadEncodeableMatrix&lt;T&gt;(name) round trip
        /// (the streaming XML decoder, not the in-memory XmlParser).
        /// </summary>
        [Test]
        public void XmlDecoderReadEncodeableMatrixNoEncodingIdRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            MatrixOf<MatrixSample> input = CreateSample();

            string xml;
            using (var encoder = new XmlEncoder(ctx))
            {
                encoder.PushNamespace(Namespaces.OpcUaXsd);
                encoder.WriteEncodeableMatrix("Matrix", input);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var reader = XmlReader.Create(
                stream,
                CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");
            decoder.PopNamespace();

            AssertMatricesEqual(input, output);
        }

        /// <summary>
        /// Verifies JsonEncoder.WriteEncodeableMatrix &lt;-&gt;
        /// JsonDecoder.ReadEncodeableMatrix&lt;T&gt;(name) round trip.
        /// </summary>
        [Test]
        public void JsonDecoderReadEncodeableMatrixNoEncodingIdRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            MatrixOf<MatrixSample> input = CreateSample();

            string json;
            using (var encoder = new JsonEncoder(ctx))
            {
                encoder.WriteEncodeableMatrix("Matrix", input);
                json = encoder.CloseAndReturnText();
            }
            using var decoder = new JsonDecoder(json, ctx);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");

            AssertMatricesEqual(input, output);
        }

        /// <summary>
        /// Verifies the JSON read returns a default
        /// <see cref="MatrixOf{T}"/> when the field is missing from the
        /// encoded payload.
        /// </summary>
        [Test]
        public void JsonDecoderReadEncodeableMatrixNoEncodingIdMissingFieldReturnsDefault()
        {
            ServiceMessageContext ctx = CreateContext();

            using var decoder = new JsonDecoder("{}", ctx);
            MatrixOf<MatrixSample> output =
                decoder.ReadEncodeableMatrix<MatrixSample>("Matrix");

            Assert.That(output.IsNull, Is.True);
        }

        /// <summary>
        /// Concrete encodeable used to exercise the matrix overloads.
        /// </summary>
        [DataContract(Name = "MatrixSample", Namespace = Namespaces.OpcUaXsd)]
        public sealed class MatrixSample : IEncodeable
        {
            public int Value { get; set; }

            public ExpandedNodeId TypeId => new(77777, 0);
            public ExpandedNodeId BinaryEncodingId => new(77778, 0);
            public ExpandedNodeId XmlEncodingId => new(77779, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is MatrixSample other && other.Value == Value;
            }

            public object Clone()
            {
                return new MatrixSample { Value = Value };
            }
        }
    }
}
