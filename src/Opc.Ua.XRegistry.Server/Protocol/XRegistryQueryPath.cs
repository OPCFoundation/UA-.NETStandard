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
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    internal sealed record XRegistryQueryStep(string Name, int Index = -1, bool Wildcard = false, bool Array = false);

    internal static class XRegistryQueryPath
    {
        public static List<XRegistryQueryStep> Parse(string text, string error = "bad_filter")
        {
            var result = new List<XRegistryQueryStep>();
            int index = text.StartsWith('.') ? 1 : 0;
            while (index < text.Length)
            {
                if (text[index] == '[')
                {
                    index++;
                    if (index >= text.Length)
                    {
                        throw Invalid(error);
                    }
                    if (text[index] is '\'' or '"')
                    {
                        char quote = text[index++];
                        var name = new StringBuilder();
                        bool ended = false;
                        while (index < text.Length)
                        {
                            char character = text[index++];
                            if (character == quote)
                            {
                                ended = true;
                                break;
                            }
                            if (character == '\\')
                            {
                                if (index == text.Length)
                                {
                                    throw Invalid(error);
                                }
                                character = text[index++];
                            }
                            name.Append(character);
                        }
                        if (!ended || index == text.Length || text[index++] != ']')
                        {
                            throw Invalid(error);
                        }
                        result.Add(new XRegistryQueryStep(name.ToString()));
                    }
                    else
                    {
                        int end = text.IndexOf(']', index);
                        if (end < 0)
                        {
                            throw Invalid(error);
                        }
                        string value = text[index..end];
                        if (value == "*")
                        {
                            result.Add(new XRegistryQueryStep(string.Empty, Wildcard: true, Array: true));
                        }
                        else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                        {
                            result.Add(new XRegistryQueryStep(string.Empty, number, Array: true));
                        }
                        else
                        {
                            throw Invalid(error);
                        }
                        index = end + 1;
                    }
                }
                else
                {
                    int start = index;
                    while (index < text.Length && text[index] is not ('.' or '['))
                    {
                        if (text[index] is ']' or '\'' or '"' || char.IsWhiteSpace(text[index]))
                        {
                            throw Invalid(error);
                        }
                        index++;
                    }
                    if (index == start)
                    {
                        throw Invalid(error);
                    }
                    string name = text[start..index];
                    result.Add(new XRegistryQueryStep(name, Wildcard: name == "*"));
                }
                if (result.Count > 64)
                {
                    throw Invalid(error);
                }
                if (index < text.Length && text[index] == '.')
                {
                    index++;
                    if (index == text.Length || text[index] == '[')
                    {
                        throw Invalid(error);
                    }
                }
                else if (index < text.Length && text[index] != '[')
                {
                    throw Invalid(error);
                }
            }
            return result.Count == 0 ? throw Invalid(error) : result;
        }

        public static IEnumerable<JsonNode> Values(
            JsonNode? node, IReadOnlyList<XRegistryQueryStep> path, int start = 0)
        {
            if (node is null)
            {
                yield break;
            }
            if (start == path.Count)
            {
                yield return node;
                yield break;
            }
            XRegistryQueryStep step = path[start];
            if (step.Array)
            {
                if (node is not JsonArray array)
                {
                    yield break;
                }
                if (step.Wildcard)
                {
                    foreach (JsonNode? child in array)
                    {
                        foreach (JsonNode value in Values(child, path, start + 1))
                        {
                            yield return value;
                        }
                    }
                }
                else if (step.Index < array.Count)
                {
                    foreach (JsonNode value in Values(array[step.Index], path, start + 1))
                    {
                        yield return value;
                    }
                }
            }
            else if (node is JsonObject map)
            {
                if (step.Wildcard)
                {
                    foreach ((_, JsonNode? child) in map)
                    {
                        foreach (JsonNode value in Values(child, path, start + 1))
                        {
                            yield return value;
                        }
                    }
                }
                else
                {
                    foreach (JsonNode value in Values(map[step.Name], path, start + 1))
                    {
                        yield return value;
                    }
                }
            }
        }

        public static List<string> Split(string text, char delimiter, string error)
        {
            var parts = new List<string>();
            int start = 0;
            char quote = '\0';
            bool escaped = false;
            int brackets = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (value == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (quote != '\0')
                {
                    if (value == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (value is '\'' or '"' && brackets != 0)
                {
                    quote = value;
                }
                else if (value == '[')
                {
                    brackets++;
                }
                else if (value == ']')
                {
                    brackets--;
                }
                else if (value == delimiter && brackets == 0)
                {
                    parts.Add(text[start..index]);
                    start = index + 1;
                }
                if (brackets < 0)
                {
                    throw Invalid(error);
                }
            }
            if (quote != '\0' || brackets != 0)
            {
                throw Invalid(error);
            }
            parts.Add(text[start..]);
            return parts;
        }

        public static int Compare(JsonNode? first, JsonNode? second)
        {
            if (first is null || second is null)
            {
                return first is null ? second is null ? 0 : -1 : 1;
            }
            JsonValueKind kind = first.GetValueKind();
            if (kind != second.GetValueKind())
            {
                if (kind is JsonValueKind.True or JsonValueKind.False &&
                    second.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                {
                    return first.GetValue<bool>().CompareTo(second.GetValue<bool>());
                }
                return kind.CompareTo(second.GetValueKind());
            }
            return kind switch
            {
                JsonValueKind.Number => CompareNumbers(first.ToJsonString(), second.ToJsonString()),
                JsonValueKind.String => CultureInfo.GetCultureInfo("en-US").CompareInfo.Compare(
                    first.GetValue<string>(), second.GetValue<string>(), CompareOptions.IgnoreCase),
                JsonValueKind.True or JsonValueKind.False => 0,
                _ => throw new XRegistryRejectionException("bad_sort", "A sort key must have a scalar value.")
            };
        }

        public static int CompareNumbers(string first, string second)
        {
            (int firstSign, string firstDigits, BigInteger firstOrder) = Number(first);
            (int secondSign, string secondDigits, BigInteger secondOrder) = Number(second);
            if (firstSign != secondSign)
            {
                return firstSign.CompareTo(secondSign);
            }
            if (firstSign == 0)
            {
                return 0;
            }
            int order = firstOrder.CompareTo(secondOrder);
            if (order != 0)
            {
                return order * firstSign;
            }
            int length = Math.Max(firstDigits.Length, secondDigits.Length);
            for (int index = 0; index < length; index++)
            {
                char left = index < firstDigits.Length ? firstDigits[index] : '0';
                char right = index < secondDigits.Length ? secondDigits[index] : '0';
                if (left != right)
                {
                    return left.CompareTo(right) * firstSign;
                }
            }
            return 0;
        }

        public static bool WildcardMatch(string actual, string pattern)
        {
            var parts = new List<string>();
            var text = new StringBuilder();
            bool wildcard = false;
            for (int index = 0; index < pattern.Length; index++)
            {
                char value = pattern[index];
                if (value == '\\' && index + 1 < pattern.Length)
                {
                    text.Append(pattern[++index]);
                }
                else if (value == '*')
                {
                    parts.Add(text.ToString());
                    text.Clear();
                    wildcard = true;
                }
                else
                {
                    text.Append(value);
                }
            }
            parts.Add(text.ToString());
            if (!wildcard)
            {
                return string.Equals(actual, parts[0], StringComparison.OrdinalIgnoreCase);
            }
            int position = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                string part = parts[index];
                int found = actual.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
                if (found < 0 || (index == 0 && found != 0))
                {
                    return false;
                }
                if (index == parts.Count - 1)
                {
                    return actual.EndsWith(part, StringComparison.OrdinalIgnoreCase) &&
                        actual.Length - part.Length >= position;
                }
                position = found + part.Length;
            }
            return true;
        }

        private static (int Sign, string Digits, BigInteger Order) Number(string value)
        {
            int sign = value.StartsWith('-') ? -1 : 1;
            if (sign < 0)
            {
                value = value[1..];
            }
            int exponentAt = value.IndexOfAny(['e', 'E']);
            BigInteger exponent = exponentAt < 0 ? BigInteger.Zero :
                BigInteger.Parse(
                    value[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            string significand = exponentAt < 0 ? value : value[..exponentAt];
            int point = significand.IndexOf('.', StringComparison.Ordinal);
            int fraction = point < 0 ? 0 : significand.Length - point - 1;
            string digits = significand.Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0');
            return digits.Length == 0 ? (0, string.Empty, BigInteger.Zero)
                : (sign, digits, digits.Length - fraction + exponent);
        }

        private static XRegistryRejectionException Invalid(string error)
        {
            return new XRegistryRejectionException(error, "The attribute path or expression is malformed.");
        }
    }
}
