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
