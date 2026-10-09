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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Covers the binary codec paths that avoid per value allocations:
    /// strings, arrays, byte strings and guids written without a copy of
    /// the bytes, strings and guids read without a temporary array, and
    /// enumerations converted without boxing. The results must match the
    /// encoding they replace in both decoder modes.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class BinaryCodecAllocationTests
    {
        private const int kIterations = 20_000;
        private static readonly bool[] s_streamModes = [false, true];
        private static readonly int[] s_int32Values = [1, 2, 3, 4];

        [Test]
        public void GuidIsEncodedAsTheBytesOfGuidToByteArray()
        {
            var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            byte[] bytes = Encode(e => e.WriteGuid(null, new Uuid(guid)));

            Assert.That(bytes, Is.EqualTo(guid.ToByteArray()));
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                Assert.That(decoder.ReadGuid(null).Guid, Is.EqualTo(guid), useStream ? "stream" : "buffer");
            }
        }

        [Test]
        public void TruncatedGuidReportsADecodingError()
        {
            byte[] bytes = new byte[10];
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadGuid(null));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }
        }

        [Test]
        [TestCase("")]
        [TestCase("a")]
        [TestCase("Grüße aus Zürich € \U0001F600")]
        public void StringsRoundTripInBothDecoderModes(string value)
        {
            byte[] bytes = Encode(e => e.WriteString(null, value));
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                Assert.That(decoder.ReadString(null), Is.EqualTo(value));
            }
        }

        [Test]
        public void LongStringRoundTripsInBothDecoderModes()
        {
            string value = new('x', 60_000);
            byte[] bytes = Encode(e => e.WriteString(null, value));
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                Assert.That(decoder.ReadString(null), Is.EqualTo(value));
            }
        }

        [Test]
        public void TrailingZeroBytesAreRemovedFromStrings()
        {
            byte[] bytes = Build(w =>
            {
                w.Write(5);
                w.Write(Encoding.UTF8.GetBytes("abc"));
                w.Write((byte)0);
                w.Write((byte)0);
            });
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                Assert.That(decoder.ReadString(null), Is.EqualTo("abc"));
            }
        }

        [Test]
        public void TruncatedStringReportsADecodingError()
        {
            byte[] bytes = Build(w =>
            {
                w.Write(10);
                w.Write(Encoding.UTF8.GetBytes("abc"));
            });
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadString(null));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }
        }

        [Test]
        public void ByteStringAndArraysRoundTripInBothDecoderModes()
        {
            var byteString = ByteString.From([1, 2, 3, 0]);
            ArrayOf<int> int32Array = s_int32Values.ToArrayOf();
            byte[] bytes = Encode(e =>
            {
                e.WriteByteString(null, byteString);
                e.WriteInt32Array(null, int32Array);
                e.WriteString(null, "end");
            });
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                Assert.That(decoder.ReadByteString(null), Is.EqualTo(byteString));
                Assert.That(decoder.ReadInt32Array(null), Is.EqualTo(int32Array));
                Assert.That(decoder.ReadString(null), Is.EqualTo("end"));
            }
        }

        [Test]
        public void EncodingStringsArraysGuidsAndEnumerationsDoesNotAllocate()
        {
            var guid = new Uuid(Guid.NewGuid());
            var byteString = ByteString.From([1, 2, 3, 4]);
            ArrayOf<int> int32Array = s_int32Values.ToArrayOf();
            var text = new LocalizedText("en", "text");
            using var stream = new MemoryStream(16 * 1024 * 1024);
            using var encoder = new BinaryEncoder(stream, CreateContext(), true);

            long allocated = MeasureAllocatedBytes(() =>
            {
                encoder.WriteString(null, "hello");
                encoder.WriteLocalizedText(null, text);
                encoder.WriteByteString(null, byteString);
                encoder.WriteInt32Array(null, int32Array);
                encoder.WriteGuid(null, guid);
                encoder.WriteEnumerated(null, NodeClass.Variable);
            });

            AssertNoAllocationPerCall(allocated);
        }

        [Test]
        public void DecodingGuidsAndEnumerationsDoesNotAllocate()
        {
            byte[] bytes = Encode(e =>
            {
                for (int ii = 0; ii < kIterations + 1; ii++)
                {
                    e.WriteGuid(null, Uuid.NewUuid());
                    e.WriteEnumerated(null, NodeClass.Variable);
                }
            });
            using var decoder = new BinaryDecoder(bytes, CreateContext());

            long allocated = MeasureAllocatedBytes(() =>
            {
                _ = decoder.ReadGuid(null);
                _ = decoder.ReadEnumerated<NodeClass>(null);
            });

            AssertNoAllocationPerCall(allocated);
        }

        [Test]
        public void DecodingStringsOnlyAllocatesTheStrings()
        {
            const string value = "hello world";
            byte[] bytes = Encode(e =>
            {
                for (int ii = 0; ii < kIterations + 1; ii++)
                {
                    e.WriteString(null, value);
                }
            });

            // A string of this length is well below 64 bytes on every runtime,
            // a temporary array per string would add at least 32 bytes more.
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, useStream);
                long allocated = MeasureAllocatedBytes(() => decoder.ReadString(null));
                Assert.That(allocated, Is.LessThan(64L * kIterations), useStream ? "stream" : "buffer");
            }
        }

        [Test]
        public void EnumerationConversionsDoNotAllocate()
        {
            var dataValue = new DataValue(Variant.From(NodeClass.Method));
            Variant variant = Variant.From((int)NodeClass.Object);

            long allocated = MeasureAllocatedBytes(() =>
            {
                _ = EnumHelper.EnumToInt32(NodeClass.View);
                _ = EnumHelper.Int32ToEnum<NodeClass>(8);
                _ = EnumHelper.EnumToInt64(NodeClass.View);
                _ = EnumValue.From(NodeClass.Variable);
                _ = Variant.From(NodeClass.Variable);
                _ = dataValue.GetValue(NodeClass.Unspecified);
                _ = variant.TryCastTo(out NodeClass _);
            });

            AssertNoAllocationPerCall(allocated);
        }

        private static long MeasureAllocatedBytes(Action action)
        {
            // the first calls jit and initialize statics.
            action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int ii = 0; ii < kIterations; ii++)
            {
                action();
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void AssertNoAllocationPerCall(long allocated)
        {
            // The counter can be off by an allocation quantum when other
            // threads run a GC. A box per call would be 24 bytes or more.
            Assert.That(allocated, Is.LessThan((long)kIterations));
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static BinaryDecoder CreateDecoder(byte[] bytes, bool useStream)
        {
            return useStream
                ? new BinaryDecoder(new MemoryStream(bytes, false), CreateContext())
                : new BinaryDecoder(bytes, CreateContext());
        }

        private static byte[] Encode(Action<BinaryEncoder> write)
        {
            using var encoder = new BinaryEncoder(CreateContext());
            write(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        private static byte[] Build(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
            }
            return stream.ToArray();
        }
    }
}
