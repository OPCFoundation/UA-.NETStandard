/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <remarks>
        /// Initializes the object with default values.
        /// </remarks>
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

            m_value.Value = Utils.Clone(value.m_value.Value);
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
        /// <remarks>
        /// Initializes the object with a status code.
        /// </remarks>
        /// <param name="statusCode">The StatusCode to set</param>
        public DataValue(StatusCode statusCode)
        {
            Initialize();
            StatusCode = statusCode;
        }

        /// <summary>
        /// Initializes the object with a status code and a server timestamp.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a status code and a server timestamp.
        /// </remarks>
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
        /// <remarks>
        /// Initializes the object with a value and a status code.
        /// </remarks>
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
        /// <remarks>
        /// Initializes the object with a value, a status code and a source timestamp
        /// </remarks>
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
        /// <remarks>
        /// Initializes the object with a value, a status code, a source timestamp and a server timestamp
        /// </remarks>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The source timestamp to set</param>
        /// <param name="serverTimestamp">The servers timestamp to set</param>
        public DataValue(Variant value, StatusCode statusCode, DateTime sourceTimestamp, DateTime serverTimestamp)
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
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        /// <param name="obj">The object to compare to *this*</param>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is DataValue value)
            {
                if (this.StatusCode != value.StatusCode)
                {
                    return false;
                }

                if (this.ServerTimestamp != value.ServerTimestamp)
                {
                    return false;
                }

                if (this.SourceTimestamp != value.SourceTimestamp)
                {
                    return false;
                }

                if (this.ServerPicoseconds != value.ServerPicoseconds)
                {
                    return false;
                }

                if (this.SourcePicoseconds != value.SourcePicoseconds)
                {
                    return false;
                }

                return Utils.IsEqual(this.m_value.Value, value.m_value.Value);
            }

            return false;
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        /// <param name="other">The DataValue to compare to *this*</param>
        public bool Equals(DataValue other)
        {
            if (Object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other != null)
            {
                if (this.StatusCode != other.StatusCode)
                {
                    return false;
                }

                if (this.ServerTimestamp != other.ServerTimestamp)
                {
                    return false;
                }

                if (this.SourceTimestamp != other.SourceTimestamp)
                {
                    return false;
                }

                if (this.ServerPicoseconds != other.ServerPicoseconds)
                {
                    return false;
                }

                if (this.SourcePicoseconds != other.SourcePicoseconds)
                {
                    return false;
                }

                return Utils.IsEqual(this.m_value.Value, other.m_value.Value);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        /// <remarks>
        /// Returns a unique hashcode for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            if (this.m_value.Value != null)
            {
                return this.m_value.Value.GetHashCode();
            }

            return this.StatusCode.GetHashCode();
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        /// <remarks>
        /// Converts the value to a human readable string.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <param name="format">Not used, ALWAYS specify a null/nothing value</param>
        /// <param name="formatProvider">The format string, ALWAYS specify a null/nothing value</param>
        /// <exception cref="FormatException">Thrown when the format is NOT null/nothing</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", m_value);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Makes a deep copy of the object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new DataValue(this);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The value of data value.
        /// </summary>
        /// <remarks>
        /// The value of data value.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public object Value
        {
            get { return m_value.Value; }
            set { m_value.Value = value; }
        }

        /// <summary>
        /// The value of data value.
        /// </summary>
        /// <remarks>
        /// The value of data value.
        /// </remarks>
        [DataMember(Name = "Value", Order = 1, IsRequired = false)]
        public Variant WrappedValue
        {
            get { return m_value; }
            set { m_value = value; }
        }

        /// <summary>
        /// The status code associated with the value.
        /// </summary>
        /// <remarks>
        /// The status code associated with the value.
        /// </remarks>
        [DataMember(Order = 2, IsRequired = false)]
        public StatusCode StatusCode { get; set; }

        /// <summary>
        /// The source timestamp associated with the value.
        /// </summary>
        /// <remarks>
        /// The source timestamp associated with the value.
        /// </remarks>
        [DataMember(Order = 3, IsRequired = false)]
        public DateTime SourceTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the source timestamp.
        /// </summary>
        /// <remarks>
        /// Additional resolution for the source timestamp.
        /// </remarks>
        [DataMember(Order = 4, IsRequired = false)]
        public ushort SourcePicoseconds { get; set; }

        /// <summary>
        /// The server timestamp associated with the value.
        /// </summary>
        /// <remarks>
        /// The server timestamp associated with the value.
        /// </remarks>
        [DataMember(Order = 5, IsRequired = false)]
        public DateTime ServerTimestamp { get; set; }

        /// <summary>
        /// Additional resolution for the server timestamp.
        /// </summary>
        /// <remarks>
        /// Additional resolution for the server timestamp.
        /// </remarks>
        [DataMember(Order = 6, IsRequired = false)]
        public ushort ServerPicoseconds { get; set; }
        #endregion

        #region Static Methods
        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        /// <remarks>
        /// Returns true if the status code is good.
        /// </remarks>
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
        /// <remarks>
        /// Returns true if the status is bad or uncertain.
        /// </remarks>
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
        /// <remarks>
        /// Returns true if the status code is uncertain.
        /// </remarks>
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
        /// <remarks>
        /// Returns true if the status is good or uncertain.
        /// </remarks>
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
        /// <remarks>
        /// Returns true if the status code is bad.
        /// </remarks>
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
        /// <remarks>
        /// Returns true if the status is good or uncertain.
        /// </remarks>
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
        public object GetValue(Type expectedType)
        {
            object value = this.Value;

            if (expectedType != null && value != null)
            {
                // return null for a DataValue with bad status code.
                if (StatusCode.IsBad(this.StatusCode))
                {
                    return null;
                }

                if (value is ExtensionObject extension)
                {
                    value = extension.Body;
                }

                if (!expectedType.IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "DataValue is not of type {0}.", expectedType.Name);
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
        public T GetValueOrDefault<T>()
        {
            // return default for a DataValue with bad status code.
            if (StatusCode.IsBad(this.StatusCode))
            {
                return default;
            }

            object value = this.Value;
            if (value != null)
            {
                if (value is ExtensionObject extension)
                {
                    value = extension.Body;
                }

                if (!typeof(T).IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "DataValue is not of type {0}.", typeof(T).Name);
                }

                return (T)value;
            }

            // a null value for a value type should throw
            if (typeof(T).IsValueType)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "DataValue is null and not of value type {0}.", typeof(T).Name);
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
            if (StatusCode.IsNotGood(this.StatusCode))
            {
                return defaultValue;
            }

            if (typeof(T).IsInstanceOfType(this.Value))
            {
                return (T)this.Value;
            }

            if (this.Value is ExtensionObject extension && typeof(T).IsInstanceOfType(extension.Body))
            {
                return (T)extension.Body;
            }

            return defaultValue;
        }
        #endregion

        #region Private Fields
        private Variant m_value;
        #endregion
    }

    #region DataValueCollection Class
    /// <summary>
    /// A collection of DataValues.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of DataValues.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfDataValue", Namespace = Namespaces.OpcUaXsd, ItemName = "DataValue")]
    public class DataValueCollection : List<DataValue>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public DataValueCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of <see cref="DataValue"/> objects to pre-populate this new collection with</param>
        public DataValueCollection(IEnumerable<DataValue> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity of this collection</param>
        public DataValueCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="DataValue"/> objects to return as a collection</param>
        public static DataValueCollection ToDataValueCollection(DataValue[] values)
        {
            if (values != null)
            {
                return new DataValueCollection(values);
            }

            return new DataValueCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="DataValue"/> objects to return as a collection</param>
        public static implicit operator DataValueCollection(DataValue[] values)
        {
            return ToDataValueCollection(values);
        }

        #region ICloneable
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            var clone = new DataValueCollection(this.Count);

            foreach (DataValue element in this)
            {
                clone.Add((DataValue)Utils.Clone(element));
            }

            return clone;
        }
        #endregion
    }
    #endregion

}//namespace
