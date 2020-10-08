/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.Text;
using System.Xml.Serialization;

namespace Opc.Ua
{

    /// <summary>
    /// A numeric code that describes the result of a service or operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The StatusCode is defined in <b>OPC UA Specifications Part 4: Services, section 7.22</b>
    /// titled <b>StatusCode</b>.<br/>
    /// <br/></para>
    /// <para>
    /// A numeric code that is used to describe the result of a service or operation. The
    /// StatusCode uses bit-assignments, which are described below.
    /// <br/></para>
    /// <para>
    /// The StatusCode is a 32-bit number, with the top 16-bits (high word) representing the
    /// numeric value of an error or condition, whereas the bottom 16-bits (low word) represents
    /// additional flags to provide more information on the meaning of the status code.
    /// <br/></para>
    /// <para>
    /// The following list shows the bit-assignments, i.e. 0-7 means bit 0 to bit 7.
    /// <list type="number">
    /// <item><b>0 - 7</b>:<br/>
    /// InfoBits. Additional information bits that qualify the status code.</item>
    /// <item><b>8 - 9</b>:<br/>
    /// The type of information contained in the info bits. These bits have the following meanings:<br/>
    ///     <list type="bullet">
    ///         <item>Binary Representation <b>00</b>:<br/>
    ///         The info bits are not used and must be set to zero.</item>
    ///         <item>Binary Representation <b>01</b>:<br/>
    ///         The status code and its info bits are associated with a data value returned from the Server.</item>
    ///         <item>Binary Representation <b>10</b> or <b>11</b>:<br/>
    ///         Reserved for future use. The info bits must be ignored.</item>
    ///     </list>
    /// <br/></item>
    /// <item><b>10 - 13</b>:<br/>
    /// Reserved for future use. Must always be zero.</item>
    /// <item><b>14</b>: 
    /// Indicates that the semantics of the associated data value have changed. Clients should not process the data value until they re-read the metadata associated with the Variable.
    /// Servers should set this bit if the metadata has changed in way that could case application errors if the Client does not re-read the metadata. For example, a change to the engineering units could create problems if the Client uses the value to perform calculations.
    /// [UA Part 8] defines the conditions where a Server must set this bit for a DA Variable. Other specifications may define additional conditions. A Server may define other conditions that cause this bit to be set. 
    /// This bit only has meaning for status codes returned as part of a data change Notification. Status codes used in other contexts must always set this bit to zero.</item>
    /// <item><b>15</b>:<br/>
    /// Indicates that the structure of the associated data value has changed since the last Notification. Clients should not process the data value unless they re-read the metadata. 
    /// Servers must set this bit if the DataTypeEncoding used for a Variable changes. Clause 7.14 describes how the DataTypeEncoding is specified for a Variable.
    /// The bit is also set if the data type Attribute of the Variable changes. A Variable with data type BaseDataType does not require the bit to be set when the data type changes.
    /// Servers must also set this bit if the length of a fixed length array Variable changes.
    /// This bit is provided to warn Clients that parse complex data values that their parsing routines could fail because the serialized form of the data value has changed. 
    /// This bit only has meaning for status codes returned as part of a data change Notification. Status codes used in other contexts must always set this bit to zero.</item>
    /// <item><b>16 - 27</b>:<br/> 
    /// The code is a numeric value assigned to represent different conditions. 
    /// Each code has a symbolic name and a numeric value. All descriptions in the UA specification refer 
    /// to the symbolic name. [UA Part 6] maps the symbolic names onto a numeric value.</item>
    /// <item><b>28 - 29</b>:<br/> 
    /// Reserved for future use. Must always be zero.</item>
    /// <item><b>30 - 31</b>:<br/>
    /// Indicates whether the status code represents a good, bad or uncertain condition. 
    /// These bits have the following meanings:<br/>
    ///     <list type="bullet">
    ///         <item>Binary Represntation <b>00</b>:<br/>
    ///         Indicates that the operation was successful and the associated results may be used.</item>
    ///         <item>Binary Represntation <b>01</b>:<br/>
    ///         Indicates that the operation was partially successful and that associated results may not be suitable for some purposes.</item>
    ///         <item>Binary Represntation <b>10</b>:<br/>
    ///         Indicates that the operation failed and any associated results cannot be used.</item>
    ///         <item>Binary Represntation <b>11</b>:<br/>
    ///         Reserved for future use. All Clients should treat a status code with this severity as �Bad�.</item>
    ///     </list>
    /// </item>
    /// </list>
    /// <br/></para>
    /// </remarks>
    [DataContract(Name = "StatusCode", Namespace = Namespaces.OpcUaXsd)]
    public struct StatusCode : IComparable, IFormattable
    {
        #region Constructors

        /// <summary>
        /// Initializes the object with a numeric value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a numeric value.
        /// </remarks>
        /// <param name="code">The numeric code to apply to this status code</param>
        public StatusCode(uint code)
        {
            m_code = code;
        }

        /// <summary>
        /// Initializes the object from an exception.
        /// </summary>
        /// <remarks>
        /// Initializes the object from an exception and a numeric code. The numeric code
        /// will be determined from the Exception if possible, otherwise the value passed in
        /// will be used.
        /// </remarks>
        /// <param name="defaultCode">The default code to apply if the routine cannot determine the code from the Exception</param>
        /// <param name="e">The exception to convert to a status code</param>
        public StatusCode(Exception e, uint defaultCode)
        {
            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
            {
                m_code = sre.StatusCode;
            }
            else
            {
                m_code = defaultCode;
            }
        }
        #endregion

        #region Public Properties
        #region public uint Code
        /// <summary>
        /// The entire 32-bit status value.
        /// </summary>
        /// <remarks>
        /// The entire 32-bit status value.
        /// </remarks>        
        [DataMember(Name = "Code", Order = 1, IsRequired = false)]
        public uint Code
        {
            get { return m_code; }
            set { m_code = value; }
        }
        #endregion

        #region public uint CodeBits
        /// <summary>
        /// The 16 code bits of the status code. 
        /// </summary>
        /// <remarks>
        /// The 16 code bits of the status code. 
        /// </remarks>
        public uint CodeBits => m_code & 0xFFFF0000;

        /// <summary>
        /// Returns a copy of the status code with the Code bits set.
        /// </summary>
        /// <param name="bits">The value for the Code bits.</param>
        /// <returns>The status code with the Code bits set to the specified values.</returns>
        public StatusCode SetCodeBits(uint bits)
        {
            m_code &= 0x0000FFFF;
            m_code |= (bits & 0xFFFF0000);

            return this;
        }
        #endregion

        #region public uint FlagBits
        /// <summary>
        /// The 16 flag bits of the status code. 
        /// </summary>
        /// <remarks>
        /// The 16 flag bits of the status code. 
        /// </remarks>
        public uint FlagBits => m_code & 0x0000FFFF;

        /// <summary>
        /// Returns a copy of the status code with the Flag bits set.
        /// </summary>
        /// <param name="bits">The value for the Flag bits.</param>
        /// <returns>The status code with the Flag bits set to the specified values.</returns>
        public StatusCode SetFlagBits(uint bits)
        {
            m_code &= 0xFFFF0000;
            m_code |= ((uint)bits & 0x0000FFFF);

            return this;
        }
        #endregion

        #region public uint SubCode
        /// <summary>
        /// The sub-code portion of the status code. 
        /// </summary>
        /// <remarks>
        /// The sub-code portion of the status code. 
        /// </remarks>
        public uint SubCode
        {
            get { return m_code & 0x0FFF000; }
            set { m_code = 0x0FFF000 & value; }
        }
        #endregion

        #region public bool StructureChanged
        /// <summary>
        /// Set to indicate that the structure of the data value has changed.
        /// </summary>
        /// <remarks>
        /// Set to indicate that the structure of the data value has changed.
        /// </remarks>
        [XmlIgnore()]
        public bool StructureChanged
        {
            get { return (m_code & s_StructureChangedBit) != 0; }

            set
            {
                if (value)
                {
                    m_code |= s_StructureChangedBit;
                }
                else
                {
                    m_code &= ~s_StructureChangedBit;
                }
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the StructureChanged bit set.
        /// </summary>
        /// <param name="structureChanged">The value for the StructureChanged bit.</param>
        /// <returns>The status code with the StructureChanged bit set to the specified value.</returns>
        public StatusCode SetStructureChanged(bool structureChanged)
        {
            this.StructureChanged = structureChanged;
            return this;
        }
        #endregion

        #region public bool SemanticsChanged
        /// <summary>
        /// Set to indicate that the semantics associated with the data value have changed.
        /// </summary>
        /// <remarks>
        /// Set to indicate that the semantics associated with the data value have changed.
        /// </remarks>
        [XmlIgnore()]
        public bool SemanticsChanged
        {
            get { return (m_code & s_SemanticsChangedBit) != 0; }

            set
            {
                if (value)
                {
                    m_code |= s_SemanticsChangedBit;
                }
                else
                {
                    m_code &= ~s_SemanticsChangedBit;
                }
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the SemanticsChanged bit set.
        /// </summary>
        /// <param name="semanticsChanged">The value for the SemanticsChanged bit.</param>
        /// <returns>The status code with the SemanticsChanged bit set to the specified value.</returns>
        public StatusCode SetSemanticsChanged(bool semanticsChanged)
        {
            this.SemanticsChanged = semanticsChanged;
            return this;
        }
        #endregion

        #region public bool HasDataValueInfo
        /// <summary>
        /// The bits that indicate the meaning of the status code
        /// </summary>
        /// <remarks>
        /// The bits that indicate the meaning of the status code
        /// </remarks>
        [XmlIgnore()]
        public bool HasDataValueInfo
        {
            get { return (m_code & s_DataValueInfoType) != 0; }

            set
            {
                if (value)
                {
                    m_code |= s_DataValueInfoType;
                }
                else
                {
                    m_code &= ~s_DataValueInfoType;
                    m_code &= 0xFFFFFC00;
                }
            }
        }


        #endregion

        #region public LimitBits LimitBits
        /// <summary>
        /// The limit bits, indicating Hi/Lo etc.
        /// </summary>
        /// <remarks>
        /// The limit bits, indicating Hi/Lo etc.
        /// </remarks>
        /// <seealso cref="LimitBits"/>
        [XmlIgnore()]
        public LimitBits LimitBits
        {
            get { return (LimitBits)(m_code & s_LimitBits); }

            set
            {
                m_code |= s_DataValueInfoType;
                m_code &= ~s_LimitBits;
                m_code |= ((uint)value & s_LimitBits);
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the llimit bits set.
        /// </summary>
        /// <param name="bits">The value for the limits bits</param>
        /// <returns>The status code with the limit bits set to the specified values.</returns>
        public StatusCode SetLimitBits(LimitBits bits)
        {
            this.LimitBits = bits;
            return this;
        }
        #endregion

        #region public bool Overflow
        /// <summary>
        /// The overflow bit.
        /// </summary>
        /// <remarks>
        /// Specifies if there is an overflow or not
        /// </remarks>
        [XmlIgnore()]
        public bool Overflow
        {
            get { return ((m_code & s_DataValueInfoType) != 0) && ((m_code & s_OverflowBit) != 0); }

            set
            {
                m_code |= s_DataValueInfoType;

                if (value)
                {
                    m_code |= s_OverflowBit;
                }
                else
                {
                    m_code &= ~s_OverflowBit;
                }
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the overflow bit set.
        /// </summary>
        /// <param name="overflow">The value for the overflow bit.</param>
        /// <returns>The status code with the overflow bit set to the specified value.</returns>
        public StatusCode SetOverflow(bool overflow)
        {
            this.Overflow = overflow;
            return this;
        }
        #endregion

        #region public AggregateBits AggregateBits
        /// <summary>
        /// The historian bits.
        /// </summary>
        /// <remarks>
        /// The historian bits.
        /// </remarks>
        /// <seealso cref="AggregateBits"/>
        [XmlIgnore()]
        public AggregateBits AggregateBits
        {
            get { return (AggregateBits)(m_code & s_AggregateBits); }

            set
            {
                m_code |= s_DataValueInfoType;
                m_code &= ~s_AggregateBits;
                m_code |= ((uint)value & s_AggregateBits);
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the aggregate bits set.
        /// </summary>
        /// <param name="bits">The bits to set.</param>
        /// <returns>The status code with the aggregate bits set to the specified values.</returns>
        public StatusCode SetAggregateBits(AggregateBits bits)
        {
            this.AggregateBits = bits;
            return this;
        }
        #endregion
        #endregion

        #region IComparable Members
        /// <summary>
        /// Compares the instance to another object.
        /// </summary>
        /// <remarks>
        /// Compares the instance to another object.
        /// </remarks>
        /// <param name="obj">The object to compare to *this* object</param>
        public int CompareTo(object obj)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(obj, this))
            {
                return 0;
            }

            // check for null.
            if (obj == null)
            {
                return +1;
            }

            // check for status code.
            if (obj is uint)
            {
                return m_code.CompareTo((uint)obj);
            }

            // compare codes.
            if (obj is StatusCode)
            {
                return m_code.CompareTo(((StatusCode)obj).m_code);
            }

            // objects not comparable.
            return -1;
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <param name="format">(Unused) The format of the string. Always specify null for this parameter</param>
        /// <param name="formatProvider">The provider to use for the formatting</param>
        /// <exception cref="FormatException">Thrown if you specify a value for the Format parameter</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                string text = StatusCodes.GetBrowseName(m_code & 0xFFFF0000);

                if (!String.IsNullOrEmpty(text))
                {
                    return String.Format(formatProvider, "{0}", text);
                }

                return String.Format(formatProvider, "0x{0:X8}", m_code);

            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        /// <param name="obj">The object to compare to *this* object</param>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        /// <remarks>
        /// Returns a unique hashcode for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            return m_code.GetHashCode();
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        /// <remarks>
        /// Converts the value to a human readable string.
        /// </remarks>
        public override string ToString()
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(LookupSymbolicId(m_code));

            if ((0x0000FFFF & Code) != 0)
            {
                buffer.AppendFormat(" [{0:X4}]", (0x0000FFFF & Code));
            }

            return buffer.ToString();
        }
        #endregion

        #region Static Members
        /// <summary>
        /// Converts a 32-bit code a StatusCode object.
        /// </summary>
        /// <remarks>
        /// Converts a 32-bit code a StatusCode object.
        /// </remarks>
        /// <param name="code">The code to convert to a StatusCode</param>
        public static implicit operator StatusCode(uint code)
        {
            return new StatusCode(code);
        }

        /// <summary>
        /// Converts a StatusCode object to a 32-bit code.
        /// </summary>
        /// <remarks>
        /// Converts a StatusCode object to a 32-bit code.
        /// </remarks>
        /// <param name="code">The StatusCode to convert to a 32-but number</param>
        public static explicit operator uint(StatusCode code)
        {
            return code.Code;
        }

        /// <summary>
        /// Looks up the symbolic name for a status code.
        /// </summary>
        /// <remarks>
        /// Looks up the symbolic name for a status code.
        /// </remarks>
        /// <param name="code">The numeric error-code to convert to a textual description</param>
        public static string LookupSymbolicId(uint code)
        {
            return StatusCodes.GetBrowseName(code & 0xFFFF0000);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator ==(StatusCode a, StatusCode b)
        {
            if (Object.ReferenceEquals(a, null))
            {
                return Object.ReferenceEquals(b, null);
            }

            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator !=(StatusCode a, StatusCode b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator ==(StatusCode a, uint b)
        {
            if (Object.ReferenceEquals(a, null))
            {
                return false;
            }

            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator !=(StatusCode a, uint b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Returns true if the object a is less than object b.
        /// </summary>
        /// <remarks>
        /// Returns true if the object a is less than object b.
        /// </remarks>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator <(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <summary>
        /// Returns true if the object a is greater than object b.
        /// </summary>
        /// <remarks>
        /// Returns true if the object a is greater than object b.
        /// </remarks>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator >(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        /// <remarks>
        /// Returns true if the status code is good.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsGood(StatusCode code)
        {
            return (code.m_code & 0xC0000000) == 0;
        }

        /// <summary>
        /// Returns true if the status is bad or uncertain.
        /// </summary>
        /// <remarks>
        /// Returns true if the status is bad or uncertain.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsNotGood(StatusCode code)
        {
            return (code.m_code & 0xC0000000) != 0;
        }

        /// <summary>
        /// Returns true if the status code is uncertain.
        /// </summary>
        /// <remarks>
        /// Returns true if the status code is uncertain.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsUncertain(StatusCode code)
        {
            return (code.m_code & 0x40000000) == 0x40000000;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <remarks>
        /// Returns true if the status is good or uncertain.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsNotUncertain(StatusCode code)
        {
            return (code.m_code & 0x40000000) != 0x40000000;
        }

        /// <summary>
        /// Returns true if the status code is bad.
        /// </summary>
        /// <remarks>
        /// Returns true if the status code is bad.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsBad(StatusCode code)
        {
            return (code.m_code & 0x80000000) != 0;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <remarks>
        /// Returns true if the status is good or uncertain.
        /// </remarks>
        /// <param name="code">The code to check</param>
        public static bool IsNotBad(StatusCode code)
        {
            return (code.m_code & 0x80000000) == 0;
        }
        #endregion

        #region Private Members
        private uint m_code;

        private const uint s_AggregateBits = 0x001F;
        private const uint s_OverflowBit = 0x0080;
        private const uint s_LimitBits = 0x0300;
        private const uint s_DataValueInfoType = 0x0400;
        private const uint s_SemanticsChangedBit = 0x4000;
        private const uint s_StructureChangedBit = 0x8000;
        #endregion
    }

    #region LimitBits Enumeration
    /// <summary>
    /// Flags that are set to indicate the limit status of the value.
    /// </summary>
    [Flags]
    public enum LimitBits : int
    {
        /// <summary>
        /// The value is free to change.
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// The value is at the lower limit for the data source.
        /// </summary>
        Low = 0x0100,

        /// <summary>
        /// The value is at the higher limit for the data source.
        /// </summary>
        High = 0x0200,

        /// <summary>
        /// The value is constant and cannot change.
        /// </summary>
        Constant = 0x0300
    }
    #endregion

    #region AggregateBits Enumeration
    /// <summary>
    /// Flags that are set by the historian when returning archived values.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue"), Flags]
    public enum AggregateBits : int
    {
        /// <summary>
        /// A raw data value.
        /// </summary>
        Raw = 0x00,

        /// <summary>
        /// A raw data value.
        /// </summary>
        Calculated = 0x01,

        /// <summary>
        /// A data value which was interpolated.
        /// </summary>
        Interpolated = 0x02,

        /// <summary>
        /// A mask that selects the bit which identify the source of the value (raw, calculated, interpolated).
        /// </summary>
        DataSourceMask = 0x03,

        /// <summary>
        /// A data value which was calculated with an incomplete interval.
        /// </summary>
        Partial = 0x04,

        /// <summary>
        /// A raw data value that hides other data at the same timestamp.
        /// </summary>
        ExtraData = 0x08,

        /// <summary>
        /// Multiple values match the aggregate criteria (i.e. multiple minimum values at different timestamps within the same interval)
        /// </summary>
        MultipleValues = 0x10,
    }
    #endregion

    #region StatusCodeCollection Class
    /// <summary>
    /// A collection of StatusCodes.
    /// </summary>
    [CollectionDataContract(Name = "ListOfStatusCode", Namespace = Namespaces.OpcUaXsd, ItemName = "StatusCode")]
    public partial class StatusCodeCollection : List<StatusCode>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public StatusCodeCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection to copy</param>
        public StatusCodeCollection(IEnumerable<StatusCode> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The maximum capacity allowed for this instance of the collection</param>
        public StatusCodeCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">The array of <see cref="StatusCode"/> values to return as a Collection</param>
        public static StatusCodeCollection ToStatusCodeCollection(StatusCode[] values)
        {
            if (values != null)
            {
                return new StatusCodeCollection(values);
            }

            return new StatusCodeCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">The array of <see cref="StatusCode"/> values to return as a Collection</param>
        public static implicit operator StatusCodeCollection(StatusCode[] values)
        {
            return ToStatusCodeCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new StatusCodeCollection(this);
        }
    }
    #endregion

    #region Standard StatusCodes
    /// <summary>
    /// Defines standard status codes.
    /// </summary>
    /// <remarks>
    /// Defines standard status codes.
    /// </remarks>
    public static partial class StatusCodes
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        public const uint Good = 0x00000000;

        /// <summary>
        /// The operation completed however its outputs may not be usable.
        /// </summary>
        public const uint Uncertain = 0x40000000;

        /// <summary>
        /// The operation failed.
        /// </summary>
        public const uint Bad = 0x80000000;
    }
    #endregion

}//namespace
