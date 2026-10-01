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
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Tests that nested arrays and nested XML read from untrusted input
    /// stay within the memory and stack the input can justify.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DecoderNestingLimitTests
    {
        private const string kTypesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// A chain of Variant arrays whose length prefixes promise 65535
        /// elements each, nested in the first element of the parent, used to
        /// preallocate about 1 MB per 5 input bytes and level.
        /// </summary>
        [Test]
        public void NestedVariantArrayLengthPrefixesDoNotMultiplyAllocations()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            byte[] message = CreateNestedVariantArrayChain(199);

#if NET
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            ServiceResultException sre = Assert.Throws<ServiceResultException>(() =>
            {
                using var decoder = new BinaryDecoder(message, context);
                decoder.ReadVariant(null);
            });
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
#if NET
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // 199 levels of 16 KB growing arrays, not 199 x 1 MB.
            Assert.That(allocated, Is.LessThan(16 * 1024 * 1024));
#endif
        }

        /// <summary>
        /// An array of null Variants takes one byte per element on the wire
        /// and 16 bytes in memory, more than the preallocation budget, so it
        /// is grown while it is read and still decodes completely.
        /// </summary>
        [Test]
        public void VariantArrayLargerThanPreallocationBudgetDecodes()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            const int length = 65535;
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((byte)(0x80 | (byte)BuiltInType.Variant));
                writer.Write(length);
                writer.Write(new byte[length]);
            }

            using var decoder = new BinaryDecoder(stream.ToArray(), context);
            Variant variant = decoder.ReadVariant(null);

            var values = (Variant[])variant.Value;
            Assert.That(values, Has.Length.EqualTo(length));
            Assert.That(values[length - 1].Value, Is.Null);

            // The same array as a VariantArray field, without the Variant encoding byte.
            byte[] field = stream.ToArray();
            using var decoder2 = new BinaryDecoder(field, 1, field.Length - 1, context);
            Assert.That(decoder2.ReadVariantArray(null), Has.Count.EqualTo(length));
        }

        /// <summary>
        /// An encodeable array whose elements take no bytes on the wire is
        /// grown while it is read and still decodes completely.
        /// </summary>
        [Test]
        public void EncodeableArrayLargerThanPreallocationBudgetDecodes()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            const int length = 20000;
            byte[] message = BitConverter.GetBytes(length);

            using var decoder = new BinaryDecoder(message, context);
            Array values = decoder.ReadEncodeableArray(null, typeof(EmptyEncodeable));

            Assert.That(values, Is.TypeOf<EmptyEncodeable[]>());
            Assert.That(values, Has.Length.EqualTo(length));
            Assert.That(values.GetValue(length - 1), Is.Not.Null);
        }

        /// <summary>
        /// A deeply nested XmlElement in a binary message is rejected before
        /// it is loaded; recursive DOM operations on it would overflow the stack.
        /// </summary>
        [Test]
        [TestCase(50, false)]
        [TestCase(300, true)]
        [TestCase(30000, true)]
        public void BinaryDecoderRejectsDeeplyNestedXmlElement(int depth, bool rejected)
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            byte[] xml = Encoding.UTF8.GetBytes(CreateNestedXml(depth));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(xml.Length);
                writer.Write(xml);
            }

            using var decoder = new BinaryDecoder(stream.ToArray(), context);
            if (rejected)
            {
                ServiceResultException sre = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadXmlElement(null));
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                Assert.That(decoder.ReadXmlElement(null).LocalName, Is.EqualTo("r"));
            }
        }

        /// <summary>
        /// A deeply nested XmlElement in an XML document is rejected before
        /// the InnerXml setter, which recurses once per level, parses it.
        /// </summary>
        [Test]
        [TestCase(50, false)]
        [TestCase(300, true)]
        [TestCase(30000, true)]
        public void XmlDecoderRejectsDeeplyNestedXmlElement(int depth, bool rejected)
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string document =
                $"<Test xmlns=\"{kTypesNamespace}\"><Value>{CreateNestedXml(depth)}</Value></Test>";

            using var reader = XmlReader.Create(new StringReader(document));
            using var decoder = new XmlDecoder(null, reader, context);
            if (rejected)
            {
                ServiceResultException sre = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadXmlElement("Value"));
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                Assert.That(decoder.ReadXmlElement("Value").LocalName, Is.EqualTo("r"));
            }
        }

        private static byte[] CreateNestedVariantArrayChain(int levels)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                for (int ii = 0; ii < levels; ii++)
                {
                    writer.Write((byte)(0x80 | (byte)BuiltInType.Variant));
                    writer.Write(65535);
                }
            }
            return stream.ToArray();
        }

        private static string CreateNestedXml(int depth)
        {
            var builder = new StringBuilder("<r>");
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("<a>");
            }
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("</a>");
            }
            return builder.Append("</r>").ToString();
        }

        /// <summary>
        /// An encodeable without fields.
        /// </summary>
        public sealed class EmptyEncodeable : IEncodeable
        {
            /// <inheritdoc/>
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;

            /// <inheritdoc/>
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;

            /// <inheritdoc/>
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            /// <inheritdoc/>
            public void Encode(IEncoder encoder)
            {
            }

            /// <inheritdoc/>
            public void Decode(IDecoder decoder)
            {
            }

            /// <inheritdoc/>
            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is EmptyEncodeable;
            }

            /// <inheritdoc/>
            public object Clone()
            {
                return new EmptyEncodeable();
            }
        }
    }
}
