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

#if NET8_0
using System.Collections.Generic;
using System.Globalization;

namespace System.Text.Json
{
    /// <summary>
    /// Supplies exact JSON value comparison on the .NET 8 framework JSON library.
    /// </summary>
    internal static class JsonElementPolyfills
    {
        extension(JsonElement)
        {
            /// <summary>
            /// Compares JSON values, preserving array and repeated-property order
            /// while comparing numbers without rounding.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            public static bool DeepEquals(JsonElement element1, JsonElement element2)
            {
                var pending = new Stack<(JsonElement First, JsonElement Second)>();
                pending.Push((element1, element2));
                while (pending.TryPop(out (JsonElement First, JsonElement Second) pair))
                {
                    JsonValueKind firstKind = pair.First.ValueKind;
                    JsonValueKind secondKind = pair.Second.ValueKind;
                    if (firstKind == JsonValueKind.Undefined || secondKind == JsonValueKind.Undefined)
                    {
                        throw new InvalidOperationException("An undefined JSON value cannot be compared.");
                    }
                    if (firstKind != secondKind)
                    {
                        return false;
                    }
                    switch (firstKind)
                    {
                        case JsonValueKind.String:
                            if (!pair.First.ValueEquals(pair.Second.GetString()))
                            {
                                return false;
                            }
                            break;
                        case JsonValueKind.Number:
                            if (NormalizeNumber(pair.First) != NormalizeNumber(pair.Second))
                            {
                                return false;
                            }
                            break;
                        case JsonValueKind.Array:
                            if (pair.First.GetArrayLength() != pair.Second.GetArrayLength())
                            {
                                return false;
                            }
                            using (JsonElement.ArrayEnumerator first = pair.First.EnumerateArray())
                            using (JsonElement.ArrayEnumerator second = pair.Second.EnumerateArray())
                            {
                                while (first.MoveNext() && second.MoveNext())
                                {
                                    pending.Push((first.Current, second.Current));
                                }
                            }
                            break;
                        case JsonValueKind.Object:
                            if (!PairProperties(pair.First, pair.Second, pending))
                            {
                                return false;
                            }
                            break;
                    }
                }
                return true;
            }
        }

        private static bool PairProperties(
            JsonElement first, JsonElement second, Stack<(JsonElement First, JsonElement Second)> pending)
        {
            var right = new Dictionary<string, Queue<JsonElement>>(StringComparer.Ordinal);
            int rightCount = 0;
            foreach (JsonProperty property in second.EnumerateObject())
            {
                if (!right.TryGetValue(property.Name, out Queue<JsonElement>? values))
                {
                    values = new Queue<JsonElement>();
                    right.Add(property.Name, values);
                }
                values.Enqueue(property.Value);
                rightCount++;
            }
            int leftCount = 0;
            foreach (JsonProperty property in first.EnumerateObject())
            {
                if (!right.TryGetValue(property.Name, out Queue<JsonElement>? values) ||
                    !values.TryDequeue(out JsonElement value))
                {
                    return false;
                }
                pending.Push((property.Value, value));
                leftCount++;
            }
            return leftCount == rightCount;
        }

        private static JsonNumber NormalizeNumber(JsonElement value)
        {
            string text = value.GetRawText();
            int exponentStart = text.AsSpan().IndexOfAny('e', 'E');
            int exponent = exponentStart < 0 ? 0 : ParseExponent(text.AsSpan(exponentStart + 1));
            string mantissa = exponentStart < 0 ? text : text[..exponentStart];
            bool negative = mantissa[0] == '-';
            if (negative)
            {
                mantissa = mantissa[1..];
            }
            int point = mantissa.IndexOf('.', StringComparison.Ordinal);
            int fractionalDigits = point < 0 ? 0 : mantissa.Length - point - 1;
            string digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
            int start = 0;
            while (start < digits.Length && digits[start] == '0')
            {
                start++;
            }
            if (start == digits.Length)
            {
                return new JsonNumber(false, "0", 0);
            }
            int end = digits.Length - 1;
            while (digits[end] == '0')
            {
                end--;
            }
            long scale = (long)exponent - fractionalDigits + digits.Length - end - 1;
            return new JsonNumber(negative, digits[start..(end + 1)], scale);
        }

        private static int ParseExponent(ReadOnlySpan<char> exponent)
        {
            if (!int.TryParse(exponent, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                throw new ArgumentOutOfRangeException(nameof(exponent), "The JSON number exponent is too large.");
            }
            return value;
        }

        private readonly record struct JsonNumber(bool Negative, string Digits, long Scale);
    }
}
#endif
