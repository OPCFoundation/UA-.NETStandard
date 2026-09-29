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
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Lossless JSON compatibility for the native xRegistry value family.
    /// Numbers never pass through a floating-point or fixed-precision decimal value.
    /// </summary>
    public static class RegistryValues
    {
        /// <summary>
        /// Parses a UTF-8 JSON document into exact native values.
        /// Duplicate members, invalid Unicode and values outside the bounds are rejected.
        /// </summary>
        public static RegistryValueDataType Parse(
            ReadOnlySpan<byte> json,
            int maxDepth = 128,
            int maxValues = 100000)
        {
            var budget = new Budget(maxDepth, maxValues);
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    json.ToArray(), new JsonDocumentOptions { MaxDepth = maxDepth });
                return Read(document.RootElement, budget, 0);
            }
            catch (JsonException error)
            {
                throw new ArgumentException("Invalid registry JSON or nesting limit exceeded.", nameof(json), error);
            }
        }

        /// <summary>
        /// Serializes native values without losing numeric form, array order or member presence.
        /// Incidental source whitespace and escape spelling are not reproduced.
        /// </summary>
        public static ByteString ToJson(
            RegistryValueDataType value,
            int maxDepth = 128,
            int maxValues = 100000)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                Write(value, writer, new Budget(maxDepth, maxValues), 0);
            }
            return ByteString.From(stream.ToArray());
        }

        /// <summary>
        /// Checks committed value identity, including integral form, scale and negative zero.
        /// Array order matters; object member order does not.
        /// </summary>
        public static bool Identical(RegistryValueDataType first, RegistryValueDataType second)
        {
            if (first is null)
            {
                throw new ArgumentNullException(nameof(first));
            }
            if (second is null)
            {
                throw new ArgumentNullException(nameof(second));
            }
            Validate(first);
            Validate(second);
            return EqualValue(first, second);
        }

        /// <summary>
        /// Validates discriminators, exact numbers, unique names, Unicode and traversal bounds.
        /// </summary>
        public static void Validate(
            RegistryValueDataType value,
            int maxDepth = 128,
            int maxValues = 100000)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            Write(value, null, new Budget(maxDepth, maxValues), 0);
        }

        /// <summary>
        /// Applies a complete batch to a copy of a native value.
        /// Set of RegistryNullValueDataType stores null; Remove deletes the selected member.
        /// Missing parents, invalid selectors and inconsistent operations reject the entire batch.
        /// </summary>
        public static RegistryValueDataType ApplyChanges(
            RegistryValueDataType value,
            ArrayOf<RegistryChangeDataType> changes,
            int maxDepth = 128,
            int maxValues = 100000)
        {
            Validate(value, maxDepth, maxValues);
            if (changes.Count > maxValues)
            {
                throw new ArgumentException("The native change batch exceeds the operation limit.", nameof(changes));
            }
            var result = (RegistryValueDataType)value.Clone();
            foreach (RegistryChangeDataType change in changes)
            {
                if (change is null || change.Operation > 1 || change.Path.Count == 0 ||
                    change.Path.Count > maxDepth)
                {
                    throw new ArgumentException("Invalid native change operation or path.", nameof(changes));
                }
                if (change.Operation == 0)
                {
                    Validate(change.Value ?? throw new ArgumentException(
                        "Set requires a native value; use RegistryNullValueDataType for an explicit null."),
                        maxDepth, maxValues);
                }
                else if (change.Value is not null)
                {
                    throw new ArgumentException("Remove carries a null ExtensionObject, not an explicit null value.",
                        nameof(changes));
                }
                RegistryValueDataType parent = result;
                for (int depth = 0; depth < change.Path.Count - 1; depth++)
                {
                    parent = SelectChild(parent, change.Path[depth]);
                }
                RegistryPathElementDataType selector = change.Path[^1];
                ValidateSelector(selector);
                if (selector.Kind == 0 && parent is RegistryObjectValueDataType map)
                {
                    var members = new List<RegistryMemberDataType>();
                    foreach (RegistryMemberDataType member in map.Members)
                    {
                        members.Add(member);
                    }
                    int index = members.FindIndex(member =>
                        string.Equals(member.Name, selector.Name, StringComparison.Ordinal));
                    if (change.Operation == 1)
                    {
                        if (index < 0)
                        {
                            throw new ArgumentException("Cannot remove a missing object member.", nameof(changes));
                        }
                        members.RemoveAt(index);
                    }
                    else
                    {
                        var replacement = new RegistryMemberDataType
                        {
                            Name = selector.Name,
                            Value = (RegistryValueDataType)change.Value!.Clone()
                        };
                        if (index < 0)
                        {
                            members.Add(replacement);
                        }
                        else
                        {
                            members[index] = replacement;
                        }
                    }
                    map.Members = members.ToArray();
                }
                else if (selector.Kind == 1 && parent is RegistryArrayValueDataType array &&
                    selector.Index < array.Items.Count)
                {
                    var items = new List<RegistryValueDataType>();
                    foreach (RegistryValueDataType item in array.Items)
                    {
                        items.Add(item);
                    }
                    if (change.Operation == 1)
                    {
                        items.RemoveAt((int)selector.Index);
                    }
                    else
                    {
                        items[(int)selector.Index] = (RegistryValueDataType)change.Value!.Clone();
                    }
                    array.Items = items.ToArray();
                }
                else
                {
                    throw new ArgumentException("The selector does not address an existing native container.",
                        nameof(changes));
                }
                Validate(result, maxDepth, maxValues);
            }
            return result;
        }

        private static RegistryValueDataType SelectChild(
            RegistryValueDataType parent,
            RegistryPathElementDataType selector)
        {
            ValidateSelector(selector);
            if (selector.Kind == 0 && parent is RegistryObjectValueDataType map)
            {
                foreach (RegistryMemberDataType member in map.Members)
                {
                    if (string.Equals(member.Name, selector.Name, StringComparison.Ordinal))
                    {
                        return member.Value;
                    }
                }
            }
            else if (selector.Kind == 1 && parent is RegistryArrayValueDataType array &&
                selector.Index < array.Items.Count)
            {
                return array.Items[(int)selector.Index];
            }
            throw new ArgumentException("The native path has a missing or incompatible parent.", nameof(selector));
        }

        private static void ValidateSelector(RegistryPathElementDataType selector)
        {
            if (selector is null || selector.Kind > 1 ||
                (selector.Kind == 0 && selector.Index != 0) ||
                (selector.Kind == 1 && !string.IsNullOrEmpty(selector.Name)))
            {
                throw new ArgumentException("Invalid native member/index selector.", nameof(selector));
            }
            if (selector.Kind == 0)
            {
                Unicode(selector.Name);
            }
        }

        private static RegistryValueDataType Read(JsonElement element, Budget budget, int depth)
        {
            budget.Take(depth);
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return new RegistryNullValueDataType { Kind = 0 };
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new RegistryBooleanValueDataType { Kind = 1, Value = element.GetBoolean() };
                case JsonValueKind.String:
                    return new RegistryStringValueDataType { Kind = 2, Value = Unicode(element.GetString()!) };
                case JsonValueKind.Number:
                    return ReadNumber(element.GetRawText());
                case JsonValueKind.Array:
                    var items = new List<RegistryValueDataType>();
                    foreach (JsonElement child in element.EnumerateArray())
                    {
                        items.Add(Read(child, budget, depth + 1));
                    }
                    return new RegistryArrayValueDataType { Kind = 4, Items = items.ToArray() };
                case JsonValueKind.Object:
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    var members = new List<RegistryMemberDataType>();
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        string name = Unicode(member.Name);
                        if (!names.Add(name))
                        {
                            throw new ArgumentException("A registry object repeats a member name.");
                        }
                        members.Add(new RegistryMemberDataType
                        {
                            Name = name,
                            Value = Read(member.Value, budget, depth + 1)
                        });
                    }
                    return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
                default:
                    throw new ArgumentException("A registry document contains an undefined JSON value.");
            }
        }

        private static RegistryNumberValueDataType ReadNumber(string literal)
        {
            int exponentPosition = literal.AsSpan().IndexOf('e');
            if (exponentPosition < 0)
            {
                exponentPosition = literal.AsSpan().IndexOf('E');
            }
            int decimalPosition = literal.AsSpan().IndexOf('.');
            int significandLength = exponentPosition < 0 ? literal.Length : exponentPosition;
            string significand = literal.Substring(0, significandLength);
            bool integral = exponentPosition < 0 && decimalPosition < 0;
            long exponent = 0;
            if (exponentPosition >= 0)
            {
#if NETFRAMEWORK
                exponent = long.Parse(literal.Substring(exponentPosition + 1), CultureInfo.InvariantCulture);
#else
                exponent = long.Parse(literal.AsSpan(exponentPosition + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
#endif
            }
            if (decimalPosition >= 0)
            {
                exponent = checked(exponent - (significandLength - decimalPosition - 1));
                significand = significand.Remove(decimalPosition, 1);
            }
            BigInteger coefficient = BigInteger.Parse(significand, CultureInfo.InvariantCulture);
            byte[] bytes = coefficient.ToByteArray();
            Array.Reverse(bytes);
            return new RegistryNumberValueDataType
            {
                Kind = 3,
                Coefficient = ByteString.From(bytes),
                Exponent = exponent,
                IsInteger = integral,
                NegativeZero = !integral && coefficient.IsZero && literal[0] == '-'
            };
        }

        private static BigInteger Coefficient(RegistryNumberValueDataType value)
        {
            ReadOnlySpan<byte> bytes = value.Coefficient.Span;
            if (bytes.IsEmpty ||
                (bytes.Length > 1 &&
                    ((bytes[0] == 0 && bytes[1] < 128) || (bytes[0] == 255 && bytes[1] >= 128))))
            {
                throw new ArgumentException("An exact number requires a minimal signed coefficient.");
            }
            byte[] reversed = bytes.ToArray();
            Array.Reverse(reversed);
            var coefficient = new BigInteger(reversed);
            if ((value.IsInteger && (value.Exponent != 0 || value.NegativeZero)) ||
                (value.NegativeZero && !coefficient.IsZero))
            {
                throw new ArgumentException("An exact number has inconsistent integral or negative-zero fields.");
            }
            return coefficient;
        }

        private static void Write(
            RegistryValueDataType value,
            Utf8JsonWriter? writer,
            Budget budget,
            int depth)
        {
            budget.Take(depth);
            switch (value)
            {
                case RegistryNullValueDataType when value.Kind == 0 && value.GetType() == typeof(RegistryNullValueDataType):
                    writer?.WriteNullValue();
                    break;
                case RegistryBooleanValueDataType boolean when value.Kind == 1 &&
                    value.GetType() == typeof(RegistryBooleanValueDataType):
                    writer?.WriteBooleanValue(boolean.Value);
                    break;
                case RegistryStringValueDataType text when value.Kind == 2 &&
                    value.GetType() == typeof(RegistryStringValueDataType):
                    string checkedText = Unicode(text.Value);
                    writer?.WriteStringValue(checkedText);
                    break;
                case RegistryNumberValueDataType number when value.Kind == 3 &&
                    value.GetType() == typeof(RegistryNumberValueDataType):
                    BigInteger coefficient = Coefficient(number);
                    if (writer is not null)
                    {
                        string literal = number.NegativeZero ? "-0" : coefficient.ToString(CultureInfo.InvariantCulture);
                        if (!number.IsInteger)
                        {
                            literal += "e" + number.Exponent.ToString(CultureInfo.InvariantCulture);
                        }
                        writer.WriteRawValue(literal, skipInputValidation: false);
                    }
                    break;
                case RegistryArrayValueDataType array when value.Kind == 4 &&
                    value.GetType() == typeof(RegistryArrayValueDataType):
                    writer?.WriteStartArray();
                    foreach (RegistryValueDataType child in array.Items)
                    {
                        Write(child, writer, budget, depth + 1);
                    }
                    writer?.WriteEndArray();
                    break;
                case RegistryObjectValueDataType map when value.Kind == 5 &&
                    value.GetType() == typeof(RegistryObjectValueDataType):
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    writer?.WriteStartObject();
                    foreach (RegistryMemberDataType member in map.Members)
                    {
                        if (member is null)
                        {
                            throw new ArgumentException("A registry object has a missing member.");
                        }
                        string name = Unicode(member.Name);
                        if (!names.Add(name))
                        {
                            throw new ArgumentException("A registry object repeats a member name.");
                        }
                        writer?.WritePropertyName(name);
                        Write(member.Value, writer, budget, depth + 1);
                    }
                    writer?.WriteEndObject();
                    break;
                default:
                    throw new ArgumentException("The registry value type and discriminator disagree.");
            }
        }

        private static bool EqualValue(RegistryValueDataType first, RegistryValueDataType second)
        {
            if (first.GetType() != second.GetType())
            {
                return false;
            }
            if (first is RegistryObjectValueDataType left && second is RegistryObjectValueDataType right)
            {
                if (left.Members.Count != right.Members.Count)
                {
                    return false;
                }
                var members = new Dictionary<string, RegistryValueDataType>(StringComparer.Ordinal);
                foreach (RegistryMemberDataType member in right.Members)
                {
                    members.Add(Unicode(member.Name), member.Value);
                }
                foreach (RegistryMemberDataType member in left.Members)
                {
                    if (!members.TryGetValue(Unicode(member.Name), out RegistryValueDataType? other) ||
                        !EqualValue(member.Value, other))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (first is RegistryArrayValueDataType firstArray && second is RegistryArrayValueDataType secondArray)
            {
                if (firstArray.Items.Count != secondArray.Items.Count)
                {
                    return false;
                }
                for (int index = 0; index < firstArray.Items.Count; index++)
                {
                    if (!EqualValue(firstArray.Items[index], secondArray.Items[index]))
                    {
                        return false;
                    }
                }
                return true;
            }
            return first.IsEqual(second);
        }

        private static string Unicode(string? value)
        {
            if (value is null)
            {
                throw new ArgumentException("A registry String or member name cannot be null.");
            }
            _ = s_utf8.GetByteCount(value);
            return value;
        }

        private sealed class Budget
        {
            public Budget(int maxDepth, int maxValues)
            {
                if (maxDepth < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxDepth));
                }
                if (maxValues < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxValues));
                }
                m_depth = maxDepth;
                m_remaining = maxValues;
            }

            public void Take(int depth)
            {
                if (depth >= m_depth || --m_remaining < 0)
                {
                    throw new ArgumentException("Native registry traversal exceeds the advertised bounds.");
                }
            }

            private readonly int m_depth;
            private int m_remaining;
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
