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
using System.Globalization;
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
    ///         <item>Binary Representation <b>00</b>:<br/>
    ///         Indicates that the operation was successful and the associated results may be used.</item>
    ///         <item>Binary Representation <b>01</b>:<br/>
    ///         Indicates that the operation was partially successful and that associated results may not be suitable for some purposes.</item>
    ///         <item>Binary Representation <b>10</b>:<br/>
    ///         Indicates that the operation failed and any associated results cannot be used.</item>
    ///         <item>Binary Representation <b>11</b>:<br/>
    ///         Reserved for future use. All Clients should treat a status code with this severity as �Bad�.</item>
    ///     </list>
    /// </item>
    /// </list>
    /// <br/></para>
    /// </remarks>
    [DataContract(Name = "StatusCode", Namespace = Namespaces.OpcUaXsd)]
    public struct StatusCode :
        IComparable,
        IFormattable,
        IComparable<StatusCode>,
        IEquatable<StatusCode>
    {
        /// <summary>
        /// Initializes the object with a numeric value.
        /// </summary>
        /// <param name="code">The numeric code to apply to this status code</param>
        public StatusCode(uint code)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes the object from an exception.
        /// </summary>
        /// <remarks>
        /// Initializes the object from an exception and a numeric code. The numeric code
        /// will be determined from the Exception if possible, otherwise the value passed in
        /// will be used.
        /// </remarks>
        /// <param name="e">The exception to convert to a status code</param>
        /// <param name="defaultCode">The default code to apply if the routine cannot determine the code from the Exception</param>
        public StatusCode(Exception e, uint defaultCode)
        {
            if (e is ServiceResultException sre)
            {
                Code = sre.StatusCode;
            }
            else
            {
                Code = defaultCode;
            }
        }

        /// <summary>
        /// The entire 32-bit status value.
        /// </summary>
        /// <remarks>
        /// The entire 32-bit status value.
        /// </remarks>
        [DataMember(Name = "Code", Order = 1, IsRequired = false)]
        public uint Code { get; set; }

        /// <summary>
        /// The 16 code bits of the status code.
        /// </summary>
        /// <remarks>
        /// The 16 code bits of the status code.
        /// </remarks>
        public readonly uint CodeBits => Code & 0xFFFF0000;

        /// <summary>
        /// Returns a copy of the status code with the Code bits set.
        /// </summary>
        /// <param name="bits">The value for the Code bits.</param>
        /// <returns>The status code with the Code bits set to the specified values.</returns>
        public StatusCode SetCodeBits(uint bits)
        {
            Code &= 0x0000FFFF;
            Code |= bits & 0xFFFF0000;

            return this;
        }

        /// <summary>
        /// The 16 flag bits of the status code.
        /// </summary>
        /// <remarks>
        /// The 16 flag bits of the status code.
        /// </remarks>
        public readonly uint FlagBits => Code & 0x0000FFFF;

        /// <summary>
        /// Returns a copy of the status code with the Flag bits set.
        /// </summary>
        /// <param name="bits">The value for the Flag bits.</param>
        /// <returns>The status code with the Flag bits set to the specified values.</returns>
        public StatusCode SetFlagBits(uint bits)
        {
            Code &= 0xFFFF0000;
            Code |= bits & 0x0000FFFF;

            return this;
        }

        /// <summary>
        /// The sub-code portion of the status code.
        /// </summary>
        /// <remarks>
        /// The sub-code portion of the status code.
        /// </remarks>
        public uint SubCode
        {
            readonly get => Code & 0x0FFF0000;
            set => Code = 0x0FFF0000 & value;
        }

        /// <summary>
        /// Set to indicate that the structure of the data value has changed.
        /// </summary>
        /// <remarks>
        /// Set to indicate that the structure of the data value has changed.
        /// </remarks>
        [XmlIgnore]
        public bool StructureChanged
        {
            readonly get => (Code & kStructureChangedBit) != 0;
            set
            {
                if (value)
                {
                    Code |= kStructureChangedBit;
                }
                else
                {
                    Code &= ~kStructureChangedBit;
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
            StructureChanged = structureChanged;
            return this;
        }

        /// <summary>
        /// Set to indicate that the semantics associated with the data value have changed.
        /// </summary>
        /// <remarks>
        /// Set to indicate that the semantics associated with the data value have changed.
        /// </remarks>
        [XmlIgnore]
        public bool SemanticsChanged
        {
            readonly get => (Code & kSemanticsChangedBit) != 0;
            set
            {
                if (value)
                {
                    Code |= kSemanticsChangedBit;
                }
                else
                {
                    Code &= ~kSemanticsChangedBit;
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
            SemanticsChanged = semanticsChanged;
            return this;
        }

        /// <summary>
        /// The bits that indicate the meaning of the status code
        /// </summary>
        /// <remarks>
        /// The bits that indicate the meaning of the status code
        /// </remarks>
        [XmlIgnore]
        public bool HasDataValueInfo
        {
            readonly get => (Code & kDataValueInfoType) != 0;
            set
            {
                if (value)
                {
                    Code |= kDataValueInfoType;
                }
                else
                {
                    Code &= ~kDataValueInfoType;
                    Code &= 0xFFFFFC00;
                }
            }
        }

        /// <summary>
        /// The limit bits, indicating Hi/Lo etc.
        /// </summary>
        /// <seealso cref="LimitBits"/>
        [XmlIgnore]
        public LimitBits LimitBits
        {
            readonly get => (LimitBits)(Code & kLimitBits);
            set
            {
                Code |= kDataValueInfoType;
                Code &= ~kLimitBits;
                Code |= (uint)value & kLimitBits;
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the limit bits set.
        /// </summary>
        /// <param name="bits">The value for the limits bits</param>
        /// <returns>The status code with the limit bits set to the specified values.</returns>
        public StatusCode SetLimitBits(LimitBits bits)
        {
            LimitBits = bits;
            return this;
        }

        /// <summary>
        /// The overflow bit.
        /// </summary>
        /// <remarks>
        /// Specifies if there is an overflow or not
        /// </remarks>
        [XmlIgnore]
        public bool Overflow
        {
            readonly get => ((Code & kDataValueInfoType) != 0) && ((Code & kOverflowBit) != 0);
            set
            {
                Code |= kDataValueInfoType;

                if (value)
                {
                    Code |= kOverflowBit;
                }
                else
                {
                    Code &= ~kOverflowBit;
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
            Overflow = overflow;
            return this;
        }

        /// <summary>
        /// The historian bits.
        /// </summary>
        /// <seealso cref="AggregateBits"/>
        [XmlIgnore]
        public AggregateBits AggregateBits
        {
            readonly get => (AggregateBits)(Code & kAggregateBits);
            set
            {
                Code |= kDataValueInfoType;
                Code &= ~kAggregateBits;
                Code |= (uint)value & kAggregateBits;
            }
        }

        /// <summary>
        /// Returns a copy of the status code with the aggregate bits set.
        /// </summary>
        /// <param name="bits">The bits to set.</param>
        /// <returns>The status code with the aggregate bits set to the specified values.</returns>
        public StatusCode SetAggregateBits(AggregateBits bits)
        {
            AggregateBits = bits;
            return this;
        }

        /// <summary>
        /// Compares the instance to another object.
        /// </summary>
        /// <param name="obj">The object to compare to *this* object</param>
        public readonly int CompareTo(object obj)
        {
            // compare codes
            if (obj is StatusCode statusCode)
            {
                return Code.CompareTo(statusCode.Code);
            }

            // check for null.
            if (obj == null)
            {
                return +1;
            }

            // check for status code.
            if (obj is uint code)
            {
                return Code.CompareTo(code);
            }

            // objects not comparable.
            return -1;
        }

        /// <summary>
        /// Compares the instance to another object.
        /// </summary>
        /// <param name="other">The StatusCode to compare to *this* object</param>
        public readonly int CompareTo(StatusCode other)
        {
            // check for status code.
            return Code.CompareTo(other.Code);
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused) The format of the string. Always specify null for this parameter</param>
        /// <param name="formatProvider">The provider to use for the formatting</param>
        /// <exception cref="FormatException">Thrown if you specify a value for the Format parameter</exception>
        public readonly string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
#if ZOMBIE // Manual
                string text = StatusCodes.GetBrowseName(Code & 0xFFFF0000);

                if (!string.IsNullOrEmpty(text))
                {
                    return string.Format(formatProvider, "{0}", text);
                }
#endif

                return string.Format(formatProvider, "0x{0:X8}", Code);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <param name="obj">The object to compare to *this* object</param>
        public override readonly bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <param name="other">The StatusCode to compare to *this* object</param>
        public readonly bool Equals(StatusCode other)
        {
            return CompareTo(other) == 0;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        /// <remarks>
        /// Returns a unique hashcode for the object.
        /// </remarks>
        public override readonly int GetHashCode()
        {
            return Code.GetHashCode();
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        /// <remarks>
        /// Converts the value to a human readable string.
        /// </remarks>
        public override readonly string ToString()
        {
            var buffer = new StringBuilder();

#if ZOMBIE // Manual
            buffer.Append(LookupSymbolicId(Code));
#endif
            if ((0x0000FFFF & Code) != 0)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, " [{0:X4}]", 0x0000FFFF & Code);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Converts a 32-bit code a StatusCode object.
        /// </summary>
        /// <param name="code">The code to convert to a StatusCode</param>
        public static implicit operator StatusCode(uint code)
        {
            return new StatusCode(code);
        }

        /// <summary>
        /// Converts a StatusCode object to a 32-bit code.
        /// </summary>
        /// <param name="code">The StatusCode to convert to a 32-but number</param>
        public static explicit operator uint(StatusCode code)
        {
            return code.Code;
        }

#if ZOMBIE // Manual
        /// <summary>
        /// Looks up the symbolic name for a status code.
        /// </summary>
        /// <param name="code">The numeric error-code to convert to a textual description</param>
        public static string LookupSymbolicId(uint code)
        {
            return StatusCodes.GetBrowseName(code);
        }

        /// <summary>
        /// Looks up the Utf8 encoded symbolic name for a status code.
        /// </summary>
        /// <param name="code">The numeric error-code to convert to a textual description</param>
        public static byte[] LookupUtf8SymbolicId(uint code)
        {
            return StatusCodes.GetUtf8BrowseName(code);
        }
#endif

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator ==(StatusCode a, StatusCode b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator !=(StatusCode a, StatusCode b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">The first object being compared</param>
        /// <param name="b">The second object being compared to</param>
        public static bool operator ==(StatusCode a, uint b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator !=(StatusCode a, uint b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Returns true if the object a is less than object b.
        /// </summary>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator <(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <summary>
        /// Returns true if the object a is greater than object b.
        /// </summary>
        /// <param name="a">The first value being compared</param>
        /// <param name="b">The second value being compared to</param>
        public static bool operator >(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsGood(StatusCode code)
        {
            return (code.Code & 0xC0000000) == 0;
        }

        /// <summary>
        /// Returns true if the status is bad or uncertain.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsNotGood(StatusCode code)
        {
            return (code.Code & 0xC0000000) != 0;
        }

        /// <summary>
        /// Returns true if the status code is uncertain.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsUncertain(StatusCode code)
        {
            return (code.Code & 0x40000000) == 0x40000000;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsNotUncertain(StatusCode code)
        {
            return (code.Code & 0x40000000) != 0x40000000;
        }

        /// <summary>
        /// Returns true if the status code is bad.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsBad(StatusCode code)
        {
            return (code.Code & 0x80000000) != 0;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <param name="code">The code to check</param>
        public static bool IsNotBad(StatusCode code)
        {
            return (code.Code & 0x80000000) == 0;
        }

        private const uint kAggregateBits = 0x001F;
        private const uint kOverflowBit = 0x0080;
        private const uint kLimitBits = 0x0300;
        private const uint kDataValueInfoType = 0x0400;
        private const uint kSemanticsChangedBit = 0x4000;
        private const uint kStructureChangedBit = 0x8000;
    }

    /// <summary>
    /// Flags that are set to indicate the limit status of the value.
    /// </summary>
    [Flags]
    public enum LimitBits
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
        Constant = Low | High
    }

    /// <summary>
    /// Flags that are set by the historian when returning archived values.
    /// </summary>
    [Flags]
    public enum AggregateBits
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
        DataSourceMask = Calculated | Interpolated,

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
        MultipleValues = 0x10
    }

    /// <summary>
    /// A collection of StatusCodes.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStatusCode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StatusCode")]
    public class StatusCodeCollection : List<StatusCode>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public StatusCodeCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The collection to copy</param>
        public StatusCodeCollection(IEnumerable<StatusCode> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The maximum capacity allowed for this instance of the collection</param>
        public StatusCodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The array of <see cref="StatusCode"/> values to return as a Collection</param>
        public static StatusCodeCollection ToStatusCodeCollection(StatusCode[] values)
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
        /// <param name="values">The array of <see cref="StatusCode"/> values to return as a Collection</param>
        public static implicit operator StatusCodeCollection(StatusCode[] values)
        {
            return ToStatusCodeCollection(values);
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
            return new StatusCodeCollection(this);
        }
    }

    /// <summary>
    /// Defines standard status codes.
    /// </summary>
    internal static partial class StatusCodes
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
}
