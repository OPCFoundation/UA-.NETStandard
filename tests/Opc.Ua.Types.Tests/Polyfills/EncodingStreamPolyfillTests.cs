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

using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Polyfills
{
    /// <summary>
    /// Tests for the span-based <see cref="System.IO.Stream"/> and <see cref="System.Text.Encoding"/>
    /// polyfills used by the experimental Avro codec on the legacy target frameworks.
    /// </summary>
    /// <remarks>
    /// The tests call the span-based surface directly. On .NET 7+ these bind to the in-box BCL
    /// methods; on the legacy frameworks (net472/net48/netstandard2.0) they bind to the polyfill
    /// extensions, so the legacy coverage leg exercises the polyfill implementations.
    /// </remarks>
    [TestFixture]
    [Category("Encoder")]
    [Parallelizable]
    public class EncodingStreamPolyfillTests
    {
        [Test]
        public void StreamReadExactly_FillsBufferFromStream()
        {
            byte[] source = [1, 2, 3, 4, 5, 6, 7, 8];
            using var stream = new MemoryStream(source);
            Span<byte> destination = new byte[source.Length];

            stream.ReadExactly(destination);

            Assert.That(destination.ToArray(), Is.EqualTo(source));
        }

        [Test]
        public void StreamReadExactly_StreamEndsEarly_Throws()
        {
            using var stream = new MemoryStream([1, 2, 3]);
            byte[] destination = new byte[8];

            Assert.That(
                () => stream.ReadExactly(destination.AsSpan()),
                Throws.InstanceOf<EndOfStreamException>());
        }

        [Test]
        public void StreamReadSpan_ReadsAvailableBytes()
        {
            byte[] source = [10, 20, 30, 40];
            using var stream = new MemoryStream(source);
            Span<byte> destination = new byte[source.Length];

            int read = stream.Read(destination);

            Assert.That(read, Is.EqualTo(source.Length));
            Assert.That(destination.ToArray(), Is.EqualTo(source));
        }

        [Test]
        public void StreamReadSpan_AtEndOfStream_ReturnsZero()
        {
            using var stream = new MemoryStream([1, 2]);
            stream.Position = stream.Length;
            Span<byte> destination = new byte[4];

            int read = stream.Read(destination);

            Assert.That(read, Is.Zero);
        }

        [Test]
        public void StreamWriteSpan_WritesBytes()
        {
            byte[] payload = [9, 8, 7, 6, 5];
            using var stream = new MemoryStream();

            stream.Write(payload.AsSpan());

            Assert.That(stream.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void EncodingGetStringSpan_DecodesBytes()
        {
            byte[] utf8 = Encoding.UTF8.GetBytes("héllo");

            string decoded = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(utf8));

            Assert.That(decoded, Is.EqualTo("héllo"));
        }

        [Test]
        public void EncodingGetStringSpan_EmptyInput_ReturnsEmpty()
        {
            string decoded = Encoding.UTF8.GetString(ReadOnlySpan<byte>.Empty);

            Assert.That(decoded, Is.Empty);
        }

        [Test]
        public void EncodingGetBytesSpan_EncodesChars()
        {
            const string text = "wörld";
            byte[] expected = Encoding.UTF8.GetBytes(text);
            Span<byte> destination = new byte[Encoding.UTF8.GetByteCount(text)];

            int written = Encoding.UTF8.GetBytes(text.AsSpan(), destination);

            Assert.That(written, Is.EqualTo(expected.Length));
            Assert.That(destination[..written].ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void EncodingGetBytesSpan_EmptyInput_ReturnsZero()
        {
            int written = Encoding.UTF8.GetBytes(ReadOnlySpan<char>.Empty, Span<byte>.Empty);

            Assert.That(written, Is.Zero);
        }
    }
}
