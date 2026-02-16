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
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Wraps a read only memory when constructing using constructors.
    /// Use assignment to copy memory into the byte string. The
    /// ByteString can be re-interpreted as <see cref="ReadOnlyMemory{T}"/>
    /// memory layout.
    /// </summary>
    public readonly struct ByteString :
        IEquatable<ByteString>, IComparable<ByteString>,
        IEquatable<ReadOnlyMemory<byte>>, IComparable<ReadOnlyMemory<byte>>,
        IEquatable<byte[]>, IComparable<byte[]>,
        IEquatable<ReadOnlySequence<byte>>
    {
        /// <summary>
        /// Provides read-only access to the memory backing the
        /// <see cref="ByteString"/>.
        /// No data is copied so this is the most efficient way
        /// of accessing.
        /// </summary>
        public ReadOnlyMemory<byte> Memory => m_memory;

        /// <summary>
        /// Same as a readonly span
        /// </summary>
        public ReadOnlySpan<byte> Span => m_memory.Span;

        /// <summary>
        /// Returns the length of this ByteString in bytes.
        /// </summary>
        public int Length => m_memory.Length;

        /// <summary>
        /// Returns <c>true</c> if this byte string is
        /// empty, <c>false</c> otherwise.
        /// </summary>
        public bool IsEmpty => m_memory.IsEmpty;

        /// <summary>
        /// Intern the memory into this byte string (no copy)
        /// </summary>
        internal ByteString(ReadOnlyMemory<byte> bytes)
        {
            m_memory = bytes;
        }

        /// <summary>
        /// Intern the memory into a byte string (only copyies if multisegment)
        /// </summary>
        internal ByteString(ReadOnlySequence<byte> bytes)
        {
            m_memory = bytes.IsSingleSegment ? bytes.First : bytes.ToArray();
        }

        /// <inheritdoc/>
        public bool Equals(ByteString other)
        {
            return Equals(other.m_memory.Span);
        }

        /// <inheritdoc/>
        public int CompareTo(ByteString other)
        {
            return CompareTo(other.m_memory.Span);
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlyMemory<byte> other)
        {
            return Equals(other.Span);
        }

        /// <inheritdoc/>
        public int CompareTo(ReadOnlyMemory<byte> other)
        {
            return CompareTo(other.Span);
        }

        /// <inheritdoc/>
        public bool Equals(byte[]? other)
        {
            return other == null || other.Length == 0 ?
                IsEmpty :
                Equals(other.AsSpan());
        }

        /// <inheritdoc/>
        public int CompareTo(byte[]? other)
        {
            return other == null || other.Length == 0 ?
                IsEmpty ? 0 : -1 :
                CompareTo(other.AsSpan());
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlySpan<byte> other)
        {
            if (IsEmpty)
            {
                return other.IsEmpty;
            }
#if DEBUG
            bool equal = m_memory.Span.SequenceEqual(other);
            if (!equal)
            {
                equal = m_memory.Span.SequenceEqual(other);
                Debug.Assert(!equal); // Memory corruption?
#if DEBUG_EQUAL
            if (_memory.Length == other.Length)
            {
                for (var i = 0; i < _memory.Length; i++)
                {
                    if (_memory.Span[i] != other[i])
                    {
                        return false;
                    }
                }
                Debug.Fail("Failed but equal");
            }
#endif
                return equal;
            }
#endif
            return m_memory.Span.SequenceEqual(other);
        }

        /// <inheritdoc/>
        public int CompareTo(ReadOnlySpan<byte> other)
        {
            return m_memory.Span.SequenceCompareTo(other);
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlySequence<byte> other)
        {
            if (Length != other.Length)
            {
                return false;
            }
            if (other.IsSingleSegment)
            {
#if NET8_0_OR_GREATER
                return other.FirstSpan.SequenceEqual(m_memory.Span);
#else
                return other.First.Span.SequenceEqual(m_memory.Span);
#endif
            }
            var enumerator1 = new SequenceReader(
                new ReadOnlySequence<byte>(m_memory));
            var enumerator2 = new SequenceReader(other);
            while (
                enumerator1.TryRead(out byte b1) &&
                enumerator2.TryRead(out byte b2))
            {
                if (b1 != b2)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => IsEmpty,
                byte[] bytes => Equals(bytes),
                ReadOnlyMemory<byte> bytes => Equals(bytes),
                ReadOnlySequence<byte> bytes => Equals(bytes),
                ByteString b => Equals(b),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToBase64();
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return ReadOnlySpan.ComputeHash32(m_memory.Span);
        }

        /// <inheritdoc/>
        public static bool operator ==(ByteString left, ByteString right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ByteString left, ByteString right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator <(ByteString left, ByteString right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ByteString left, ByteString right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(ByteString left, ByteString right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ByteString left, ByteString right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <inheritdoc/>
        public static bool operator ==(ByteString left, ReadOnlyMemory<byte> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ByteString left, ReadOnlyMemory<byte> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator <(ByteString left, ReadOnlyMemory<byte> right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ByteString left, ReadOnlyMemory<byte> right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(ByteString left, ReadOnlyMemory<byte> right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ByteString left, ReadOnlyMemory<byte> right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <inheritdoc/>
        public static bool operator ==(ByteString left, ReadOnlySpan<byte> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ByteString left, ReadOnlySpan<byte> right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator <(ByteString left, ReadOnlySpan<byte> right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ByteString left, ReadOnlySpan<byte> right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(ByteString left, ReadOnlySpan<byte> right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ByteString left, ReadOnlySpan<byte> right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <inheritdoc/>
        public static bool operator ==(ByteString left, byte[] right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ByteString left, byte[] right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator <(ByteString left, byte[] right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ByteString left, byte[] right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(ByteString left, byte[] right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ByteString left, byte[] right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <inheritdoc/>
        public static bool operator ==(ByteString left, ReadOnlySequence<byte> right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ByteString left, ReadOnlySequence<byte> right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns an empty ByteString.
        /// </summary>
        public static ByteString Empty => new(ReadOnlyMemory<byte>.Empty);

        /// <inheritdoc/>
        public static implicit operator ReadOnlyMemory<byte>(in ByteString bytes)
        {
            return bytes.m_memory;
        }

        /// <inheritdoc/>
        public static implicit operator ByteString(in ReadOnlySpan<byte> bytes)
        {
            return From(bytes.ToArray());
        }

        /// <inheritdoc/>
        public static implicit operator ByteString(in Span<byte> bytes)
        {
            return From(bytes.ToArray());
        }

        /// <inheritdoc/>
        public static explicit operator ByteString(byte[] bytes)
        {
            return From(bytes);
        }

        /// <summary>
        /// Copy memory into this byte string
        /// </summary>
        public static ByteString From(in ReadOnlyMemory<byte> bytes)
        {
            return From(bytes.ToArray());
        }

        /// <summary>
        /// Copy memory into this byte string
        /// </summary>
        public static ByteString From(in Memory<byte> bytes)
        {
            return new(bytes.ToArray());
        }

        /// <summary>
        /// Copy memory into this byte string
        /// </summary>
        public static ByteString From(in ReadOnlySequence<byte> bytes)
        {
            return new(bytes.ToArray());
        }

        /// <summary>
        /// Copy memory into this byte string
        /// </summary>
        public static ByteString From(byte[] bytes)
        {
            return new(bytes);
        }

        /// <summary>
        /// Slice the byte string
        /// </summary>
        public ByteString Slice(int start)
        {
            return new(m_memory[start..]);
        }

        /// <summary>
        /// Slice the byte string
        /// </summary>
        public ByteString Slice(int start, int length)
        {
            return new(m_memory.Slice(start, length));
        }

        /// <summary>
        /// Clone the byte string
        /// </summary>
        public ByteString Copy()
        {
            return new(m_memory.ToArray());
        }

        /// <summary>
        /// Constructs a <see cref="ByteString" /> from the given
        /// array. The contents are copied.
        /// </summary>
        public static ByteString Combine(params byte[] bytes)
        {
            return new((byte[])bytes.Clone());
        }

        /// <summary>
        /// Constructs a <see cref="ByteString" /> from the given
        /// bytes strings. The contents are copied.
        /// </summary>
        public static ByteString Combine(params ByteString[] bytes)
        {
            return Combine(1, bytes);
        }

        /// <summary>
        /// Constructs a <see cref="ByteString" /> from the given
        /// bytes strings. The contents are copied.
        /// </summary>
        public static ByteString Combine(int padToMultipleOf,
            params ByteString[] bytes)
        {
            int length = 0;
            foreach (ByteString item in bytes)
            {
                length += item.Length;
            }
            int partial = length % padToMultipleOf;
            if (partial != 0)
            {
                length += padToMultipleOf - partial;
            }
            byte[] buffer = new byte[length];
            Span<byte> dest = buffer.AsSpan();
            foreach (ByteString item in bytes)
            {
                item.Span.CopyTo(dest[..item.Length]);
                if (dest.Length == item.Length)
                {
                    break;
                }
                dest = dest[item.Length..];
            }
            return new ByteString(buffer);
        }

        /// <summary>
        /// Copies the entire byte array to the destination array
        /// provided at the offset specified.
        /// </summary>
        public void CopyTo(byte[] array, int position)
        {
            m_memory.Span.CopyTo(array.AsSpan(position));
        }

        /// <summary>
        /// Copies the entire byte array to the destination array
        /// provided at the offset specified.
        /// </summary>
        public void CopyTo(Span<byte> span)
        {
            m_memory.Span.CopyTo(span);
        }

        /// <summary>
        /// Copies the entire byte array to the writer.
        /// </summary>
        /// <typeparam name="TWriter"></typeparam>
        public void CopyTo<TWriter>(in TWriter writer)
            where TWriter : IBufferWriter<byte>
        {
            if (IsEmpty)
            {
                return;
            }
            CopyTo(writer.GetSpan(m_memory.Length));
            writer.Advance(m_memory.Length);
        }

        /// <summary>
        /// Copies the byte array as base64 string to the writer
        /// </summary>
        /// <typeparam name="TWriter"></typeparam>
        /// <param name="writer"></param>
        public void FormatAsBase64<TWriter>(in TWriter writer)
            where TWriter : IBufferWriter<byte>
        {
            if (IsEmpty)
            {
                return;
            }

            // TODO: Write in chunks
            Span<byte> buffer = writer.GetSpan(Base64.GetMaxEncodedToUtf8Length(Length));
            OperationStatus o = Base64.EncodeToUtf8(
                Span, buffer, out int consumed, out int written);
            Debug.Assert(o == OperationStatus.Done);
            Debug.Assert(consumed == Length);
            writer.Write(buffer[..written]);
        }

        /// <summary>
        /// Converts this <see cref="ByteString"/> into a byte array.
        /// Content is cloned if Sequence is not based on array.
        /// </summary>
        /// <returns>A byte array with the same data as this object.
        /// </returns>
        public byte[] ToArray()
        {
            return m_memory.ToArray();
        }

        /// <summary>
        /// Converts this <see cref="ByteString"/> into a standard
        /// base64 representation.
        /// </summary>
        /// <returns>A base64 representation of this object.</returns>
        public string ToBase64()
        {
#if NET8_0_OR_GREATER
            return Convert.ToBase64String(m_memory.Span);
#else
            return Convert.ToBase64String(m_memory.ToArray());
#endif
        }

        /// <summary>
        /// Convert from base64 string
        /// </summary>
        public static ByteString FromBase64(string base64)
        {
            return From(Convert.FromBase64String(base64));
        }

        /// <summary>
        /// Create a byte string from base64 encoded utf16 string
        /// </summary>
        public static bool TryFromBase64(string? strUnescaped, out ByteString bytes)
        {
            if (string.IsNullOrEmpty(strUnescaped))
            {
                bytes = Empty;
                return true;
            }
#if NET8_0_OR_GREATER
            byte[]? pooledArray = null;
            Span<byte> byteSpan = strUnescaped!.Length <= kStackLimit ?
                stackalloc byte[kStackLimit] :
                (pooledArray = ArrayPool<byte>.Shared.Rent(strUnescaped.Length));
            bool success;
            if (Convert.TryFromBase64String(strUnescaped, byteSpan, out int bytesWritten))
            {
                bytes = byteSpan[..bytesWritten];
                success = true;
            }
            else
            {
                bytes = default;
                success = false;
            }
            if (pooledArray != null)
            {
                byteSpan.Clear();
                ArrayPool<byte>.Shared.Return(pooledArray);
            }
            return success;
#else
            try
            {
                bytes = Convert.FromBase64String(strUnescaped!);
                return true;
            }
            catch (FormatException)
            {
                bytes = default;
                return false;
            }
#endif
        }

        /// <summary>
        /// Create a byte string from base64 encoded utf8
        /// </summary>
        public static bool TryFromBase64(ReadOnlySpan<byte> utf8Unescaped, out ByteString bytes)
        {
            if (utf8Unescaped.IsEmpty)
            {
                bytes = Empty;
                return true;
            }

            OperationStatus status;
            if (utf8Unescaped.Length <= kStackLimit)
            {
                Span<byte> byteSpan = stackalloc byte[utf8Unescaped.Length];
                status = Base64.DecodeFromUtf8(utf8Unescaped, byteSpan,
                    out int bytesConsumed, out int bytesWritten);
                bytes = byteSpan[..bytesWritten];
            }
            else
            {
                byte[] pooledArray = ArrayPool<byte>.Shared.Rent(utf8Unescaped.Length);
                status = Base64.DecodeFromUtf8(utf8Unescaped, pooledArray.AsSpan(),
                    out int bytesConsumed, out int bytesWritten);
                bytes = pooledArray.AsSpan()[..bytesWritten];
                ArrayPool<byte>.Shared.Return(pooledArray);
            }
            return status == OperationStatus.Done;
        }

        /// <summary>
        /// Create a byte string from base64 encoded utf8
        /// </summary>
        public static bool TryFromBase64(
            in ReadOnlySequence<byte> utf8Unescaped,
            out ByteString bytes)
        {
            if (utf8Unescaped.IsSingleSegment)
            {
#if NET8_0_OR_GREATER
                return TryFromBase64(utf8Unescaped.FirstSpan, out bytes);
#else
                return TryFromBase64(utf8Unescaped.First.Span, out bytes);
#endif
            }
            if (utf8Unescaped.IsEmpty)
            {
                bytes = Empty;
                return true;
            }
            OperationStatus status;
            int len = (int)utf8Unescaped.Length;
            if (len <= kStackLimit)
            {
                Span<byte> inplace = stackalloc byte[len];
                utf8Unescaped.CopyTo(inplace);
                status = Base64.DecodeFromUtf8InPlace(inplace, out int bytesWritten);
                bytes = inplace[..bytesWritten];
            }
            else
            {
                byte[] pooledArray = ArrayPool<byte>.Shared.Rent(len);
                utf8Unescaped.CopyTo(pooledArray.AsSpan());
                status = Base64.DecodeFromUtf8InPlace(pooledArray.AsSpan()[..len],
                    out int bytesWritten);
                bytes = pooledArray.AsSpan()[..bytesWritten];
                ArrayPool<byte>.Shared.Return(pooledArray);
            }
            return status == OperationStatus.Done;
        }

        /// <summary>
        /// Convert to hex string
        /// </summary>
        /// <returns>A hex representation of this object.</returns>
        public string ToHexString()
        {
#if NET8_0_OR_GREATER
            return CoreUtils.ToHexString(m_memory.Span);
#else
            return CoreUtils.ToHexString(m_memory.ToArray());
#endif
        }

        /// <summary>
        /// Convert from hex string
        /// </summary>
        public static ByteString FromHexString(string hexString)
        {
            return From(CoreUtils.FromHexString(hexString));
        }

        private const int kStackLimit = 64;
        private readonly ReadOnlyMemory<byte> m_memory;
    }

    /// <summary>
    /// A collection of ByteString values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfByteString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ByteString")]
    public class ByteStringCollection : List<ByteString>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ByteStringCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max size of collection</param>
        public ByteStringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of byte to add to this collection</param>
        public ByteStringCollection(IEnumerable<ByteString> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of bytes to return as a collection</param>
        public static ByteStringCollection ToByteStringCollection(ByteString[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of bytes to return as a collection</param>
        public static implicit operator ByteStringCollection(ByteString[] values)
        {
            return ToByteStringCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new ByteStringCollection(Count);

            foreach (ByteString element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }
}
