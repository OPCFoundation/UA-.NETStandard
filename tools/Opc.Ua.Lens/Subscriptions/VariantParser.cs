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
using System.Globalization;
using Opc.Ua;

namespace UaLens.Subscriptions;

/// <summary>
/// Lightweight string → <see cref="Variant"/> parser used by the Method
/// Call and Write Value dialogs.  Covers the most common built-in
/// scalars and 1-D arrays.  Anything more complex (extension objects,
/// matrices, …) returns <c>false</c> with an error message — callers
/// surface the message inline so the user can correct the input.
/// </summary>
internal static class VariantParser
{
    private static readonly char[] s_arraySeparators = [',', '\n', '\r'];

    public static bool TryParse(NodeId dataType, int valueRank, string text,
        out Variant result, out string? error)
    {
        result = Variant.Null;
        error = null;
        if (dataType.IsNull)
        {
            error = "DataType is null.";
            return false;
        }

        // Resolve the built-in type via the data-type id.  Only namespace 0
        // numeric ids map to built-ins; anything else we can't handle here.
        BuiltInType bi = BuiltInTypeForDataType(dataType);
        if (bi == BuiltInType.Null)
        {
            error = $"Unsupported DataType: {dataType}";
            return false;
        }

        // Scalar.
        if (valueRank == ValueRanks.Scalar
            || valueRank == ValueRanks.ScalarOrOneDimension
            || valueRank == ValueRanks.Any)
        {
            return TryParseScalar(bi, text.Trim(), out result, out error);
        }

        // 1-D array.
        if (valueRank == ValueRanks.OneDimension
            || valueRank == ValueRanks.OneOrMoreDimensions)
        {
            return TryParseArray(bi, text, out result, out error);
        }

        error = $"ValueRank {valueRank} is not supported by this dialog.";
        return false;
    }

    private static bool TryParseScalar(BuiltInType bi, string s,
        out Variant result, out string? error)
    {
        result = Variant.Null;
        error = null;
        try
        {
            switch (bi)
            {
                case BuiltInType.Boolean:
                    if (!bool.TryParse(s, out bool b))
                    {
                        error = "Expected 'true' or 'false'.";
                        return false;
                    }
                    result = Variant.From(b);
                    return true;
                case BuiltInType.SByte:
                    result = Variant.From(sbyte.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Byte:
                    result = Variant.From(byte.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Int16:
                    result = Variant.From(short.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.UInt16:
                    result = Variant.From(ushort.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Int32:
                    result = Variant.From(int.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.UInt32:
                    result = Variant.From(uint.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Int64:
                    result = Variant.From(long.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.UInt64:
                    result = Variant.From(ulong.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Float:
                    result = Variant.From(float.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.Double:
                    result = Variant.From(double.Parse(s, CultureInfo.InvariantCulture));
                    return true;
                case BuiltInType.String:
                    result = Variant.From(s);
                    return true;
                case BuiltInType.DateTime:
                    result = Variant.From(new DateTimeUtc(DateTime.Parse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
                    return true;
                case BuiltInType.NodeId:
                    result = Variant.From(NodeId.Parse(s));
                    return true;
                case BuiltInType.Guid:
                    result = Variant.From(new Uuid(Guid.Parse(s)));
                    return true;
                case BuiltInType.QualifiedName:
                    result = Variant.From(QualifiedName.Parse(s));
                    return true;
                case BuiltInType.LocalizedText:
                    result = Variant.From(new LocalizedText(s));
                    return true;
                case BuiltInType.ByteString:
                    result = Variant.From(ParseByteString(s));
                    return true;
                default:
                    error = $"BuiltInType {bi} is not supported by this dialog.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Cannot parse '{s}' as {bi}: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseArray(BuiltInType bi, string text,
        out Variant result, out string? error)
    {
        result = Variant.Null;
        error = null;
        // Accept "[a, b, c]" or "a, b, c" or whitespace/newline separated.
        string trimmed = text.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        string[] parts = trimmed.Split(s_arraySeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            // Empty array of the right element type.
            return TryEmptyArray(bi, out result, out error);
        }
        // Build a strongly-typed array based on bi.  Variant.From only
        // accepts ArrayOf<T> for arrays — the implicit conversion from
        // T[] to ArrayOf<T> handles the bridging.
        try
        {
            switch (bi)
            {
                case BuiltInType.Boolean:
                    return BuildArr<bool>(parts, bool.Parse, out result, out error);
                case BuiltInType.SByte:
                    return BuildArr<sbyte>(parts, s => sbyte.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Byte:
                    return BuildArr<byte>(parts, s => byte.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Int16:
                    return BuildArr<short>(parts, s => short.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.UInt16:
                    return BuildArr<ushort>(parts, s => ushort.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Int32:
                    return BuildArr<int>(parts, s => int.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.UInt32:
                    return BuildArr<uint>(parts, s => uint.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Int64:
                    return BuildArr<long>(parts, s => long.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.UInt64:
                    return BuildArr<ulong>(parts, s => ulong.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Float:
                    return BuildArr<float>(parts, s => float.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.Double:
                    return BuildArr<double>(parts, s => double.Parse(s, CultureInfo.InvariantCulture), out result, out error);
                case BuiltInType.String:
                {
                    ArrayOf<string> arr = parts;
                    result = Variant.From(arr);
                    return true;
                }
                case BuiltInType.DateTime:
                {
                    var arr = new DateTimeUtc[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        arr[i] = new DateTimeUtc(DateTime.Parse(parts[i],
                            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
                    }
                    result = Variant.From((ArrayOf<DateTimeUtc>)arr);
                    return true;
                }
                case BuiltInType.NodeId:
                {
                    var arr = new NodeId[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        arr[i] = NodeId.Parse(parts[i]);
                    }
                    result = Variant.From((ArrayOf<NodeId>)arr);
                    return true;
                }
                case BuiltInType.Guid:
                {
                    var arr = new Uuid[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        arr[i] = new Uuid(Guid.Parse(parts[i]));
                    }
                    result = Variant.From((ArrayOf<Uuid>)arr);
                    return true;
                }
                default:
                    error = $"BuiltInType {bi} arrays are not supported by this dialog.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Failed to parse array: {ex.Message}";
            return false;
        }
    }

    private static bool BuildArr<T>(string[] parts, Func<string, T> conv,
        out Variant result, out string? error) where T : struct
    {
        var arr = new T[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            arr[i] = conv(parts[i]);
        }
        // ArrayOf<T> has implicit conversion from T[]; Variant.From has
        // overloads for every numeric ArrayOf<T>.
        result = (T[])arr switch
        {
            bool[] b => Variant.From((ArrayOf<bool>)b),
            sbyte[] sb => Variant.From((ArrayOf<sbyte>)sb),
            byte[] bt => Variant.From((ArrayOf<byte>)bt),
            short[] sh => Variant.From((ArrayOf<short>)sh),
            ushort[] us => Variant.From((ArrayOf<ushort>)us),
            int[] ia => Variant.From((ArrayOf<int>)ia),
            uint[] ua => Variant.From((ArrayOf<uint>)ua),
            long[] la => Variant.From((ArrayOf<long>)la),
            ulong[] ula => Variant.From((ArrayOf<ulong>)ula),
            float[] fa => Variant.From((ArrayOf<float>)fa),
            double[] da => Variant.From((ArrayOf<double>)da),
            _ => Variant.Null
        };
        if (result.IsNull)
        {
            error = $"Unsupported array element type {typeof(T).Name}.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryEmptyArray(BuiltInType bi, out Variant result, out string? error)
    {
        error = null;
        result = bi switch
        {
            BuiltInType.Boolean => Variant.From((ArrayOf<bool>)Array.Empty<bool>()),
            BuiltInType.SByte => Variant.From((ArrayOf<sbyte>)Array.Empty<sbyte>()),
            BuiltInType.Byte => Variant.From((ArrayOf<byte>)Array.Empty<byte>()),
            BuiltInType.Int16 => Variant.From((ArrayOf<short>)Array.Empty<short>()),
            BuiltInType.UInt16 => Variant.From((ArrayOf<ushort>)Array.Empty<ushort>()),
            BuiltInType.Int32 => Variant.From((ArrayOf<int>)Array.Empty<int>()),
            BuiltInType.UInt32 => Variant.From((ArrayOf<uint>)Array.Empty<uint>()),
            BuiltInType.Int64 => Variant.From((ArrayOf<long>)Array.Empty<long>()),
            BuiltInType.UInt64 => Variant.From((ArrayOf<ulong>)Array.Empty<ulong>()),
            BuiltInType.Float => Variant.From((ArrayOf<float>)Array.Empty<float>()),
            BuiltInType.Double => Variant.From((ArrayOf<double>)Array.Empty<double>()),
            BuiltInType.String => Variant.From((ArrayOf<string>)Array.Empty<string>()),
            BuiltInType.DateTime => Variant.From((ArrayOf<DateTimeUtc>)Array.Empty<DateTimeUtc>()),
            BuiltInType.NodeId => Variant.From((ArrayOf<NodeId>)Array.Empty<NodeId>()),
            BuiltInType.Guid => Variant.From((ArrayOf<Uuid>)Array.Empty<Uuid>()),
            _ => Variant.Null
        };
        if (result.IsNull)
        {
            error = $"Cannot construct empty array of {bi}.";
            return false;
        }
        return true;
    }

    private static BuiltInType BuiltInTypeForDataType(NodeId id)
    {
        if (id.NamespaceIndex != 0 || id.IdType != IdType.Numeric)
        {
            return BuiltInType.Null;
        }
        // IdType.Numeric guarantees Identifier is a non-null boxed uint.
        uint idValue = (uint)id.Identifier!;
        // Map well-known DataType node ids to built-in types.  This list
        // mirrors Opc.Ua.DataTypeIds for the values we support.
        return idValue switch
        {
            DataTypes.Boolean => BuiltInType.Boolean,
            DataTypes.SByte => BuiltInType.SByte,
            DataTypes.Byte => BuiltInType.Byte,
            DataTypes.Int16 => BuiltInType.Int16,
            DataTypes.UInt16 => BuiltInType.UInt16,
            DataTypes.Int32 => BuiltInType.Int32,
            DataTypes.UInt32 => BuiltInType.UInt32,
            DataTypes.Int64 => BuiltInType.Int64,
            DataTypes.UInt64 => BuiltInType.UInt64,
            DataTypes.Float => BuiltInType.Float,
            DataTypes.Double => BuiltInType.Double,
            DataTypes.String => BuiltInType.String,
            DataTypes.DateTime => BuiltInType.DateTime,
            DataTypes.NodeId => BuiltInType.NodeId,
            DataTypes.Guid => BuiltInType.Guid,
            DataTypes.QualifiedName => BuiltInType.QualifiedName,
            DataTypes.LocalizedText => BuiltInType.LocalizedText,
            DataTypes.ByteString => BuiltInType.ByteString,
            _ => BuiltInType.Null
        };
    }

    /// <summary>
    /// Parses a string into a <see cref="ByteString"/>.  Accepts a
    /// hex sequence (with optional <c>0x</c> prefix, spaces or dashes
    /// between bytes) or falls back to a base-64 decode.
    /// </summary>
    private static ByteString ParseByteString(string text)
    {
        string raw = text.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(2);
        }
        string hex = raw.Replace(" ", string.Empty, StringComparison.Ordinal)
                        .Replace("-", string.Empty, StringComparison.Ordinal)
                        .Replace(":", string.Empty, StringComparison.Ordinal);
        if (hex.Length > 0 && hex.Length % 2 == 0 && IsHex(hex))
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return (ByteString)bytes;
        }
        // Fall back to base64.
        return (ByteString)Convert.FromBase64String(raw);
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }
}
