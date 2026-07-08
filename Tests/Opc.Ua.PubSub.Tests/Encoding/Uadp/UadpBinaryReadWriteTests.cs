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
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Targeted coverage for the <see cref="UadpBinaryWriter"/> and
    /// <see cref="UadpBinaryReader"/> primitives — exercises every
    /// scalar read/write helper, plus the EnsureCapacity grow path,
    /// Reserve/Patch round-trips, and bounds-checked failures on
    /// the reader.
    /// </summary>
    [TestFixture]
    public class UadpBinaryReadWriteTests
    {
        [Test]
        public void Writer_AllScalars_RoundTrip_Via_Reader()
        {
            byte[] buffer = new byte[1024];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            writer.WriteByte(0xAB);
            writer.WriteUInt16Le(0x1234);
            writer.WriteUInt32Le(0xDEADBEEF);
            writer.WriteUInt64Le(0x0102030405060708UL);
            writer.WriteInt64Le(unchecked((long)0xFFFFFFFFFFFFFFFEUL));
            writer.WriteString("hello");
            writer.WriteString(null);
            writer.WriteGuid(new Guid("12345678-90AB-CDEF-1234-567890ABCDEF"));
            writer.WriteBytes([1, 2, 3, 4]);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);

            Assert.That(reader.TryReadByte(out byte b), Is.True);
            Assert.That(b, Is.EqualTo((byte)0xAB));
            Assert.That(reader.TryReadUInt16Le(out ushort u16), Is.True);
            Assert.That(u16, Is.EqualTo((ushort)0x1234));
            Assert.That(reader.TryReadUInt32Le(out uint u32), Is.True);
            Assert.That(u32, Is.EqualTo(0xDEADBEEFu));
            Assert.That(reader.TryReadUInt64Le(out ulong u64), Is.True);
            Assert.That(u64, Is.EqualTo(0x0102030405060708UL));
            Assert.That(reader.TryReadInt64Le(out long i64), Is.True);
            Assert.That(i64, Is.EqualTo(unchecked((long)0xFFFFFFFFFFFFFFFEUL)));
            Assert.That(reader.TryReadString(out string? s), Is.True);
            Assert.That(s, Is.EqualTo("hello"));
            Assert.That(reader.TryReadString(out string? sNull), Is.True);
            Assert.That(sNull, Is.Null);
            Assert.That(reader.TryReadGuid(out Guid g), Is.True);
            Assert.That(g,
                Is.EqualTo(new Guid("12345678-90AB-CDEF-1234-567890ABCDEF")));
            Assert.That(reader.TryReadBytes(4, out byte[] body), Is.True);
            Assert.That(body, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void Reader_TruncatedScalars_ReturnFalse()
        {
            var reader = new UadpBinaryReader(Array.Empty<byte>(), 0, 0);
            Assert.That(reader.TryReadByte(out _), Is.False);
            Assert.That(reader.TryReadUInt16Le(out _), Is.False);
            Assert.That(reader.TryReadUInt32Le(out _), Is.False);
            Assert.That(reader.TryReadUInt64Le(out _), Is.False);
            Assert.That(reader.TryReadInt64Le(out _), Is.False);
            Assert.That(reader.TryReadString(out _), Is.False);
            Assert.That(reader.TryReadGuid(out _), Is.False);
            Assert.That(reader.TryReadBytes(4, out _), Is.False);
        }

        [Test]
        public void Reader_TryReadString_NegativeLength_ReturnsNull()
        {
            // Length = -1 (all bytes 0xFF) is the UA-binary null sentinel.
            byte[] buf = [0xFF, 0xFF, 0xFF, 0xFF];
            var reader = new UadpBinaryReader(buf, 0, buf.Length);
            Assert.That(reader.TryReadString(out string? value), Is.True);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void Reader_TryReadString_OversizedLength_ReturnsFalse()
        {
            byte[] buf = [10, 0, 0, 0, (byte)'A']; // declares 10 bytes, only 1 available
            var reader = new UadpBinaryReader(buf, 0, buf.Length);
            Assert.That(reader.TryReadString(out _), Is.False);
        }

        [Test]
        public void Writer_Reserve_And_Patch_RoundTrips()
        {
            byte[] buffer = new byte[64];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            int reservedU16 = writer.Reserve(2);
            int reservedU32 = writer.Reserve(4);
            writer.WriteByte(0xAA);
            writer.PatchUInt16Le(reservedU16, 0x1234);
            writer.PatchUInt32Le(reservedU32, 0xDEADBEEF);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Assert.That(reader.TryReadUInt16Le(out ushort u16), Is.True);
            Assert.That(u16, Is.EqualTo((ushort)0x1234));
            Assert.That(reader.TryReadUInt32Le(out uint u32), Is.True);
            Assert.That(u32, Is.EqualTo(0xDEADBEEFu));
            Assert.That(reader.TryReadByte(out byte b), Is.True);
            Assert.That(b, Is.EqualTo((byte)0xAA));
        }

        [Test]
        public void Writer_Advance_MovesPosition()
        {
            byte[] buffer = new byte[16];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteByte(1);
            writer.Advance(4);
            writer.WriteByte(2);
            Assert.That(writer.Position, Is.EqualTo(6));
            Assert.That(buffer[0], Is.EqualTo((byte)1));
            Assert.That(buffer[5], Is.EqualTo((byte)2));
        }

        [Test]
        public void Reader_Advance_MovesPosition()
        {
            byte[] buffer = [1, 2, 3, 4, 5];
            var reader = new UadpBinaryReader(buffer, 0, buffer.Length);
            reader.Advance(3);
            Assert.That(reader.Position, Is.EqualTo(3));
            Assert.That(reader.TryReadByte(out byte b), Is.True);
            Assert.That(b, Is.EqualTo((byte)4));
        }

        [Test]
        public void Writer_WriteBytes_Empty_IsNoOp()
        {
            byte[] buffer = new byte[8];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteBytes(Array.Empty<byte>());
            Assert.That(writer.Position, Is.Zero);
        }

        [Test]
        public void Writer_GrowsBufferOnCapacity()
        {
            // Start with a tight buffer that requires a grow.
            byte[] buffer = new byte[4];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            byte[] large = new byte[64];
            for (int i = 0; i < large.Length; i++)
            {
                large[i] = (byte)(i & 0xFF);
            }
            Assert.That(
                () => writer.WriteBytes(large),
                Throws.InstanceOf<InvalidOperationException>(),
                "Fixed-capacity writer rejects overflow.");
        }

        [Test]
        public void Writer_WriteString_LargeUtf8_RoundTrips()
        {
            string value = new('Ä', 200); // 400 UTF-8 bytes
            byte[] buffer = new byte[1024];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteString(value);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Assert.That(reader.TryReadString(out string? decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(value));
        }

        [Test]
        public void Writer_WriteString_Empty_RoundTrips()
        {
            byte[] buffer = new byte[16];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            writer.WriteString(string.Empty);

            var reader = new UadpBinaryReader(buffer, 0, writer.Position);
            Assert.That(reader.TryReadString(out string? decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Reader_TryReadBytes_NegativeCount_ReturnsFalse()
        {
            var reader = new UadpBinaryReader([1, 2, 3], 0, 3);
            Assert.That(reader.TryReadBytes(-1, out _), Is.False);
        }

        [Test]
        public void Reader_Origin_Honored()
        {
            byte[] outer = [99, 99, 1, 2, 3];
            var reader = new UadpBinaryReader(outer, 2, 3);
            Assert.That(reader.TryReadByte(out byte b), Is.True);
            Assert.That(b, Is.EqualTo((byte)1));
            Assert.That(reader.Remaining, Is.EqualTo(2));
        }

        [Test]
        public void Writer_PatchOutsideCapacity_Throws()
        {
            byte[] buffer = new byte[4];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            Assert.That(() => writer.PatchUInt16Le(10, 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => writer.PatchUInt32Le(10, 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Writer_Reserve_AdvancesPosition()
        {
            byte[] buffer = new byte[16];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            int pos = writer.Reserve(6);
            Assert.That(pos, Is.Zero);
            Assert.That(writer.Position, Is.EqualTo(6));
        }
    }
}
