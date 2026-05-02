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
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Opc.Ua.Encoders
{
    /// <summary>
    /// Runtime representation of a concrete Structure-backed
    /// sub-type of the abstract <see cref="Opc.Ua.OptionSet"/>
    /// DataType whose field semantics are described by an
    /// <see cref="EnumDefinition"/>.
    /// </summary>
    /// <remarks>
    /// The wire format (<c>Value</c> / <c>ValidBits</c>
    /// <see cref="ByteString"/>s) is inherited from the generated
    /// <see cref="Opc.Ua.OptionSet"/> base class. This class carries
    /// the concrete sub-type's TypeId / encoding ids and the
    /// bit-field metadata, and self-registers with
    /// <see cref="IEncodeableFactory"/> via
    /// <see cref="IEncodeableType"/>.
    /// </remarks>
    public sealed class OptionSet :
        global::Opc.Ua.OptionSet,
        IEncodeableType
    {
        /// <summary>
        /// Create a new OptionSet runtime type.
        /// </summary>
        public OptionSet(
            XmlQualifiedName xmlName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            EnumDefinition enumDefinition)
        {
            XmlName = xmlName ?? throw new ArgumentNullException(nameof(xmlName));
            Definition = enumDefinition ?? throw new ArgumentNullException(nameof(enumDefinition));
            m_typeId = typeId;
            m_binaryEncodingId = binaryEncodingId;
            m_xmlEncodingId = xmlEncodingId;
            m_byteLength = ComputeByteLength(enumDefinition);
        }

        private OptionSet(OptionSet source, bool copyValues)
        {
            XmlName = source.XmlName;
            Definition = source.Definition;
            m_typeId = source.m_typeId;
            m_binaryEncodingId = source.m_binaryEncodingId;
            m_xmlEncodingId = source.m_xmlEncodingId;
            m_byteLength = source.m_byteLength;
            if (copyValues)
            {
                Value = source.Value.Copy();
                ValidBits = source.ValidBits.Copy();
            }
        }

        /// <inheritdoc/>
        public Type Type => typeof(OptionSet);

        /// <inheritdoc/>
        public XmlQualifiedName XmlName { get; }

        /// <summary>
        /// The bit-field definition of this concrete OptionSet sub-type.
        /// Each <see cref="EnumField"/>'s Value is the bit index (0-based).
        /// </summary>
        public EnumDefinition Definition { get; }

        /// <summary>
        /// The fixed byte length of the <see cref="Opc.Ua.OptionSet.Value"/>
        /// and <see cref="Opc.Ua.OptionSet.ValidBits"/> ByteStrings for this
        /// OptionSet sub-type, derived from the highest bit index declared
        /// in <see cref="Definition"/>.
        /// </summary>
        /// <remarks>
        /// Per OPC UA Part 3 §8.40 / §3.2.8: OptionSet sub-types may add new
        /// Bits but must not change the overall length of the underlying
        /// ByteStrings. This value is therefore fixed at construction; bits
        /// outside the range <c>[0, ByteLength*8)</c> cannot be set.
        /// </remarks>
        public int ByteLength => m_byteLength;

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => m_typeId;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => m_binaryEncodingId;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => m_xmlEncodingId;

        /// <inheritdoc/>
        public IEncodeable CreateInstance()
        {
            return new OptionSet(this, copyValues: false);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return new OptionSet(this, copyValues: true);
        }

        /// <summary>
        /// Gets or sets the bit corresponding to the given field name
        /// (as declared in <see cref="Definition"/>).
        /// </summary>
        public bool this[string fieldName]
        {
            get
            {
                if (!TryGetBitIndex(fieldName, out int bit))
                {
                    throw new ArgumentException(
                        CoreUtils.Format("Unknown OptionSet field '{0}'.", fieldName),
                        nameof(fieldName));
                }
                return GetBit(Value.Span, bit);
            }
            set
            {
                if (!TryGetBitIndex(fieldName, out int bit))
                {
                    throw new ArgumentException(
                        CoreUtils.Format("Unknown OptionSet field '{0}'.", fieldName),
                        nameof(fieldName));
                }
                SetBit(bit, value);
            }
        }

        /// <summary>
        /// Gets or sets the bit at the specified bit index.
        /// </summary>
        public bool this[int bit]
        {
            get => GetBit(Value.Span, bit);
            set => SetBit(bit, value);
        }

        /// <summary>
        /// Returns the names of all bits that are set and marked
        /// valid according to <see cref="Opc.Ua.OptionSet.ValidBits"/>.
        /// If <see cref="Opc.Ua.OptionSet.ValidBits"/> is empty the
        /// OptionSet is treated as fully valid.
        /// </summary>
        public IReadOnlyList<string> GetSetFieldNames()
        {
            var names = new List<string>();
            if (Definition.Fields.IsEmpty)
            {
                return names;
            }
            ReadOnlySpan<byte> value = Value.Span;
            ReadOnlySpan<byte> valid = ValidBits.Span;
            bool validOmitted = valid.IsEmpty;
            foreach (EnumField field in Definition.Fields)
            {
                int bit = (int)field.Value;
                if (bit < 0)
                {
                    continue;
                }
                if (GetBit(value, bit) && (validOmitted || GetBit(valid, bit)))
                {
                    names.Add(field.Name);
                }
            }
            return names;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder(XmlName?.Name ?? "OptionSet").Append(" {");
            bool first = true;
            foreach (string name in GetSetFieldNames())
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                sb.Append(name);
                first = false;
            }
            return sb.Append('}').ToString();
        }

        private bool TryGetBitIndex(string fieldName, out int bit)
        {
            if (!string.IsNullOrEmpty(fieldName) && !Definition.Fields.IsEmpty)
            {
                foreach (EnumField field in Definition.Fields)
                {
                    if (field.Name == fieldName)
                    {
                        bit = (int)field.Value;
                        return bit >= 0;
                    }
                }
            }
            bit = -1;
            return false;
        }

        private static bool GetBit(ReadOnlySpan<byte> bytes, int bit)
        {
            if (bit < 0)
            {
                return false;
            }
            int byteIndex = bit >> 3;
            if ((uint)byteIndex >= (uint)bytes.Length)
            {
                return false;
            }
            return (bytes[byteIndex] & (1 << (bit & 7))) != 0;
        }

        private void SetBit(int bit, bool on)
        {
            if (bit < 0 || bit >= m_byteLength * 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bit),
                    CoreUtils.Format(
                        "Bit index {0} is outside the fixed {1}-byte OptionSet length. "
                        + "OPC UA Part 3 §8.40 requires that sub-types do not change the overall length.",
                        bit,
                        m_byteLength));
            }
            int byteIndex = bit >> 3;
            int mask = 1 << (bit & 7);

            Value = WithBit(Value, byteIndex, mask, on, m_byteLength);
            // Setting a bit implicitly marks the bit valid.
            ValidBits = WithBit(ValidBits, byteIndex, mask, true, m_byteLength);
        }

        private static ByteString WithBit(ByteString source, int byteIndex, int mask, bool on, int fixedLength)
        {
            byte[] buffer = new byte[fixedLength];
            if (!source.IsEmpty)
            {
                int copyLength = Math.Min(source.Length, fixedLength);
                source.Span.Slice(0, copyLength).CopyTo(buffer);
            }
            if (on)
            {
                buffer[byteIndex] = (byte)(buffer[byteIndex] | mask);
            }
            else
            {
                buffer[byteIndex] = (byte)(buffer[byteIndex] & ~mask);
            }
            return ByteString.From(buffer);
        }

        private static int ComputeByteLength(EnumDefinition definition)
        {
            if (definition.Fields.IsEmpty)
            {
                return 0;
            }
            long maxBit = -1;
            foreach (EnumField field in definition.Fields)
            {
                if (field.Value > maxBit)
                {
                    maxBit = field.Value;
                }
            }
            if (maxBit < 0)
            {
                return 0;
            }
            return (int)((maxBit >> 3) + 1);
        }

        private readonly int m_byteLength;

        private readonly ExpandedNodeId m_typeId;
        private readonly ExpandedNodeId m_binaryEncodingId;
        private readonly ExpandedNodeId m_xmlEncodingId;
    }
}
