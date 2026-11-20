/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.StructuredValues;

/// <summary>
/// Scalar text editing without boxing. Unchanged text preserves typed nulls and
/// metadata such as a LocalizedText locale instead of reparsing a display string.
/// </summary>
internal static class StructuredScalarValue
{
    public static string FormatValue(Variant value, IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (value.IsNull)
        {
            return "(null)";
        }
        if (value.TypeInfo.IsScalar)
        {
            return Format(value);
        }
        ArrayOf<string> elements = StructuredArrayValue.Read(value, context).Elements.ConvertAll(Format);
        return $"[{string.Join(", ", elements.ToArray() ?? [])}]";
    }

    public static string Format(Variant value)
    {
        if (value.IsNull)
        {
            return string.Empty;
        }
        if (value.TypeInfo.IsScalar)
        {
            if (value.TryGetValue(out string text))
            {
                return text ?? string.Empty;
            }
            if (value.TryGetValue(out LocalizedText localized))
            {
                return localized.Text ?? string.Empty;
            }
            if (value.TryGetValue(out ByteString bytes))
            {
                return bytes.IsNull ? string.Empty : Convert.ToBase64String(bytes.Span);
            }
            if (value.TryGetValue(out DateTimeUtc time))
            {
                return time.ToString("O", CultureInfo.InvariantCulture);
            }
            if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration &&
                value.TryGetValue(out int number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
        }
        return value.ToString(null, CultureInfo.InvariantCulture);
    }

    public static bool TryParse(
        BuiltInType builtInType,
        string text,
        Variant original,
        out Variant value,
        out string? error)
    {
        error = null;
        if ((!original.IsNull || builtInType == BuiltInType.Variant) &&
            string.Equals(text, Format(original), StringComparison.Ordinal))
        {
            value = original.Copy();
            return true;
        }
        if (builtInType == BuiltInType.String)
        {
            value = Variant.From(text);
            return true;
        }
        if (builtInType == BuiltInType.LocalizedText)
        {
            original.TryGetValue(out LocalizedText localized);
            value = Variant.From(new LocalizedText(localized.Locale, text));
            return true;
        }
        if (builtInType == BuiltInType.Variant && !original.IsNull)
        {
            builtInType = original.TypeInfo.BuiltInType;
        }
        NodeId dataType = builtInType == BuiltInType.Enumeration
            ? DataTypeIds.Int32
            : new NodeId((uint)builtInType);
        return VariantParser.TryParse(dataType, ValueRanks.Scalar, text, out value, out error);
    }
}
