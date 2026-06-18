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
using System.Globalization;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Discriminator for the value stored inside a
    /// <see cref="PublisherId"/>. Matches the on-wire PublisherId type
    /// bits of UADP ExtendedFlags1 plus the JSON-only Guid alternative
    /// allowed by the JSON mapping.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.2">
    /// Part 14 §7.2.4.5.2 PublisherId</see>.
    /// </remarks>
    public enum PublisherIdType
    {
        /// <summary>8-bit unsigned integer PublisherId.</summary>
        Byte,

        /// <summary>16-bit unsigned integer PublisherId.</summary>
        UInt16,

        /// <summary>32-bit unsigned integer PublisherId.</summary>
        UInt32,

        /// <summary>64-bit unsigned integer PublisherId.</summary>
        UInt64,

        /// <summary>UTF-8 string PublisherId.</summary>
        String,

        /// <summary>GUID PublisherId (JSON mapping only).</summary>
        Guid
    }

    /// <summary>
    /// Discriminated union modelling the OPC UA PubSub PublisherId. A
    /// PublisherId may be one of Byte / UInt16 / UInt32 / UInt64 / String
    /// / Guid; the chosen variant is selected by the
    /// <see cref="PubSubConnectionDataType.PublisherId"/> Variant at
    /// configuration time and is preserved through encode / decode so
    /// subscribers can match by structural equality.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.2">
    /// Part 14 §7.2.4.5.2 PublisherId</see>. The struct is a value type
    /// — never <see langword="null"/>; use <see cref="IsNull"/> to test
    /// for the unset sentinel.
    /// </remarks>
    public readonly record struct PublisherId
    {
        private readonly ulong m_numeric;
        private readonly string? m_string;
        private readonly Guid m_guid;

        private PublisherId(PublisherIdType type, ulong numeric, string? str, Guid guid)
        {
            Type = type;
            m_numeric = numeric;
            m_string = str;
            m_guid = guid;
        }

        /// <summary>
        /// Discriminator value identifying which payload field is
        /// populated.
        /// </summary>
        public PublisherIdType Type { get; }

        /// <summary>
        /// Sentinel for the unset / absent PublisherId. Treated as
        /// <see cref="PublisherIdType.UInt16"/> with value 0 — the wire
        /// default when ExtendedFlags1 PublisherId-enabled bit is clear.
        /// </summary>
        public static PublisherId Null { get; } = FromUInt16(0);

        /// <summary>
        /// <see langword="true"/> when this instance is the
        /// <see cref="Null"/> sentinel.
        /// </summary>
        public bool IsNull => Type == PublisherIdType.UInt16
            && m_numeric == 0
            && m_string == null
            && m_guid == Guid.Empty;

        /// <summary>
        /// Constructs a <see cref="PublisherId"/> from a
        /// <see cref="Variant"/> as carried by the configuration data
        /// types. Accepted scalar types: <see cref="byte"/>,
        /// <see cref="ushort"/>, <see cref="uint"/>, <see cref="ulong"/>,
        /// <see cref="string"/>, <see cref="Uuid"/>,
        /// <see cref="System.Guid"/>.
        /// </summary>
        /// <param name="value">Variant holding the PublisherId value.</param>
        /// <returns>The constructed PublisherId.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> holds an unsupported Built-In type.
        /// </exception>
        public static PublisherId From(Variant value)
        {
            if (value.IsNull)
            {
                return Null;
            }
            if (value.TryGetValue(out byte b))
            {
                return FromByte(b);
            }
            if (value.TryGetValue(out ushort u16))
            {
                return FromUInt16(u16);
            }
            if (value.TryGetValue(out uint u32))
            {
                return FromUInt32(u32);
            }
            if (value.TryGetValue(out ulong u64))
            {
                return FromUInt64(u64);
            }
            if (value.TryGetValue(out string str) && str != null)
            {
                return FromString(str);
            }
            if (value.TryGetValue(out Uuid uuid))
            {
                return FromGuid((Guid)uuid);
            }
            throw new ArgumentException(
                "PublisherId must hold one of Byte, UInt16, UInt32, UInt64, String, or Guid.",
                nameof(value));
        }

        /// <summary>
        /// Creates a Byte-typed PublisherId.
        /// </summary>
        public static PublisherId FromByte(byte value)
        {
            return new(PublisherIdType.Byte, value, null, Guid.Empty);
        }

        /// <summary>
        /// Creates a UInt16-typed PublisherId.
        /// </summary>
        public static PublisherId FromUInt16(ushort value)
        {
            return new(PublisherIdType.UInt16, value, null, Guid.Empty);
        }

        /// <summary>
        /// Creates a UInt32-typed PublisherId.
        /// </summary>
        public static PublisherId FromUInt32(uint value)
        {
            return new(PublisherIdType.UInt32, value, null, Guid.Empty);
        }

        /// <summary>
        /// Creates a UInt64-typed PublisherId.
        /// </summary>
        public static PublisherId FromUInt64(ulong value)
        {
            return new(PublisherIdType.UInt64, value, null, Guid.Empty);
        }

        /// <summary>
        /// Creates a String-typed PublisherId.
        /// </summary>
        public static PublisherId FromString(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return new PublisherId(PublisherIdType.String, 0, value, Guid.Empty);
        }

        /// <summary>
        /// Creates a Guid-typed PublisherId (JSON mapping).
        /// </summary>
        public static PublisherId FromGuid(Guid value)
        {
            return new(PublisherIdType.Guid, 0, null, value);
        }

        /// <summary>
        /// Converts the discriminated value back to a
        /// <see cref="Variant"/> for embedding in configuration objects.
        /// </summary>
        /// <returns>The PublisherId as a Variant.</returns>
        public Variant ToVariant()
        {
            return Type switch
            {
                PublisherIdType.Byte => new Variant((byte)m_numeric),
                PublisherIdType.UInt16 => new Variant((ushort)m_numeric),
                PublisherIdType.UInt32 => new Variant((uint)m_numeric),
                PublisherIdType.UInt64 => new Variant(m_numeric),
                PublisherIdType.String => new Variant(m_string ?? string.Empty),
                PublisherIdType.Guid => new Variant(new Uuid(m_guid)),
                _ => Variant.Null
            };
        }

        /// <summary>
        /// Tries to read the value as a <see cref="byte"/>.
        /// </summary>
        public bool TryGetByte(out byte value)
        {
            if (Type == PublisherIdType.Byte)
            {
                value = (byte)m_numeric;
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Tries to read the value as a <see cref="ushort"/>.
        /// </summary>
        public bool TryGetUInt16(out ushort value)
        {
            if (Type == PublisherIdType.UInt16)
            {
                value = (ushort)m_numeric;
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Tries to read the value as a <see cref="uint"/>.
        /// </summary>
        public bool TryGetUInt32(out uint value)
        {
            if (Type == PublisherIdType.UInt32)
            {
                value = (uint)m_numeric;
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Tries to read the value as a <see cref="ulong"/>.
        /// </summary>
        public bool TryGetUInt64(out ulong value)
        {
            if (Type == PublisherIdType.UInt64)
            {
                value = m_numeric;
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Tries to read the value as a <see cref="string"/>.
        /// </summary>
        public bool TryGetString(out string? value)
        {
            if (Type == PublisherIdType.String)
            {
                value = m_string;
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Tries to read the value as a <see cref="Guid"/>.
        /// </summary>
        public bool TryGetGuid(out Guid value)
        {
            if (Type == PublisherIdType.Guid)
            {
                value = m_guid;
                return true;
            }
            value = Guid.Empty;
            return false;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Type switch
            {
                PublisherIdType.Byte => m_numeric.ToString(CultureInfo.InvariantCulture),
                PublisherIdType.UInt16 => m_numeric.ToString(CultureInfo.InvariantCulture),
                PublisherIdType.UInt32 => m_numeric.ToString(CultureInfo.InvariantCulture),
                PublisherIdType.UInt64 => m_numeric.ToString(CultureInfo.InvariantCulture),
                PublisherIdType.String => m_string ?? string.Empty,
                PublisherIdType.Guid => m_guid.ToString("D", CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }
    }
}
