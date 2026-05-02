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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Represents a generic enumeration value that can be stored within
    /// a Variant and retains symbol information for encoders. It contains
    /// both symbol and value which both are required to write/read from
    /// wire representation..
    /// </summary>
    public readonly struct EnumValue : IEquatable<EnumValue>
    {
        /// <summary>
        /// Create enum value
        /// </summary>
        public EnumValue(int value, string? symbol = null)
        {
            Source = symbol;
            m_value = value;
        }

        /// <summary>
        /// Create enum value
        /// </summary>
        public EnumValue(int value, Type? enumType)
        {
            if (enumType == null || enumType == typeof(int))
            {
                m_value = value;
                return;
            }
            if (!enumType.IsEnum)
            {
                throw new ArgumentException(
                    "The provided type must be an enumeration.",
                    nameof(enumType));
            }
            Source = enumType;
            m_value = value;
        }

        /// <summary>
        /// Create enum value
        /// </summary>
        public EnumValue(int value, IEnumeratedType enumType)
        {
            Source = enumType;
            m_value = value;
        }

        /// <summary>
        /// Internal constructor to convert from Variant to enum value
        /// </summary>
        internal EnumValue(int value, object? source)
        {
            Source = source;
            m_value = value;
        }

        /// <summary>
        /// The enum value
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public int Value => m_value;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// The symbol for the value
        /// </summary>
        public string? Symbol
        {
            get
            {
                switch (Source)
                {
                    case string str:
                        if (!string.IsNullOrEmpty(str))
                        {
                            return str;
                        }
                        goto default;
                    case IEnumeratedType enumeratedType:
                        if (enumeratedType.TryGetSymbol(
                            Value,
                            out string? symbol))
                        {
                            return symbol;
                        }
                        goto default;
                    case Type enumType:
                        string? name = Enum.GetName(
                            enumType,
                            Value);
                        if (!string.IsNullOrEmpty(name))
                        {
                            return name;
                        }
                        goto default;
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Try to get a xml name
        /// </summary>
        public XmlQualifiedName? XmlName
        {
            get
            {
                switch (Source)
                {
                    case string str:
                        if (!string.IsNullOrEmpty(str))
                        {
                            return new XmlQualifiedName(str);
                        }
                        goto default;
                    case IEnumeratedType enumeratedType:
                        return enumeratedType.XmlName;
                    case Type enumType:
                        return TypeInfo.GetXmlName(enumType);
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static EnumValue From<T>(T value)
            where T : struct, Enum
        {
            return From(EnumHelper.EnumToInt32(value), typeof(T));
        }

        /// <summary>
        /// Create a enum value array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ArrayOf<EnumValue> From<T>(ArrayOf<T> value)
            where T : struct, Enum
        {
            return value.ConvertAll(From);
        }

        /// <summary>
        /// Create a enum value matrix
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static MatrixOf<EnumValue> From<T>(MatrixOf<T> value)
            where T : struct, Enum
        {
            return value.ConvertAll(From);
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        public static EnumValue From(int value, Type? type = null)
        {
            return new EnumValue(value, type);
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        public static ArrayOf<EnumValue> From(ArrayOf<int> value, Type? type = null)
        {
            if (value.IsNull)
            {
                return default;
            }
            return value.ConvertAll(e => From(e, type));
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        public static MatrixOf<EnumValue> From(MatrixOf<int> value, Type? type = null)
        {
            if (value.IsNull)
            {
                return default;
            }
            return value.ConvertAll(e => From(e, type));
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        public static EnumValue From(object value, Type type)
        {
            return new EnumValue(EnumHelper.EnumToInt32(value, type), type);
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static EnumValue GetDefault<T>()
            where T : struct, Enum
        {
#if NET8_0_OR_GREATER
            T[] values = Enum.GetValues<T>();
            if (values == null || values.Length == 0)
            {
                return default;
            }
            return From(values[0]);
#else
            return GetDefault(typeof(T));
#endif
        }

        /// <summary>
        /// Create a enum value
        /// </summary>
        [RequiresDynamicCode(
            "Cannot get the values of the type in AOT. Use GetDefault<T> instead.")]
        public static EnumValue GetDefault(Type type)
        {
            Array values = Enum.GetValues(type);
            if (values == null || values.Length == 0)
            {
                return default;
            }
            object? value = values.GetValue(0);
            if (value == null)
            {
                return default;
            }
            return From(value, type);
        }

        /// <summary>
        /// Get array of enum values
        /// </summary>
        public static ArrayOf<EnumValue> FromArray(Array? value, Type type)
        {
            if (value is null)
            {
                return default;
            }
            if (type.IsArray)
            {
                type = type.GetElementType()!;
            }
            return EnumHelper.EnumArrayToInt32Array(value)
                .ConvertAll(e => new EnumValue(e, type));
        }

        /// <summary>
        /// Get array of enum values
        /// </summary>
        public static MatrixOf<EnumValue> FromMatrix(Array? value, Type type)
        {
            if (value is null)
            {
                return default;
            }
            if (type.IsArray)
            {
                type = type.GetElementType()!;
            }
            return EnumHelper.EnumArrayToInt32Matrix(value)
                .ConvertAll(e => new EnumValue(e, type));
        }

        /// <summary>
        /// Convert to typed enum type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public T To<T>() where T : struct, Enum
        {
            return EnumHelper.Int32ToEnum<T>(Value);
        }

        /// <summary>
        /// Convert to an enum instance as object
        /// </summary>
        public object ToObject()
        {
            if (Source is Type enumType)
            {
                return EnumHelper.Int32ToEnum(Value, enumType) ?? Value;
            }
            return Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                string value => Equals(value),
                int value => Equals(value),
                EnumValue value => Equals(value),
                _ => false
            };
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            string? symbol = Symbol;
            if (string.IsNullOrEmpty(symbol))
            {
                return Value.ToString(CultureInfo.InvariantCulture);
            }
            return $"{symbol}_{Value}";
        }

        /// <inheritdoc/>
        public bool Equals(EnumValue other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public bool Equals(string? other)
        {
            return string.Equals(
                Symbol,
                other,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public bool Equals(int value)
        {
            return Value == value;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, EnumValue right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, EnumValue right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, int right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, int right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValue left, string right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValue left, string right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static explicit operator int(EnumValue value)
        {
            return value.Value;
        }

        /// <inheritdoc/>
        public static explicit operator EnumValue(int value)
        {
            return new EnumValue(value);
        }

        internal readonly object? Source;
        private readonly int m_value;
    }
}
