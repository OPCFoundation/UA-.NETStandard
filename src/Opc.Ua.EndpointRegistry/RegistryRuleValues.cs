/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
#pragma warning disable CS8602 // Native values are validated by RegistryValues before traversal.
#pragma warning disable CS8604 // Native values are validated by RegistryValues before traversal.
    internal static class RegistryRuleValues
    {
        public static bool TryGet(RegistryObjectValueDataType value, string name, out RegistryValueDataType? item)
        {
            foreach (RegistryMemberDataType member in value.Members)
            {
                if (string.Equals(member.Name, name, StringComparison.Ordinal))
                {
                    item = member.Value;
                    return true;
                }
            }
            item = null;
            return false;
        }

        public static RegistryObjectValueDataType Object(RegistryValueDataType value, string path)
        {
            if (value is RegistryObjectValueDataType map && value.Kind == 5 && value.GetType() == typeof(RegistryObjectValueDataType))
            {
                return map;
            }
            throw RegistryRuleException.Fail("E_OBJECT", path, "an Object is required");
        }

        public static RegistryObjectValueDataType ObjectOrEmpty(RegistryObjectValueDataType value, string name)
        {
            return TryGet(value, name, out RegistryValueDataType? item)
                ? Object(item!, name)
                : new RegistryObjectValueDataType { Kind = 5, Members = [] };
        }

        public static RegistryArrayValueDataType Array(RegistryValueDataType value, string path)
        {
            return value is RegistryArrayValueDataType array && value.Kind == 4 && value.GetType() == typeof(RegistryArrayValueDataType)
                ? array
                : throw RegistryRuleException.Fail("E_TYPE", path, "an array is required");
        }

        public static string NonEmptyString(RegistryValueDataType value, string path)
        {
            if (value is RegistryStringValueDataType text && value.Kind == 2 && !string.IsNullOrEmpty(text.Value))
            {
                return text.Value;
            }
            throw RegistryRuleException.Fail("E_STRING", path, "a non-empty String is required");
        }

        public static string? OptionalString(RegistryObjectValueDataType value, string name)
        {
            return TryGet(value, name, out RegistryValueDataType? item) &&
                item is RegistryStringValueDataType text && item.Kind == 2
                ? text.Value
                : null;
        }

        public static bool Has(RegistryObjectValueDataType value, string name)
        {
            return TryGet(value, name, out _);
        }

        public static bool? OptionalBoolean(RegistryObjectValueDataType value, string name)
        {
            return TryGet(value, name, out RegistryValueDataType? item) &&
                item is RegistryBooleanValueDataType flag && item.Kind == 1
                ? flag.Value
                : null;
        }

        public static IReadOnlyList<string> StringArray(RegistryObjectValueDataType value, string name, string path)
        {
            if (!TryGet(value, name, out RegistryValueDataType? item))
            {
                return [];
            }
            var result = new List<string>();
            foreach (RegistryValueDataType child in Array(item!, path).Items)
            {
                result.Add(NonEmptyString(child, path));
            }
            return result;
        }

        public static int Count(RegistryObjectValueDataType value)
        {
            return value.Members.Count;
        }

        public static IEnumerable<KeyValuePair<string, RegistryValueDataType>> Members(RegistryObjectValueDataType value)
        {
            foreach (RegistryMemberDataType member in value.Members.ToArray())
            {
                yield return new KeyValuePair<string, RegistryValueDataType>(member.Name!, member.Value);
            }
        }

        public static bool IsInteger(RegistryValueDataType value)
        {
            return value is RegistryNumberValueDataType number && IsMathematicalInteger(number);
        }

        public static bool TryInt64(RegistryValueDataType value, out long result)
        {
            if (value is RegistryNumberValueDataType number && IsMathematicalInteger(number))
            {
                BigInteger scaled = Scale(Coefficient(number), number.Exponent);
                if (scaled >= long.MinValue && scaled <= long.MaxValue)
                {
                    result = (long)scaled;
                    return true;
                }
            }
            result = 0;
            return false;
        }

        public static int CompareNumber(RegistryNumberValueDataType left, RegistryNumberValueDataType right)
        {
            BigInteger l = Coefficient(left);
            BigInteger r = Coefficient(right);
            long e = left.Exponent;
            long f = right.Exponent;
            if (e == f)
            {
                return l.CompareTo(r);
            }
            if (e > f)
            {
                return Scale(l, e - f).CompareTo(r);
            }
            return l.CompareTo(Scale(r, f - e));
        }

        public static RegistryNumberValueDataType NumberFromJson(long value)
        {
            return (RegistryNumberValueDataType)RegistryValues.Parse(
                Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
        }

        public static bool JsonEqual(RegistryValueDataType left, RegistryValueDataType right)
        {
            if (left is RegistryNumberValueDataType first && right is RegistryNumberValueDataType second)
            {
                return CompareNumber(first, second) == 0;
            }
            if (left.GetType() != right.GetType() || left.Kind != right.Kind)
            {
                return false;
            }
            switch (left)
            {
                case RegistryNullValueDataType:
                    return true;
                case RegistryBooleanValueDataType a when right is RegistryBooleanValueDataType b:
                    return a.Value == b.Value;
                case RegistryStringValueDataType a when right is RegistryStringValueDataType b:
                    return string.Equals(a.Value, b.Value, StringComparison.Ordinal);
                case RegistryArrayValueDataType a when right is RegistryArrayValueDataType b:
                    if (a.Items.Count != b.Items.Count)
                    {
                        return false;
                    }
                    for (int index = 0; index < a.Items.Count; index++)
                    {
                        if (!JsonEqual(a.Items[index], b.Items[index]))
                        {
                            return false;
                        }
                    }
                    return true;
                case RegistryObjectValueDataType a when right is RegistryObjectValueDataType b:
                    if (a.Members.Count != b.Members.Count)
                    {
                        return false;
                    }
                    foreach (RegistryMemberDataType member in a.Members)
                    {
                        if (!TryGet(b, member.Name, out RegistryValueDataType? other) || !JsonEqual(member.Value, other!))
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }

        public static string ToJsonText(RegistryValueDataType value)
        {
            return Encoding.UTF8.GetString(RegistryValues.ToJson(value).ToArray());
        }

        private static bool IsMathematicalInteger(RegistryNumberValueDataType number)
        {
            if (number.IsInteger)
            {
                return true;
            }
            if (number.Exponent >= 0)
            {
                return true;
            }
            BigInteger coefficient = Coefficient(number);
            if (coefficient.IsZero)
            {
                return true;
            }
            long zeros = 0;
            coefficient = BigInteger.Abs(coefficient);
            while (!coefficient.IsZero && coefficient % 10 == 0)
            {
                zeros++;
                coefficient /= 10;
            }
            return number.Exponent != long.MinValue && zeros >= -number.Exponent;
        }

        private static BigInteger Coefficient(RegistryNumberValueDataType value)
        {
            ReadOnlySpan<byte> bytes = value.Coefficient.Span;
            if (bytes.Length == 0)
            {
                return BigInteger.Zero;
            }
            byte[] littleEndian = bytes.ToArray();
            System.Array.Reverse(littleEndian);
            return new BigInteger(littleEndian);
        }

        private static BigInteger Scale(BigInteger coefficient, long exponent)
        {
            if (exponent < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exponent));
            }
            if (exponent > int.MaxValue)
            {
                throw RegistryRuleException.Fail("E_SCHEMA", "/", "numeric exponent is too large");
            }
            return coefficient * BigInteger.Pow(10, (int)exponent);
        }
    }
}
