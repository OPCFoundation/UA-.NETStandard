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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Buffers;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Zero length values (empty strings, byte strings and arrays) written by
    /// the binary encoder to a buffer writer. A GetSpan(0) asks an
    /// <see cref="IBufferWriter{T}"/> for any buffer, which the pooled writer
    /// used to answer with a fresh chunk of up to 64 KB per call that was only
    /// returned at dispose: an array of empty strings in a response or request
    /// stranded about 64 KB of pooled memory per element.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class BinaryEncoderZeroLengthWriteTests
    {
        private const int kCount = 1000;

        public static readonly object[] ZeroLengthValues =
        [
            new object[] { "empty string", new Variant(string.Empty) },
            new object[] { "empty strings", new Variant(Repeat(string.Empty)) },
            new object[] { "empty byte string", new Variant(ByteString.From(Array.Empty<byte>())) },
            new object[] { "empty byte strings", new Variant(Repeat(ByteString.From(Array.Empty<byte>()))) },
            new object[] { "empty localized texts", new Variant(Repeat(new LocalizedText(string.Empty, string.Empty))) },
            new object[] { "empty variant array", new Variant(Array.Empty<Variant>().ToArrayOf()) },
            new object[] { "empty variant arrays", new Variant(Repeat(new Variant(Array.Empty<int>().ToArrayOf()))) },
            new object[] { "data change of empty strings", new Variant(new ExtensionObject(CreateDataChange())) }
        ];

        private static ArrayOf<T> Repeat<T>(T value)
        {
            return Enumerable.Repeat(value, kCount).ToArray().ToArrayOf();
        }

        /// <summary>
        /// The encoder never asks the buffer writer for a buffer of size zero.
        /// The data change case encodes its body with a nested encoder that
        /// writes to a pooled writer of its own.
        /// </summary>
        [TestCaseSource(nameof(ZeroLengthValues))]
        public void ZeroLengthValuesDoNotRequestABufferOfAnySize(string name, Variant value)
        {
            var writer = new CountingBufferWriter();
            using (var encoder = new BinaryEncoder(writer, CreateContext()))
            {
                encoder.WriteVariant(null, value);
            }

            Assert.That(writer.ZeroSizeHints, Is.Zero, name);

            // same bytes as the stream encoder, which writes bodies in place.
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, CreateContext(), true))
            {
                encoder.WriteVariant(null, value);
            }
            Assert.That(writer.WrittenBytes, Is.EqualTo(stream.ToArray()), name);
        }

        /// <summary>
        /// Encoding an array of empty strings into a pooled writer, directly
        /// and as the body of a notification (the nested encoder path), keeps
        /// the written bytes in a single segment and allocates in proportion
        /// to the encoded size, not 64 KB per element.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EmptyStringsDoNotRentAChunkPerElement(bool asNotificationBody)
        {
            Variant value = asNotificationBody
                ? new Variant(new ExtensionObject(CreateDataChange()))
                : new Variant(Repeat(string.Empty));
            ServiceMessageContext context = CreateContext();

            // warm up the code path and the pool.
            Encode(value, context, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Encode(value, context, out int segments);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // chunks double from 256 bytes: a handful of segments, not one per element.
            Assert.That(segments, Is.LessThanOrEqualTo(8));
            Assert.That(allocated, Is.LessThan(256 * 1024),
                $"{allocated} bytes allocated to encode {kCount} empty strings.");
        }

        /// <summary>
        /// The message encoder of the HTTPS binding writes to an owned pooled
        /// writer: encoding a response with many empty strings allocates in
        /// proportion to the encoded size.
        /// </summary>
        [Test]
        public void EncodeMessageWithEmptyStringsAllocatesInProportionToTheMessage()
        {
            ServiceMessageContext context = CreateContext();
            var response = new ReadResponse
            {
                Results =
                [
                    new DataValue(new Variant(Repeat(string.Empty)))
                ]
            };

            byte[] warmUp = BinaryEncoder.EncodeMessage(response, context);
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] encoded = BinaryEncoder.EncodeMessage(response, context);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(encoded, Is.EqualTo(warmUp));
            Assert.That(allocated, Is.LessThan(256 * 1024),
                $"{allocated} bytes allocated to encode a {encoded.Length} byte message.");
        }

        private static void Encode(Variant value, ServiceMessageContext context, out int segments)
        {
            using var writer = new ArrayPoolBufferWriter<byte>(clearArray: true);
            using (var encoder = new BinaryEncoder(writer, context))
            {
                encoder.WriteVariant(null, value);
            }
            ReadOnlySequence<byte> sequence = writer.GetReadOnlySequence();
            segments = 0;
            foreach (ReadOnlyMemory<byte> _ in sequence)
            {
                segments++;
            }
        }

        private static DataChangeNotification CreateDataChange()
        {
            return new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = 1,
                        Value = new DataValue(new Variant(Repeat(string.Empty)))
                    }
                ]
            };
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// A buffer writer that counts requests for a buffer of any size.
        /// </summary>
        private sealed class CountingBufferWriter : IBufferWriter<byte>
        {
            private byte[] m_buffer = new byte[256];
            private int m_written;

            public int ZeroSizeHints { get; private set; }

            public byte[] WrittenBytes => m_buffer.AsSpan(0, m_written).ToArray();

            public void Advance(int count)
            {
                m_written += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return m_buffer.AsMemory(m_written);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return m_buffer.AsSpan(m_written);
            }

            private void Ensure(int sizeHint)
            {
                if (sizeHint == 0)
                {
                    ZeroSizeHints++;
                    sizeHint = 1;
                }
                if (m_buffer.Length - m_written < sizeHint)
                {
                    Array.Resize(ref m_buffer, Math.Max(m_buffer.Length * 2, m_written + sizeHint));
                }
            }
        }
    }
}
