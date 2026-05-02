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
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A class that stores the value of variable with an optional status code and timestamps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This object relates to the <b>OPC UA Specifications Part 6: Mappings, section 6.2.2.16</b>
    /// titled <b>DataValue</b>.
    /// <br/></para>
    /// <para>
    /// This object is essentially a place-holder for the following:
    /// <list type="bullet">
    ///     <item><see cref="Variant"/></item>
    ///     <item><see cref="StatusCode"/></item>
    ///     <item><see cref="DateTimeUtc"/> for the Servers Timestamp</item>
    /// </list>
    /// <br/></para>
    /// </remarks>
    /// <example>
    /// <code lang="C#">
    ///
    /// //define a new DataValue first where:
    /// //  (a) the value is a string, which is "abc123"
    /// //  (b) the statuscode is 0 (zero)
    /// //  (c) the timestamp is 'now'
    /// DataValue dv = new DataValue(Variant.From("abc123"), new StatusCode(0), DateTimeUtc.Now);
    ///
    /// </code>
    /// <code lang="Visual Basic">
    ///
    /// 'define a new DataValue first where:
    /// '  (a) the value is a string, which is "abc123"
    /// '  (b) the statuscode is 0 (zero)
    /// '  (c) the timestamp is 'now'
    /// Dim dv As DataValue = New DataValue(New Variant("abc123"), New StatusCode(0), DateTimeUtc.Now);
    ///
    /// </code>
    /// </example>
    /// <seealso cref="Variant"/>
    /// <seealso cref="StatusCode"/>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public class DataValue : ICloneable, IFormattable, IEquatable<DataValue>
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public DataValue()
        {
            m_value = Variant.Null;
            StatusCode = StatusCodes.Good;
            SourceTimestamp = DateTimeUtc.MinValue;
            ServerTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a value from a <see cref="Variant"/>
        /// </remarks>
        /// <param name="value">The value to set</param>
        [OverloadResolutionPriority(1)]
        public DataValue(Variant value)
        {
            m_value = value;
            StatusCode = StatusCodes.Good;
            SourceTimestamp = DateTimeUtc.MinValue;
            ServerTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a status code.
        /// </summary>
        /// <param name="statusCode">The StatusCode to set</param>
        [Obsolete("Use DataValue.FromStatusCode() to avoid overload ambiguity with numeric types.")]
        public DataValue(StatusCode statusCode)
        {
            m_value = Variant.Null;
            StatusCode = statusCode;
            SourceTimestamp = DateTimeUtc.MinValue;
            ServerTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a status code and a server timestamp.
        /// </summary>
        /// <param name="statusCode">The status code associated with the value.</param>
        /// <param name="serverTimestamp">The timestamp associated with the status code.</param>
        [Obsolete("Use DataValue.FromStatusCode() to avoid overload ambiguity with numeric types.")]
        public DataValue(StatusCode statusCode, DateTimeUtc serverTimestamp)
        {
            m_value = Variant.Null;
            StatusCode = statusCode;
            ServerTimestamp = serverTimestamp;
            SourceTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a value and a status code.
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="statusCode">The status code to set</param>
        public DataValue(Variant value, StatusCode statusCode)
        {
            m_value = value;
            StatusCode = statusCode;
            SourceTimestamp = DateTimeUtc.MinValue;
            ServerTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a value, a status code and a source timestamp
        /// </summary>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The timestamp to set</param>
        public DataValue(Variant value, StatusCode statusCode, DateTimeUtc sourceTimestamp)
        {
            m_value = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
            ServerTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a value, a status code, a source timestamp
        /// and a server timestamp
        /// </summary>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The source timestamp to set</param>
        /// <param name="serverTimestamp">The servers timestamp to set</param>
        public DataValue(
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp,
            DateTimeUtc serverTimestamp)
        {
            m_value = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
            ServerTimestamp = serverTimestamp;
        }

        /// <summary>
        /// Creates a DataValue with only a status code (no value).
        /// </summary>
        /// <param name="statusCode">The status code to set.</param>
        /// <returns>A new <see cref="DataValue"/> with the specified status code.</returns>
        public static DataValue FromStatusCode(StatusCode statusCode)
        {
            return new DataValue
            {
                StatusCode = statusCode
            };
        }

        /// <summary>
        /// Creates a DataValue with a status code and a server timestamp (no value).
        /// </summary>
        /// <param name="statusCode">The status code to set.</param>
        /// <param name="serverTimestamp">The server timestamp to set.</param>
        /// <returns>A new <see cref="DataValue"/> with the specified status code and server timestamp.</returns>
        public static DataValue FromStatusCode(StatusCode statusCode, DateTimeUtc serverTimestamp)
        {
            return new DataValue
            {
                StatusCode = statusCode,
                ServerTimestamp = serverTimestamp
            };
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                null => false,
                DataValue value => Equals(value),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(DataValue other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            if (StatusCode != other.StatusCode)
            {
                return false;
            }

            if (ServerTimestamp != other.ServerTimestamp)
            {
                return false;
            }

            if (SourceTimestamp != other.SourceTimestamp)
            {
                return false;
            }

            if (ServerPicoseconds != other.ServerPicoseconds)
            {
                return false;
            }

            if (SourcePicoseconds != other.SourcePicoseconds)
            {
                return false;
            }

            if (m_value != other.m_value)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public static bool operator ==(DataValue a, DataValue b)
        {
            return a is null ? b is null : a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(DataValue a, DataValue b)
        {
            return !(a == b);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (!m_value.IsNull)
            {
                return m_value.GetHashCode();
            }

            return StatusCode.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", m_value);
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return Copy();
        }

        /// <summary>
        /// Creates a deep copy of the DataValue.
        /// </summary>
        /// <returns>A new <see cref="DataValue"/> that is a deep copy of this instance.</returns>
        public DataValue Copy()
        {
            var copy = (DataValue)base.MemberwiseClone();
            copy.m_value = m_value.Copy();
            return copy;
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            return Copy();
        }

        /// <summary>
        /// The value of data value.
        /// </summary>
        [Obsolete("Use WrappedValue to access The value.")]
        public object Value
        {
            get => m_value.AsBoxedObject(Variant.BoxingBehavior.Legacy);
            set => VariantHelper.TryCastFrom(value, out m_value);
        }

        /// <summary>
        /// The value of data value.
        /// </summary>
        [DataMember(Name = "Value", Order = 1, IsRequired = false)]
        public Variant WrappedValue
        {
            get => m_value;
            set => m_value = value;
        }

        /// <summary>
        /// The status code associated with the value.
        /// </summary>
        [DataMember(Order = 2, IsRequired = false)]
        public StatusCode StatusCode { get; set; }

        /// <summary>
        /// The source timestamp associated with the value.
        /// </summary>
        [DataMember(Order = 3, IsRequired = false)]
        public DateTimeUtc SourceTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the source timestamp.
        /// </summary>
        [DataMember(Order = 4, IsRequired = false)]
        public ushort SourcePicoseconds { get; set; }

        /// <summary>
        /// The server timestamp associated with the value.
        /// </summary>
        [DataMember(Order = 5, IsRequired = false)]
        public DateTimeUtc ServerTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the server timestamp.
        /// </summary>
        [DataMember(Order = 6, IsRequired = false)]
        public ushort ServerPicoseconds { get; set; }

        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsGood(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsGood(value.StatusCode);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status is bad or uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotGood(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsNotGood(value.StatusCode);
            }

            return true;
        }

        /// <summary>
        /// Returns true if the status code is uncertain.
        /// </summary>
        /// <param name="value">The value to checck the quality of</param>
        public static bool IsUncertain(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsUncertain(value.StatusCode);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotUncertain(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsNotUncertain(value.StatusCode);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status code is bad.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsBad(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsBad(value.StatusCode);
            }

            return true;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotBad(DataValue value)
        {
            if (value != null)
            {
                return StatusCode.IsNotBad(value.StatusCode);
            }

            return false;
        }

        /// <summary>
        /// Ensures the data value contains a value with the specified type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use WrappedValue and Variant API")]
        public object GetValue(Type expectedType)
        {
            object value = WrappedValue.AsBoxedObject();

            if (expectedType != null && value != null)
            {
                // return null for a DataValue with bad status code.
                if (StatusCode.IsBad(StatusCode))
                {
                    return null;
                }

                if (value is ExtensionObject extension &&
                    extension.TryGetValue(out IEncodeable encodeable))
                {
                    value = encodeable;
                }

                if (!expectedType.IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "DataValue is not of type {0}.",
                        expectedType.Name);
                }
            }

            return value;
        }

        /// <summary>
        /// Gets the value from the data value.
        /// Returns default value for bad status.
        /// </summary>
        /// <typeparam name="T">The type of object.</typeparam>
        /// <returns>The value.</returns>
        /// <remarks>
        /// Checks the StatusCode and returns default value for bad status.
        /// Extracts the body from an ExtensionObject value if it has the correct type.
        /// Throws exception only if there is a type mismatch;
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use WrappedValue and Variant API")]
        public T GetValueOrDefault<T>()
        {
            // return default for a DataValue with bad status code.
            if (StatusCode.IsBad(StatusCode))
            {
                return default;
            }

            if (!WrappedValue.IsNull)
            {
                if (WrappedValue.TryGetValue(out ExtensionObject extension) &&
                    extension.TryGetValue(out IEncodeable encodeable) &&
                    encodeable is T typed)
                {
                    return typed;
                }
                else if (WrappedValue.TryCastTo(out typed))
                {
                    return typed;
                }

                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "DataValue is not of type {0}.",
                    typeof(T).Name);
            }

            // a null value for a value type should throw
            if (typeof(T).IsValueType)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "DataValue is null and not of value type {0}.",
                    typeof(T).Name);
            }

            return default;
        }

        /// <summary>
        /// Gets the value from the data value.
        /// </summary>
        /// <typeparam name="T">The type of object.</typeparam>
        /// <param name="defaultValue">The default value to return if any error
        /// occurs.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        /// Does not throw exceptions; returns the caller provided value instead.
        /// Extracts the body from an ExtensionObject value if it has the correct
        /// type. Checks the StatusCode and returns an error if not Good.
        /// </remarks>
        public T GetValue<T>(T defaultValue)
        {
            if (StatusCode.IsNotGood(StatusCode))
            {
                return defaultValue;
            }

            if (WrappedValue.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out IEncodeable encodeable) &&
                encodeable is T typedBody)
            {
                return typedBody;
            }

            if (WrappedValue.TryCastTo(out typedBody))
            {
                return typedBody;
            }

            return defaultValue;
        }

        private Variant m_value;
    }
}
