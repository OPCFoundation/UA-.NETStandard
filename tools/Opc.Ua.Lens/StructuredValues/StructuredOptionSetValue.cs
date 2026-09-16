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
using System.Collections.Generic;
using Opc.Ua;

namespace UaLens.StructuredValues;

/// <summary>
/// One named bit; validity is independent of its stored value.
/// </summary>
internal sealed record StructuredOptionBit(int BitIndex, string Name, bool IsSet, bool IsValid);

/// <summary>
/// A named-bit replacement. Omitted validity leaves the existing validity unchanged.
/// </summary>
internal sealed record StructuredOptionBitEdit(int BitIndex, bool IsSet, bool? IsValid = null);

/// <summary>
/// Retains the unsigned wire width or the native OptionSet body, including unnamed
/// bits and omitted validity masks. Edits never use the Int32 enumeration adapter.
/// </summary>
internal sealed class StructuredOptionSetValue
{
    public StructuredOptionSetValue(
        NodeId dataTypeId,
        EnumDefinition definition,
        Variant value,
        IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        if (!definition.IsOptionSet || value.IsNull || !value.TypeInfo.IsScalar)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "An OptionSet requires a scalar unsigned value or an OptionSet body.");
        }

        int requiredBytes = 0;
        foreach (EnumField field in definition.Fields)
        {
            if (field.Value is < 0 or > int.MaxValue || !m_indices.Add((int)field.Value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "OptionSet bit indexes must be unique, non-negative Int32 indexes.");
            }
            requiredBytes = Math.Max(requiredBytes, checked((int)(field.Value / 8) + 1));
        }

        StorageType = value.TypeInfo.BuiltInType;
        if (StorageType == BuiltInType.ExtensionObject)
        {
            if (!value.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out OptionSet? source, context) ||
                !StructuredValueDraft.SameType(source.TypeId, dataTypeId, context.NamespaceUris))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The native OptionSet body does not match its DataType.");
            }
            m_structure = CoreUtils.Clone(source)!;
            int valueLength = m_structure.Value.Length;
            int validLength = m_structure.ValidBits.Length;
            if (valueLength > 0 && validLength > 0 && valueLength != validLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "OptionSet Value and ValidBits must have the same byte width.");
            }
            int storedWidth = Math.Max(valueLength, validLength);
            if (storedWidth > 0 && storedWidth < requiredBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "A declared bit is outside the OptionSet's existing byte width.");
            }
            ByteLength = Math.Max(storedWidth, requiredBytes);
            if (ByteLength > MaximumByteLength ||
                (context.MaxByteStringLength > 0 && ByteLength > context.MaxByteStringLength))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The OptionSet exceeds the editor's byte capacity.");
            }
        }
        else
        {
            ByteLength = StorageType switch
            {
                BuiltInType.Byte => 1,
                BuiltInType.UInt16 => 2,
                BuiltInType.UInt32 => 4,
                BuiltInType.UInt64 => 8,
                _ => throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "Integer OptionSets must use Byte, UInt16, UInt32 or UInt64.")
            };
            if (requiredBytes > ByteLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "A declared bit is outside the OptionSet's unsigned wire width.");
            }
            m_number = ReadUnsigned(value);
        }

        Bits = definition.Fields.ConvertAll(field =>
        {
            int index = (int)field.Value;
            bool set = m_structure is null
                ? (m_number & (1UL << index)) != 0
                : GetBit(m_structure.Value, index);
            bool valid = m_structure is null || m_structure.ValidBits.IsEmpty ||
                GetBit(m_structure.ValidBits, index);
            return new StructuredOptionBit(
                index, field.Name ?? field.DisplayName.Text ?? $"Bit {index}", set, valid);
        });
    }

    public BuiltInType StorageType { get; }
    public int ByteLength { get; }
    public bool HasValidity => m_structure is not null;
    public ArrayOf<StructuredOptionBit> Bits { get; }

    public Variant WithBits(ArrayOf<StructuredOptionBitEdit> edits)
    {
        var seen = new HashSet<int>();
        ulong number = m_number;
        ByteString value = m_structure is null ? default : m_structure.Value;
        ByteString valid = m_structure is null ? default : m_structure.ValidBits;
        foreach (StructuredOptionBitEdit edit in edits)
        {
            if (!m_indices.Contains(edit.BitIndex) || !seen.Add(edit.BitIndex))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "Edit each declared OptionSet bit at most once.");
            }
            if (m_structure is null)
            {
                if (edit.IsValid.HasValue)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Unsigned OptionSets have no separate validity mask.");
                }
                ulong mask = 1UL << edit.BitIndex;
                number = edit.IsSet ? number | mask : number & ~mask;
            }
            else
            {
                value = WithBit(value, edit.BitIndex, edit.IsSet, implicitValidity: false);
                if (edit.IsValid is bool isValid)
                {
                    valid = WithBit(valid, edit.BitIndex, isValid, implicitValidity: true);
                }
            }
        }

        if (m_structure is null)
        {
            return StorageType switch
            {
                BuiltInType.Byte => Variant.From(checked((byte)number)),
                BuiltInType.UInt16 => Variant.From(checked((ushort)number)),
                BuiltInType.UInt32 => Variant.From(checked((uint)number)),
                BuiltInType.UInt64 => Variant.From(number),
                _ => throw new ServiceResultException(StatusCodes.BadTypeMismatch)
            };
        }

        OptionSet result = CoreUtils.Clone(m_structure)!;
        result.Value = value;
        result.ValidBits = valid;
        return Variant.FromStructure(result);
    }

    private static ulong ReadUnsigned(Variant value)
    {
        if (value.TryGetValue(out byte byteValue))
        {
            return byteValue;
        }
        if (value.TryGetValue(out ushort shortValue))
        {
            return shortValue;
        }
        if (value.TryGetValue(out uint intValue))
        {
            return intValue;
        }
        if (value.TryGetValue(out ulong longValue))
        {
            return longValue;
        }
        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The unsigned OptionSet cannot be decoded.");
    }

    private static bool GetBit(ByteString bytes, int index)
    {
        return index / 8 < bytes.Length && (bytes.Span[index / 8] & (1 << (index % 8))) != 0;
    }

    private ByteString WithBit(ByteString source, int index, bool set, bool implicitValidity)
    {
        bool omittedValidity = implicitValidity && source.IsEmpty;
        if ((omittedValidity || GetBit(source, index)) == set)
        {
            return source.Copy();
        }
        var bytes = new byte[ByteLength];
        if (omittedValidity)
        {
            // The native adapter treats an omitted mask as fully valid, including
            // unknown bits. Materializing it must not invalidate those bits.
            bytes.AsSpan().Fill(byte.MaxValue);
        }
        else
        {
            source.Span.CopyTo(bytes);
        }
        int mask = 1 << (index % 8);
        bytes[index / 8] = set ? (byte)(bytes[index / 8] | mask) : (byte)(bytes[index / 8] & ~mask);
        return ByteString.From(bytes);
    }

    public const int MaximumByteLength = 65536;

    private readonly HashSet<int> m_indices = [];
    private readonly OptionSet? m_structure;
    private readonly ulong m_number;
}
