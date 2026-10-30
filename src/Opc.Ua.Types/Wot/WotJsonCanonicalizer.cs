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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Retains the internal WoT entry points while all canonicalization uses the shared implementation.
    /// </summary>
    internal static class WotJsonCanonicalizer
    {
        public const int MaxDepth = JsonCanonicalizer.MaxDepth;

        public static bool TryCanonicalize(JsonElement value, out string canonical, out string error)
        {
            return JsonCanonicalizer.TryCanonicalize(value, out canonical, out error);
        }

        public static bool TryCanonicalize(JsonNode? value, out string canonical, out string error)
        {
            return JsonCanonicalizer.TryCanonicalize(value, out canonical, out error);
        }

        public static bool TryGetUtf8(JsonElement value, out byte[] bytes, out string error)
        {
            bool result = JsonCanonicalizer.TryGetUtf8(value, out ByteString canonical, out error);
            bytes = canonical.ToArray();
            return result;
        }

        public static bool TryEquals(JsonNode? left, JsonNode? right, out bool equal, out string error)
        {
            return JsonCanonicalizer.TryEquals(left, right, out equal, out error);
        }

        public static bool TryFormatNumber(string literal, out string formatted, out string error)
        {
            return JsonCanonicalizer.TryFormatNumber(literal, out formatted, out error);
        }

        public static void AppendString(StringBuilder text, string value)
        {
            JsonCanonicalizer.AppendString(text, value);
        }

        internal static void WriteNumber(Utf8JsonWriter writer, string propertyName, double value)
        {
            JsonCanonicalizer.WriteNumber(writer, propertyName, value);
        }

        internal static void WriteNumberValue(Utf8JsonWriter writer, double value)
        {
            JsonCanonicalizer.WriteNumberValue(writer, value);
        }
    }
}
