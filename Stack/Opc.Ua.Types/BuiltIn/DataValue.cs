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
    ///     <item><see cref="DateTime"/> for the Servers Timestamp</item>
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
    /// DataValue dv = new DataValue(new Variant("abc123"), new StatusCode(0), DateTime.Now);
    ///
    /// </code>
    /// <code lang="Visual Basic">
    ///
    /// 'define a new DataValue first where:
    /// '  (a) the value is a string, which is "abc123"
    /// '  (b) the statuscode is 0 (zero)
    /// '  (c) the timestamp is 'now'
    /// Dim dv As DataValue = New DataValue(New Variant("abc123"), New StatusCode(0), DateTime.Now);
    ///
    /// </code>
    /// </example>
    /// <seealso cref="Variant"/>
    /// <seealso cref="StatusCode"/>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class DataValue : ICloneable, IFormattable, IEquatable<DataValue>
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public DataValue()
        {
            Initialize();
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while copying the contents
        /// of another instance.
        /// </remarks>
        /// <param name="value">The DataValue to copy.</param>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public DataValue(DataValue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            m_value.Value = CoreUtils.Clone(value.m_value.Value);
            StatusCode = value.StatusCode;
            SourceTimestamp = value.SourceTimestamp;
            SourcePicoseconds = value.SourcePicoseconds;
            ServerTimestamp = value.ServerTimestamp;
            ServerPicoseconds = value.ServerPicoseconds;
        }

        /// <summary>
        /// Initializes the object with a value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a value from a <see cref="Variant"/>
        /// </remarks>
        /// <param name="value">The value to set</param>
        public DataValue(Variant value)
        {
            Initialize();

            m_value = value;
        }

        /// <summary>
        /// Initializes the object with a status code.
        /// </summary>
        /// <param name="statusCode">The StatusCode to set</param>
        public DataValue(StatusCode statusCode)
        {
            Initialize();
            StatusCode = statusCode;
        }

        /// <summary>
        /// Initializes the object with a status code and a server timestamp.
        /// </summary>
        /// <param name="statusCode">The status code associated with the value.</param>
        /// <param name="serverTimestamp">The timestamp associated with the status code.</param>
        public DataValue(StatusCode statusCode, DateTime serverTimestamp)
        {
            Initialize();
            StatusCode = statusCode;
            ServerTimestamp = serverTimestamp;
        }

        /// <summary>
        /// Initializes the object with a value and a status code.
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="statusCode">The status code to set</param>
        public DataValue(Variant value, StatusCode statusCode)
        {
            Initialize();

            m_value = value;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Initializes the object with a value, a status code and a source timestamp
        /// </summary>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The timestamp to set</param>
        public DataValue(Variant value, StatusCode statusCode, DateTime sourceTimestamp)
        {
            Initialize();

            m_value = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
        }

        /// <summary>
        /// Initializes the object with a value, a status code, a source timestamp and a server timestamp
        /// </summary>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The source timestamp to set</param>
        /// <param name="serverTimestamp">The servers timestamp to set</param>
        public DataValue(
            Variant value,
            StatusCode statusCode,
            DateTime sourceTimestamp,
            DateTime serverTimestamp)
        {
            Initialize();

            m_value = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
            ServerTimestamp = serverTimestamp;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_value = Variant.Null;
            StatusCode = StatusCodes.Good;
            SourceTimestamp = DateTime.MinValue;
            ServerTimestamp = DateTime.MinValue;
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <param name="obj">The object to compare to *this*</param>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is DataValue value)
            {
                if (StatusCode != value.StatusCode)
                {
                    return false;
                }

                if (ServerTimestamp != value.ServerTimestamp)
                {
                    return false;
                }

                if (SourceTimestamp != value.SourceTimestamp)
                {
                    return false;
                }

                if (ServerPicoseconds != value.ServerPicoseconds)
                {
                    return false;
                }

                if (SourcePicoseconds != value.SourcePicoseconds)
                {
                    return false;
                }

                return CoreUtils.IsEqual(m_value.Value, value.m_value.Value);
            }

            return false;
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <param name="other">The DataValue to compare to *this*</param>
        public bool Equals(DataValue other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other != null)
            {
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

                return CoreUtils.IsEqual(m_value.Value, other.m_value.Value);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            if (m_value.Value != null)
            {
                return m_value.Value.GetHashCode();
            }

            return StatusCode.GetHashCode();
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">Not used, ALWAYS specify a null/nothing value</param>
        /// <param name="formatProvider">The format string, ALWAYS specify a null/nothing value</param>
        /// <exception cref="FormatException">Thrown when the format is NOT null/nothing</exception>
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
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            return new DataValue(this);
        }

        /// <summary>
        /// The value of data value.
        /// </summary>
        public object Value
        {
            get => m_value.Value;
            set => m_value.Value = value;
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
        public DateTime SourceTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the source timestamp.
        /// </summary>
        [DataMember(Order = 4, IsRequired = false)]
        public ushort SourcePicoseconds { get; set; }

        /// <summary>
        /// The server timestamp associated with the value.
        /// </summary>
        [DataMember(Order = 5, IsRequired = false)]
        public DateTime ServerTimestamp { get; set; }

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
        public object GetValue(Type expectedType)
        {
            object value = Value;

            if (expectedType != null && value != null)
            {
                // return null for a DataValue with bad status code.
                if (StatusCode.IsBad(StatusCode))
                {
                    return null;
                }

                if (value is ExtensionObject extension)
                {
                    value = extension.Body;
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
        public T GetValueOrDefault<T>()
        {
            // return default for a DataValue with bad status code.
            if (StatusCode.IsBad(StatusCode))
            {
                return default;
            }

            object value = Value;
            if (value != null)
            {
                if (value is ExtensionObject extension)
                {
                    value = extension.Body;
                }

                if (!typeof(T).IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "DataValue is not of type {0}.",
                        typeof(T).Name);
                }

                return (T)value;
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
        /// <param name="defaultValue">The default value to return if any error occurs.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        /// Does not throw exceptions; returns the caller provided value instead.
        /// Extracts the body from an ExtensionObject value if it has the correct type.
        /// Checks the StatusCode and returns an error if not Good.
        /// </remarks>
        public T GetValue<T>(T defaultValue)
        {
            if (StatusCode.IsNotGood(StatusCode))
            {
                return defaultValue;
            }

            if (typeof(T).IsInstanceOfType(Value))
            {
                return (T)Value;
            }

            if (Value is ExtensionObject extension && typeof(T).IsInstanceOfType(extension.Body))
            {
                return (T)extension.Body;
            }

            return defaultValue;
        }

        private Variant m_value;
    }

    /// <summary>
    /// A collection of DataValues.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of DataValues.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfDataValue",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataValue")]
    public class DataValueCollection : List<DataValue>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public DataValueCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of <see cref="DataValue"/> objects to pre-populate this new collection with</param>
        public DataValueCollection(IEnumerable<DataValue> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max capacity of this collection</param>
        public DataValueCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="DataValue"/> objects to return as a collection</param>
        public static DataValueCollection ToDataValueCollection(DataValue[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="DataValue"/> objects to return as a collection</param>
        public static implicit operator DataValueCollection(DataValue[] values)
        {
            return ToDataValueCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new DataValueCollection(Count);

            foreach (DataValue element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }
}
