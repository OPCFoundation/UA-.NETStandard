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

#nullable enable

using System;
using System.IO;
using System.Text.Json;
using Crdt;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Round-trip tests for the <see cref="ByteStringCrdtSerializer"/>, which
    /// must distinguish a null <see cref="ByteString"/> from an empty one.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ByteStringCrdtSerializerTests
    {
        [Test]
        public void RoundTripsNullEmptyAndDataValues()
        {
            var clock = new HybridLogicalClock(ReplicaId.FromUInt64(1), TimeProvider.System);
            var map = new LWWMap<string, ByteString>();
            map.Set("data", new ByteString(new byte[] { 9, 8, 7 }), clock);
            map.Set("empty", new ByteString(Array.Empty<byte>()), clock);
            map.Set("null", default, clock);

            byte[] bytes = map.ToByteArray(CrdtValues.String, ByteStringCrdtSerializer.Instance);
            LWWMap<string, ByteString> restored = LWWMap<string, ByteString>.ReadFrom(
                bytes, CrdtValues.String, ByteStringCrdtSerializer.Instance, CrdtReaderOptions.Default);

            Assert.That(restored.TryGetValue("data", out ByteString data), Is.True);
            Assert.That(data.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));

            Assert.That(restored.TryGetValue("empty", out ByteString empty), Is.True);
            Assert.That(empty.IsNull, Is.False, "an empty ByteString must not decode as null");
            Assert.That(empty.ToArray(), Is.Empty);

            Assert.That(restored.TryGetValue("null", out ByteString nul), Is.True);
            Assert.That(nul.IsNull, Is.True, "a null ByteString must decode as null");
        }

        [Test]
        public void RoundTripsJsonNullAndDataValues()
        {
            ByteString data = JsonRoundTrip(new ByteString(new byte[] { 4, 5, 6 }));
            Assert.That(data.IsNull, Is.False);
            Assert.That(data.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));

            ByteString empty = JsonRoundTrip(new ByteString(Array.Empty<byte>()));
            Assert.That(empty.IsNull, Is.False, "an empty ByteString must not decode as null");
            Assert.That(empty.ToArray(), Is.Empty);

            ByteString nul = JsonRoundTrip(default);
            Assert.That(nul.IsNull, Is.True, "a null ByteString must decode as null");
        }

        private static ByteString JsonRoundTrip(ByteString value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                ByteStringCrdtSerializer.Instance.WriteJson(writer, value);
                writer.Flush();
            }

            var reader = new Utf8JsonReader(stream.ToArray());
            reader.Read();
            return ByteStringCrdtSerializer.Instance.ReadJson(ref reader);
        }
    }
}