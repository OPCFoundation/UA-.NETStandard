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
using System.Buffers;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Buffers;

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0301 // Simplify collection initialization

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ByteStringTests
    {
        [Test]
        public void CreateFromMemoryTest()
        {
            // Arrange
            byte[] bytes = [1, 2, 3];
            var byteString = new ByteString(bytes);

            // Act
            ReadOnlyMemory<byte> memory = byteString.Memory;

            // Assert
            Assert.That(memory.ToArray(), Is.EqualTo(bytes));
        }

        [Test]
        public void NullCheckTest()
        {
            ByteString nullByteString = default;
            Assert.That(nullByteString.IsNull, Is.True);
            Assert.That(nullByteString.IsEmpty, Is.True);
            Assert.That(ByteString.Empty.IsEmpty, Is.True);
            Assert.That(ByteString.Empty.IsNull, Is.False);
        }

        [Test]
        public void CreateFromReadOnlySequnceTest()
        {
            // Arrange
            var bytes1 = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]).ToReadOnlySequence(2);
            var bytes2 = new ReadOnlySequence<byte>([1, 2, 3, 4, 5]);
            var byteString1 = new ByteString(bytes1);
            var byteString2 = new ByteString(bytes2);

            // Act
            ReadOnlyMemory<byte> memory1 = byteString1.Memory;
            ReadOnlyMemory<byte> memory2 = byteString2.Memory;

            // Assert
            Assert.That(memory1.ToArray(), Is.EqualTo(bytes1.ToArray()));
            Assert.That(memory2.ToArray(), Is.EqualTo(bytes2.ToArray()));
        }

        [Test]
        public void SpanPropertyTest()
        {
            // Arrange
            byte[] bytes = [1, 2, 3];
            var byteString = new ByteString(bytes);

            // Act
            ReadOnlySpan<byte> span = byteString.Span;

            // Assert
            Assert.That(span.ToArray(), Is.EqualTo(bytes));
        }

        [Test]
        public void LengthPropertyTest()
        {
            // Arrange
            byte[] bytes = [1, 2, 3];
            var byteString = new ByteString(bytes);

            // Act
            int length = byteString.Length;

            // Assert
            Assert.That(length, Is.EqualTo(3));
        }

        [Test]
        public void IsEmptyPropertyTest()
        {
            // Arrange
            ByteString emptyByteString = ByteString.Empty;
            var nonEmptyByteString = new ByteString(new byte[] { 1, 2, 3 });

            // Act & Assert
            Assert.That(emptyByteString.IsEmpty, Is.True);
            Assert.That(nonEmptyByteString.IsEmpty, Is.False);
        }

        [Test]
        public void EqualsByteStringTest()
        {
            // Arrange
            var byteString1 = new ByteString(new byte[] { 1, 2, 3 });
            var byteString2 = new ByteString(new byte[] { 1, 2, 3 });
            var byteString3 = new ByteString(new byte[] { 4, 5, 6 });

            // Act & Assert
            Assert.That(byteString1.Equals(byteString2), Is.True);
            Assert.That(byteString1.Equals(byteString3), Is.False);
            Assert.That(byteString1 == byteString2, Is.True);
            Assert.That(byteString1 == byteString3, Is.False);
            Assert.That(byteString1 != byteString2, Is.False);
            Assert.That(byteString1 != byteString3, Is.True);
        }

        [Test]
        public void EqualsReadOnlyMemoryTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            var memory = new ReadOnlyMemory<byte>([1, 2, 3]);
            var differentMemory = new ReadOnlyMemory<byte>([4, 5, 6]);

            // Act & Assert
            Assert.That(byteString.Equals(memory), Is.True);
            Assert.That(byteString.Equals((object)memory), Is.True);
            Assert.That(byteString.Equals(differentMemory), Is.False);
            Assert.That(byteString == memory, Is.True);
            Assert.That(byteString == differentMemory, Is.False);
            Assert.That(byteString != memory, Is.False);
            Assert.That(byteString != differentMemory, Is.True);
        }

        [Test]
        public void EqualsByteArrayTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            byte[] array = [1, 2, 3];
            byte[] differentArray = [4, 5, 6];

            // Act & Assert
            Assert.That(byteString.Equals(array), Is.True);
            Assert.That(byteString.Equals((object)array), Is.True);
            Assert.That(byteString.Equals(differentArray), Is.False);
            Assert.That(byteString == array, Is.True);
            Assert.That(byteString == differentArray, Is.False);
            Assert.That(byteString != array, Is.False);
            Assert.That(byteString != differentArray, Is.True);
        }

        [Test]
        public void EqualsEmptyByteArrayTest()
        {
            // Arrange
            ByteString byteString = ByteString.Empty;
            byte[] array = Array.Empty<byte>();
            byte[] differentArray = [4, 5, 6];

            // Act & Assert
            Assert.That(byteString.Equals(array), Is.True);
            Assert.That(byteString.Equals(differentArray), Is.False);
            Assert.That(byteString == array, Is.True);
            Assert.That(byteString == differentArray, Is.False);
            Assert.That(byteString != array, Is.False);
            Assert.That(byteString != differentArray, Is.True);
        }

        [Test]
        public void EqualsReadOnlySpanTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            var span = new ReadOnlySpan<byte>([1, 2, 3]);
            var differentSpan = new ReadOnlySpan<byte>([4, 5, 6]);

            // Act & Assert
            Assert.That(byteString.Equals(span), Is.True);
            Assert.That(byteString.Equals(differentSpan), Is.False);
            Assert.That(byteString == span, Is.True);
            Assert.That(byteString == differentSpan, Is.False);
            Assert.That(byteString != span, Is.False);
            Assert.That(byteString != differentSpan, Is.True);
        }

        [Test]
        public void EqualsReadOnlySequenceTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            var sequence = new ReadOnlySequence<byte>([1, 2, 3]);
            var differentSequence = new ReadOnlySequence<byte>([4, 5, 6]);

            // Act & Assert
            Assert.That(byteString.Equals(sequence), Is.True);
            Assert.That(byteString.Equals(differentSequence), Is.False);
            Assert.That(byteString == sequence, Is.True);
            Assert.That(byteString == differentSequence, Is.False);
            Assert.That(byteString != sequence, Is.False);
            Assert.That(byteString != differentSequence, Is.True);
        }

        [Test]
        public void EqualsEmptyReadOnlySequenceTest()
        {
            // Arrange
            ByteString byteString = ByteString.Empty;
            var sequence = new ReadOnlySequence<byte>([]);
            var differentSequence = new ReadOnlySequence<byte>([4, 5, 6]);

            // Act & Assert
            Assert.That(byteString.Equals(sequence), Is.True);
            Assert.That(byteString.Equals(differentSequence), Is.False);
            Assert.That(byteString == sequence, Is.True);
            Assert.That(byteString == differentSequence, Is.False);
            Assert.That(byteString != sequence, Is.False);
            Assert.That(byteString != differentSequence, Is.True);
        }

        [Test]
        public void EqualsMultiSegmentReadOnlySequenceTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 6 });
            var sequence = new ReadOnlyMemory<byte>([1, 2, 3, 4, 6]).ToReadOnlySequence(2);
            var differentSequence1 = new ReadOnlyMemory<byte>([5, 6, 7]).ToReadOnlySequence(2);
            var differentSequence2 = new ReadOnlyMemory<byte>([1, 2, 8, 4, 6]).ToReadOnlySequence(2);
            ReadOnlySequence<byte> differentSequence3 = SequenceHelper.CreateEmpty<byte>(3);

            // Act & Assert
            Assert.That(byteString.Equals(sequence), Is.True);
            Assert.That(byteString.Equals((object)sequence), Is.True);
            Assert.That(byteString.Equals(differentSequence1), Is.False);
            Assert.That(byteString.Equals(differentSequence2), Is.False);
            Assert.That(byteString.Equals(differentSequence3), Is.False);
            Assert.That(byteString == sequence, Is.True);
            Assert.That(byteString == differentSequence1, Is.False);
            Assert.That(byteString == differentSequence2, Is.False);
            Assert.That(byteString == differentSequence3, Is.False);
            Assert.That(byteString != sequence, Is.False);
            Assert.That(byteString != differentSequence1, Is.True);
            Assert.That(byteString != differentSequence2, Is.True);
            Assert.That(byteString != differentSequence3, Is.True);
        }

        [Test]
        public void EqualsObjectTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            object sameByteString = new ByteString(new byte[] { 1, 2, 3 });
            object differentByteString = new ByteString(new byte[] { 4, 5, 6 });
            object? nullObject = null;
            object notByteString = "not a byte string";

            // Act & Assert
            Assert.That(byteString.Equals(sameByteString), Is.True);
            Assert.That(byteString.Equals(differentByteString), Is.False);
            Assert.That(byteString.Equals(nullObject), Is.False);
            Assert.That(byteString.Equals(notByteString), Is.False);
        }

        [Test]
        public void ToStringTest()
        {
            // Arrange
            byte[] values = [1, 2, 3];
            var byteString = new ByteString(values);

            // Act
            string result = byteString.ToString();

            // Assert
            Assert.That(result, Is.EqualTo(Convert.ToBase64String(values)));
        }

        [Test]
        public void GetHashCodeTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });

            // Act
            int hashCode = byteString.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.EqualTo(ReadOnlySpan.ComputeHash32(byteString.Span)));
        }

        [Test]
        public void EqualityOperatorsTest()
        {
            // Arrange
            var byteString1 = new ByteString(new byte[] { 1, 2, 3 });
            var byteString2 = new ByteString(new byte[] { 1, 2, 3 });
            var byteString3 = new ByteString(new byte[] { 4, 5, 6 });

            // Act & Assert
            Assert.That(byteString1 == byteString2, Is.True);
            Assert.That(byteString1 != byteString3, Is.True);
            Assert.That(byteString1 != byteString2, Is.False);
            Assert.That(byteString1 == byteString3, Is.False);
        }

        [Test]
        public void EmptyPropertyTest()
        {
            // Act
            ByteString emptyByteString = ByteString.Empty;

            // Assert
            Assert.That(emptyByteString.IsEmpty, Is.True);
            Assert.That(emptyByteString.Length, Is.EqualTo(0));
        }

        [Test]
        public void ImplicitConversionToReadOnlyMemoryTest()
        {
            // Arrange
            // Act
            ReadOnlyMemory<byte> memory = new ByteString(new byte[] { 1, 2, 3 });

            // Assert
            Assert.That(memory.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void ExpicitConversionFromReadOnlySpanTest()
        {
            // Arrange
            // Act
            ByteString byteString = (ByteString)new ReadOnlySpan<byte>([1, 2, 3]);

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void ExplicitConversionFromSpanTest()
        {
            // Arrange
            // Act
            ByteString byteString = (ByteString)new Span<byte>([1, 2, 3]);

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void ExplicitConversionFromByteArrayTest()
        {
            // Arrange
            byte[] array = [1, 2, 3];

            // Act
            ByteString byteString = (ByteString)array;

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(array));
        }

        [Test]
        public void FromReadOnlyMemoryTest()
        {
            // Arrange
            var memory = new ReadOnlyMemory<byte>([1, 2, 3]);

            // Act
            var byteString = ByteString.From(in memory);

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(memory.ToArray()));
        }

        [Test]
        public void FromMemoryTest()
        {
            // Arrange
            var memory = new Memory<byte>([1, 2, 3]);

            // Act
            var byteString = ByteString.From(in memory);

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(memory.ToArray()));
        }

        [Test]
        public void FromReadOnlySequenceTest()
        {
            // Arrange
            var sequence = new ReadOnlySequence<byte>([1, 2, 3]);

            // Act
            var byteString = ByteString.From(in sequence);

            // Assert
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(sequence.ToArray()));
        }

        [Test]
        public void SliceTest1()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 5 });

            // Act
            ByteString slicedByteString = byteString.Slice(1, 3);

            // Assert
            Assert.That(slicedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 2, 3, 4 }));
        }

        [Test]
        public void SliceTest2()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3, 4, 5 });

            // Act
#pragma warning disable IDE0057 // Use range operator
            ByteString slicedByteString = byteString.Slice(3);
#pragma warning restore IDE0057 // Use range operator

            // Assert
            Assert.That(slicedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
        }

        [Test]
        public void CopyTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });

            // Act
            ByteString copiedByteString = byteString.Copy();

            // Assert
            Assert.That(copiedByteString.Span.ToArray(), Is.EqualTo(byteString.Span.ToArray()));
        }

        [Test]
        public void FromBytesTest()
        {
            // Arrange
            const byte b1 = 1;
            const byte b2 = 2;
            const byte b3 = 3;
            const byte b4 = 4;
            const byte b5 = 5;
            const byte b6 = 6;

            // Act
            var combinedByteString = ByteString.From(b1, b2, b3, b4, b5, b6);

            // Assert
            Assert.That(combinedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void CombineByteArrayTest()
        {
            // Arrange
            byte[] array1 = [1, 2, 3];
            byte[] array2 = [4, 5, 6];

            // Act
            var combinedByteString = ByteString.Combine([array1.ToByteString(), array2.ToByteString()]);

            // Assert
            Assert.That(combinedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void CombineAndPadByteArrayTest()
        {
            // Arrange
            byte[] array1 = [1, 2, 3];
            byte[] array2 = [4, 5, 6];

            // Act
            var combinedByteString = ByteString.Combine(5, [array1.ToByteString(), array2.ToByteString()]);

            // Assert
            Assert.That(combinedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 0, 0, 0, 0 }));
        }

        [Test]
        public void CombineByteStringTest()
        {
            // Arrange
            var byteString1 = new ByteString(new byte[] { 1, 2, 3 });
            var byteString2 = new ByteString(new byte[] { 4, 5, 6 });

            // Act
            var combinedByteString = ByteString.Combine(byteString1, byteString2);

            // Assert
            Assert.That(combinedByteString.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void CopyToByteArrayTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            byte[] array = new byte[5];

            // Act
            byteString.CopyTo(array, 1);

            // Assert
            Assert.That(array, Is.EqualTo(new byte[] { 0, 1, 2, 3, 0 }));
        }

        [Test]
        public void CopyToSpanTest()
        {
            // Arrange
            var byteString = new ByteString(new byte[] { 1, 2, 3 });
            var span = new Span<byte>(new byte[5]);

            // Act
            byteString.CopyTo(span[1..]);

            // Assert
            Assert.That(span.ToArray(), Is.EqualTo(new byte[] { 0, 1, 2, 3, 0 }));
        }

        [Test]
        public void CopyToBufferWriterTest()
        {
            // Arrange
            byte[] values = [1, 2, 3];
            var byteString = new ByteString(values);
            var bufferWriter = new ArrayPoolBufferWriter<byte>();

            // Act
            byteString.CopyTo(in bufferWriter);

            // Assert
            Assert.That(bufferWriter.GetReadOnlySequence().ToArray(), Is.EqualTo(values));
        }

        [Test]
        public void CopyToEmptyBufferWriterTest()
        {
            // Arrange
            ByteString byteString = ByteString.Empty;
            var bufferWriter = new ArrayPoolBufferWriter<byte>();

            // Act
            byteString.CopyTo(in bufferWriter);

            // Assert
            Assert.That(bufferWriter.GetReadOnlySequence().Length, Is.EqualTo(0));
        }

        [Test]
        public void FormatAsBase64Test()
        {
            // Arrange
            byte[] values = [1, 2, 3];
            var byteString = new ByteString(values);
            var bufferWriter = new ArrayPoolBufferWriter<byte>();

            // Act
            byteString.FormatAsBase64(in bufferWriter);

            // Assert
            byte[] expected = [.. Convert.ToBase64String(values).Select(c => (byte)c)];
            Assert.That(bufferWriter.GetReadOnlySequence().ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void FormatAsBase64EmptyTest()
        {
            // Arrange
            ByteString byteString = ByteString.Empty;
            var bufferWriter = new ArrayPoolBufferWriter<byte>();

            // Act
            byteString.FormatAsBase64(in bufferWriter);

            // Assert
            Assert.That(bufferWriter.GetReadOnlySequence().Length, Is.EqualTo(0));
        }

        [Test]
        public void ToArrayTest()
        {
            // Arrange
            byte[] expected = [1, 2, 3];
            var byteString = new ByteString(expected);

            // Act
            byte[] array = byteString.ToArray();

            // Assert
            Assert.That(array, Is.EqualTo(expected));
        }

        [Test]
        public void ToBase64Test()
        {
            // Arrange
            byte[] expected = [1, 2, 3];
            var byteString = new ByteString(expected);

            // Act
            string base64 = byteString.ToBase64();

            // Assert
            Assert.That(base64, Is.EqualTo(Convert.ToBase64String(expected)));
        }

        [Test]
        public void TryFromBase64StringTest()
        {
            // Arrange
            byte[] expected = [1, 2, 3];
            string base64 = Convert.ToBase64String(expected);

            // Act
            bool success = ByteString.TryFromBase64(base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void TryFromBase64LargeStringTest()
        {
            // Arrange
            byte[] expected = new byte[1000];
            Fill(expected, 1);
            string base64 = Convert.ToBase64String(expected);

            // Act
            bool success = ByteString.TryFromBase64(base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void TryFromBase64EmptyStringTest()
        {
            // Arrange
            // Act
            bool success = ByteString.TryFromBase64(string.Empty, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.IsEmpty, Is.True);
        }

        [Test]
        public void TryFromBase64WithBadStringTest()
        {
            // Arrange
            // Act
            bool success = ByteString.TryFromBase64("?????", out _);

            // Assert
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryFromBase64Utf8Test()
        {
            // Arrange
            byte[] buffer = [1, 2, 3];
            string base64 = Convert.ToBase64String(buffer);
            var utf8Base64 = new ReadOnlySpan<byte>(System.Text.Encoding.UTF8.GetBytes(base64));

            // Act
            bool success = ByteString.TryFromBase64(utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(buffer));
        }

        [Test]
        public void TryFromBase64Utf8EmptyTest()
        {
            // Arrange
            // Act
            bool success = ByteString.TryFromBase64([], out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.IsEmpty, Is.True);
        }

        [Test]
        public void TryFromBase64Utf8LargeTest()
        {
            // Arrange
            byte[] expected = new byte[1000];
            Fill(expected, 1);
            string base64 = Convert.ToBase64String(expected);
            var utf8Base64 = new ReadOnlySpan<byte>(System.Text.Encoding.UTF8.GetBytes(base64));

            // Act
            bool success = ByteString.TryFromBase64(utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void TryFromBase64Utf8BadTest1()
        {
            // Arrange
            // Act
            bool success = ByteString.TryFromBase64("not-base64"u8, out _);

            // Assert
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryFromBase64Utf8BadTest2()
        {
            // Arrange
            byte[] bad = new byte[1000];
            Fill(bad, (byte)'ü');

            // Act
            bool success = ByteString.TryFromBase64(bad, out _);

            // Assert
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryFromBase64ReadOnlySequenceTest()
        {
            // Arrange
            byte[] expected = [1, 2, 3, 4, 5, 6];
            string base64 = Convert.ToBase64String(expected);
            var utf8Base64 = new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(base64));

            // Act
            bool success = ByteString.TryFromBase64(in utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void TryFromBase64ReadOnlySequenceMultiSegmentTest()
        {
            // Arrange
            byte[] expected = [1, 2, 3, 4, 5, 6];
            string base64 = Convert.ToBase64String(expected);
            var utf8Base64 = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(base64)).ToReadOnlySequence(2);

            // Act
            bool success = ByteString.TryFromBase64(in utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void TryFromBase64ReadOnlySequenceEmptyMultiSegmentTest()
        {
            // Arrange
            ReadOnlySequence<byte> utf8Base64 = SequenceHelper.CreateEmpty<byte>(3);

            // Act
            bool success = ByteString.TryFromBase64(in utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.IsEmpty, Is.True);
        }

        [Test]
        public void TryFromBase64ReadOnlySequenceEmptyTest()
        {
            // Arrange
            var utf8Base64 = new ReadOnlySequence<byte>([]);

            // Act
            bool success = ByteString.TryFromBase64(in utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.IsEmpty, Is.True);
        }

        [Test]
        public void TryFromBase64ReadOnlySequenceMultiSegmentLargeTest()
        {
            // Arrange
            byte[] expected = new byte[1000];
            Fill(expected, 1);
            string base64 = Convert.ToBase64String(expected);
            var utf8Base64 = new ReadOnlyMemory<byte>(
                System.Text.Encoding.UTF8.GetBytes(base64)).ToReadOnlySequence(2);

            // Act
            bool success = ByteString.TryFromBase64(in utf8Base64, out ByteString byteString);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(byteString.Span.ToArray(), Is.EqualTo(expected));
        }

        private static void Fill(byte[] buffer, byte value)
        {
#if NETFRAMEWORK
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = value;
            }
#else
            Array.Fill(buffer, value);
#endif
        }
    }
}
