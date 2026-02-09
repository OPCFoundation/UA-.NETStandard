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
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Opc.Ua.Types;
using System.Text.Json.Serialization;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// A numeric code that describes the result of a service or operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The StatusCode is defined in <b>OPC UA Specifications Part 4: Services, section
    /// 7.22</b> titled <b>StatusCode</b>.<br/>
    /// <br/></para>
    /// <para>
    /// A numeric code that is used to describe the result of a service or operation.
    /// The StatusCode uses bit-assignments, which are described below.
    /// <br/></para>
    /// <para>
    /// The StatusCode is a 32-bit number, with the top 16-bits (high word) representing
    /// the numeric value of an error or condition, whereas the bottom 16-bits (low word)
    /// represents additional flags to provide more information on the meaning of the
    /// status code.
    /// <br/></para>
    /// <para>
    /// The following list shows the bit-assignments, i.e. 0-7 means bit 0 to bit 7.
    /// <list type="number">
    /// <item><b>0 - 7</b>:<br/>
    /// InfoBits. Additional information bits that qualify the status code.</item>
    /// <item><b>8 - 9</b>:<br/>
    /// The type of information contained in the info bits. These bits have the following
    /// meanings:<br/>
    ///     <list type="bullet">
    ///         <item>Binary Representation <b>00</b>:<br/>
    ///         The info bits are not used and must be set to zero.</item>
    ///         <item>Binary Representation <b>01</b>:<br/>
    ///         The status code and its info bits are associated with a data value
    ///         returned from the Server.</item>
    ///         <item>Binary Representation <b>10</b> or <b>11</b>:<br/>
    ///         Reserved for future use. The info bits must be ignored.</item>
    ///     </list>
    /// <br/></item>
    /// <item><b>10 - 13</b>:<br/>
    /// Reserved for future use. Must always be zero.</item>
    /// <item><b>14</b>:
    /// Indicates that the semantics of the associated data value have changed. Clients
    /// should not process the data value until they re-read the metadata associated
    /// with the Variable.
    /// Servers should set this bit if the metadata has changed in way that could case
    /// application errors if the Client does not re-read the metadata.
    /// For example, a change to the engineering units could create problems if the
    /// Client uses the value to perform calculations.
    /// [UA Part 8] defines the conditions where a Server must set this bit for a DA
    /// Variable. Other specifications may define additional conditions. A Server may
    /// define other conditions that cause this bit to be set.
    /// This bit only has meaning for status codes returned as part of a data change
    /// Notification.
    /// Status codes used in other contexts must always set this bit to zero.
    /// </item>
    /// <item><b>15</b>:<br/>
    /// Indicates that the structure of the associated data value has changed since
    /// the last Notification.
    /// Clients should not process the data value unless they re-read the metadata.
    /// Servers must set this bit if the DataTypeEncoding used for a Variable changes.
    /// Clause 7.14 describes how the DataTypeEncoding is specified for a Variable.
    /// The bit is also set if the data type Attribute of the Variable changes.
    /// A Variable with data type BaseDataType does not require the bit to be set when
    /// the data type changes.
    /// Servers must also set this bit if the length of a fixed length array Variable
    /// changes.
    /// This bit is provided to warn Clients that parse complex data values that
    /// their parsing routines could fail because the serialized form of the data value
    /// has changed.
    /// This bit only has meaning for status codes returned as part of a data change
    /// Notification.
    /// Status codes used in other contexts must always set this bit to zero.</item>
    /// <item><b>16 - 27</b>:<br/>
    /// The code is a numeric value assigned to represent different conditions.
    /// Each code has a symbolic name and a numeric value. All descriptions in the UA
    /// specification refer to the symbolic name. [UA Part 6] maps the symbolic names
    /// onto a numeric value.</item>
    /// <item><b>28 - 29</b>:<br/>
    /// Reserved for future use. Must always be zero.</item>
    /// <item><b>30 - 31</b>:<br/>
    /// Indicates whether the status code represents a good, bad or uncertain condition.
    /// These bits have the following meanings:<br/>
    ///     <list type="bullet">
    ///         <item>Binary Representation <b>00</b>:<br/>
    ///         Indicates that the operation was successful and the associated results
    ///         may be used.</item>
    ///         <item>Binary Representation <b>01</b>:<br/>
    ///         Indicates that the operation was partially successful and that
    ///         associated results may not be suitable for some purposes.</item>
    ///         <item>Binary Representation <b>10</b>:<br/>
    ///         Indicates that the operation failed and any associated results
    ///         cannot be used.</item>
    ///         <item>Binary Representation <b>11</b>:<br/>
    ///         Reserved for future use. All Clients should treat a status code with
    ///         this severity as "Bad".</item>
    ///     </list>
    /// </item>
    /// </list>
    /// <br/></para>
    /// </remarks>
    public readonly struct StatusCode :
        IFormattable,
        IComparable,
        IComparable<StatusCode>,
        IComparable<uint>,
        IEquatable<StatusCode>,
        IEquatable<uint>
    {
        /// <summary>
        /// Initializes the object with a numeric value.
        /// </summary>
        /// <param name="code">The numeric code to apply to this status code</param>
        public StatusCode(uint code)
        {
            if (TryGetInternedStatusCode(code, out StatusCode s))
            {
                this = s;
            }
            else
            {
                SymbolicId = null;
            }
            // Set code again which could be containing more than code bits
            Code = code;
        }

        /// <summary>
        /// Initializes the object with a numeric value.
        /// </summary>
        /// <param name="code">The numeric code to apply to this status code</param>
        /// <param name="symbolicId">The symbol for the status code</param>
        [JsonConstructor]
        public StatusCode(uint code, string symbolicId)
        {
            if (symbolicId == null &&
                TryGetInternedStatusCode(code, out StatusCode s))
            {
                this = s;
            }
            else
            {
                SymbolicId = symbolicId != null ?
                    string.Intern(symbolicId) :
                    null;
            }
            // Set code again which could be containing more than code bits
            Code = code;
        }

        /// <summary>
        /// Initializes the object from an exception.
        /// </summary>
        /// <remarks>
        /// Initializes the object from an exception and a numeric code.
        /// The numeric code will be determined from the Exception if possible,
        /// otherwise the value passed in will be used.
        /// </remarks>
        /// <param name="e">The exception to convert to a status code</param>
        /// <param name="defaultCode">The default code to apply if the routine
        /// cannot determine the code from the Exception</param>
        /// <param name="symbolicId">The optional symbol</param>
        public StatusCode(Exception e, uint defaultCode, string symbolicId)
        {
            if (e is ServiceResultException sre)
            {
                this = sre.StatusCode;
            }
            else
            {
                Code = defaultCode;
                SymbolicId = symbolicId != null ? string.Intern(symbolicId) : null;
            }
        }

        /// <summary>
        /// Initializes the object from an exception.
        /// </summary>
        /// <remarks>
        /// Initializes the object from an exception and a numeric code.
        /// The numeric code will be determined from the Exception if possible,
        /// otherwise the value passed in will be used.
        /// </remarks>
        /// <param name="e">The exception to convert to a status code</param>
        /// <param name="defaultCode">The default code to apply if the routine
        /// cannot determine the code from the Exception</param>
        public StatusCode(Exception e, StatusCode defaultCode)
        {
            if (e is ServiceResultException sre)
            {
                this = sre.StatusCode;
            }
            else
            {
                this = defaultCode;
            }
        }

        /// <summary>
        /// The entire 32-bit status value.
        /// </summary>
        public uint Code { get; }

        /// <summary>
        /// Returns a copy of the status code with the Code but
        /// current symbolic id.
        /// </summary>
        public StatusCode SetCode(uint code)
        {
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The symbolic name for the code bits of the status code.
        /// This value is not serialized and lost on deserialization.
        /// </summary>
        public string SymbolicId { get; }

        /// <summary>
        /// The 16 code bits of the status code.
        /// </summary>
        public uint CodeBits => Code & 0xFFFF0000;

        /// <summary>
        /// Returns a copy of the status code with the Code bits set.
        /// </summary>
        /// <param name="bits">The value for the Code bits.</param>
        /// <returns>The status code with the Code bits set to the
        /// specified values.</returns>
        public StatusCode SetCodeBits(uint bits)
        {
            uint code = Code;
            code &= 0x0000FFFF;
            code |= bits & 0xFFFF0000;
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// Returns a copy of the status code with the Code bits set
        /// to the code bits of the provided status code.
        /// </summary>
        /// <param name="statusCode">The code bits of this status code.
        /// </param>
        /// <returns>The status code with the Code bits set to the
        /// specified values.</returns>
        public StatusCode SetCodeBits(StatusCode statusCode)
        {
            uint code = Code;
            code &= 0x0000FFFF;
            code |= statusCode.CodeBits & 0xFFFF0000;
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The 16 flag bits of the status code.
        /// </summary>
        /// <remarks>
        /// The 16 flag bits of the status code.
        /// </remarks>
        public uint FlagBits => Code & 0x0000FFFF;

        /// <summary>
        /// Returns a copy of the status code with the Flag bits set.
        /// </summary>
        /// <param name="bits">The value for the Flag bits.</param>
        /// <returns>The status code with the Flag bits set to the
        /// specified values.</returns>
        public StatusCode SetFlagBits(uint bits)
        {
            uint code = Code;
            code &= 0xFFFF0000;
            code |= bits & 0x0000FFFF;
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The sub-code portion of the status code.
        /// </summary>
        /// <remarks>
        /// The sub-code portion of the status code.
        /// </remarks>
        public uint SubCode => Code & 0x0FFF0000;

        /// <summary>
        /// Returns a copy of the status code with the sub code set.
        /// </summary>
        public StatusCode SetSubCode(uint subCode)
        {
            return new StatusCode(0x0FFF0000 & subCode, SymbolicId);
        }

        /// <summary>
        /// Set to indicate that the structure of the data value has changed.
        /// </summary>
        public bool StructureChanged => (Code & kStructureChangedBit) != 0;

        /// <summary>
        /// Returns a copy of the status code with the StructureChanged bit
        /// set.
        /// </summary>
        /// <param name="structureChanged">The value for the StructureChanged
        /// bit.</param>
        /// <returns>The status code with the StructureChanged bit set to
        /// the specified value.</returns>
        public StatusCode SetStructureChanged(bool structureChanged)
        {
            uint code = Code;
            if (structureChanged)
            {
                code |= kStructureChangedBit;
            }
            else
            {
                code &= ~kStructureChangedBit;
            }
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// Set to indicate that the semantics associated with the data
        /// value have changed.
        /// </summary>
        public bool SemanticsChanged => (Code & kSemanticsChangedBit) != 0;

        /// <summary>
        /// Returns a copy of the status code with the SemanticsChanged
        /// bit set.
        /// </summary>
        /// <param name="semanticsChanged">The value for the SemanticsChanged
        /// bit.</param>
        /// <returns>The status code with the SemanticsChanged bit set to
        /// the specified value.</returns>
        public StatusCode SetSemanticsChanged(bool semanticsChanged)
        {
            uint code = Code;
            if (semanticsChanged)
            {
                code |= kSemanticsChangedBit;
            }
            else
            {
                code &= ~kSemanticsChangedBit;
            }
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The bits that indicate the meaning of the status code
        /// </summary>
        public bool HasDataValueInfo => (Code & kDataValueInfoType) != 0;

        /// <summary>
        /// Sets the HasDataValueInfo bits.
        /// </summary>
        /// <returns>The status code with the bits set to specify
        /// data value</returns>
        public StatusCode SetHasDataValueInfo(bool value)
        {
            uint code = Code;
            if (value)
            {
                code |= kDataValueInfoType;
            }
            else
            {
                code &= ~kDataValueInfoType;
                code &= 0xFFFFFC00;
            }
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The limit bits, indicating Hi/Lo etc.
        /// </summary>
        /// <remarks>
        /// Requires the data value info type bit to be present.
        /// Otherwise always returns false.
        /// </remarks>
        /// <seealso cref="LimitBits"/>
        public LimitBits LimitBits => (LimitBits)(Code & kLimitBits);

        /// <summary>
        /// Returns a copy of the status code with the limit bits set.
        /// </summary>
        /// <remarks>Always adds the data value info type bit no matter.
        /// </remarks>
        /// <param name="bits">The value for the limits bits</param>
        /// <returns>The status code with the limit bits set to the
        /// specified values.</returns>
        public StatusCode SetLimitBits(LimitBits bits)
        {
            uint code = Code;
            code |= kDataValueInfoType;
            code &= ~kLimitBits;
            code |= (uint)bits & kLimitBits;
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// Specifies if there is an overflow or not
        /// </summary>
        /// <remarks>
        /// Requires the data value info type bit to be present.
        /// Otherwise always returns false.
        /// </remarks>
        public bool Overflow =>
            ((Code & kDataValueInfoType) != 0) && ((Code & kOverflowBit) != 0);

        /// <summary>
        /// Returns a copy of the status code with the overflow bit set
        /// or unsets it if false is passed.
        /// </summary>
        /// <remarks>
        /// Always adds the data value info type bit no matter.
        /// </remarks>
        /// <param name="overflow">The value for the overflow bit.</param>
        /// <returns>The status code with the overflow bit set to the
        /// specified value.</returns>
        public StatusCode SetOverflow(bool overflow)
        {
            uint code = Code;

            code |= kDataValueInfoType;
            if (overflow)
            {
                code |= kOverflowBit;
            }
            else
            {
                code &= ~kOverflowBit;
            }
            return new StatusCode(code, SymbolicId);
        }

        /// <summary>
        /// The aggregate bits.
        /// </summary>
        /// <seealso cref="AggregateBits"/>
        public AggregateBits AggregateBits =>
            (AggregateBits)(Code & kAggregateBits);

        /// <summary>
        /// Returns a copy of the status code with the aggregate bits set.
        /// </summary>
        /// <param name="bits">The bits to set.</param>
        /// <returns>The status code with the aggregate bits set to the
        /// specified values.</returns>
        public StatusCode SetAggregateBits(AggregateBits bits)
        {
            uint code = Code;
            code |= kDataValueInfoType;
            code &= ~kAggregateBits;
            code |= (uint)bits & kAggregateBits;
            return new StatusCode(code, SymbolicId);
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            // compare codes
            if (obj is StatusCode statusCode)
            {
                return Code.CompareTo(statusCode.Code);
            }

            // check for null.
            if (obj == null)
            {
                return 1;
            }

            // check for status code.
            if (obj is uint code)
            {
                return Code.CompareTo(code);
            }

            // objects not comparable.
            return -1;
        }

        /// <inheritdoc/>
        public int CompareTo(StatusCode other)
        {
            // check for status code.
            return CompareTo(other.Code);
        }

        /// <inheritdoc/>
        public int CompareTo(uint other)
        {
            // check for status code.
            return Code.CompareTo(other);
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (!string.IsNullOrEmpty(SymbolicId))
                {
                    return string.Format(formatProvider, "{0} [0x{1:X8}]", SymbolicId, Code);
                }
                return string.Format(formatProvider, "0x{0:X8}", Code);
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is StatusCode s ? Equals(s) : obj is uint code && Equals(code);
        }

        /// <inheritdoc/>
        public bool Equals(StatusCode other)
        {
            return CodeBits == other.CodeBits;
        }

        /// <inheritdoc/>
        public bool Equals(uint other)
        {
            return Code == other;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Code.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var buffer = new StringBuilder();

            if (!string.IsNullOrEmpty(SymbolicId))
            {
                buffer.Append(SymbolicId);
            }
            else
            {
                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0:X8}",
                    0xFFFF0000 & Code);
            }
            if ((0x0000FFFF & Code) != 0)
            {
                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    " [Flags: {0:X4}]",
                    0x0000FFFF & Code);
            }

            return buffer.ToString();
        }

        /// <inheritdoc/>
        public static implicit operator StatusCode(uint code)
        {
            return new StatusCode(code);
        }

        /// <inheritdoc/>
        public static explicit operator uint(StatusCode code)
        {
            return code.Code;
        }

        /// <inheritdoc/>
        public static bool operator ==(StatusCode a, StatusCode b)
        {
            return a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(StatusCode a, StatusCode b)
        {
            return !(a == b);
        }

        /// <inheritdoc/>
        public static bool operator ==(StatusCode a, uint b)
        {
            return a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(StatusCode a, uint b)
        {
            return !(a == b);
        }

        /// <inheritdoc/>
        public static bool operator <(StatusCode a, uint b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <inheritdoc/>
        public static bool operator >(StatusCode a, uint b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(StatusCode left, uint right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(StatusCode left, uint right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <inheritdoc/>
        public static bool operator <(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <inheritdoc/>
        public static bool operator >(StatusCode a, StatusCode b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(StatusCode left, StatusCode right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(StatusCode left, StatusCode right)
        {
            return left.CompareTo(right) >= 0;
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

        /// <summary>
        /// Lookup symbolic id for a status code.
        /// </summary>
        /// <param name="code"></param>
        [Obsolete("Use SymbolicId property directly.")]
        public static string LookupSymbolicId(uint code)
        {
            return TryGetInternedStatusCode(code, out StatusCode s) ?
                s.SymbolicId :
                null;
        }

        /// <summary>
        /// Looks up the Utf8 encoded symbolic name for a status code.
        /// </summary>
        /// <param name="code">The numeric error-code to convert to a textual
        /// description</param>
        public static byte[] LookupUtf8SymbolicId(uint code)
        {
            return TryGetInternedStatusCode(code, out StatusCode s) ?
                Encoding.UTF8.GetBytes(s.SymbolicId) :
                null;
        }

        /// <summary>
        /// Try get interned status code
        /// </summary>
        /// <param name="code"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static bool TryGetInternedStatusCode(
            uint code,
            out StatusCode statusCode)
        {
            return s_statusCodes.TryGetValue(code & 0xFFFF0000, out statusCode);
        }

        /// <summary>
        /// Add status codes to the internal table.
        /// </summary>
        /// <param name="statusCodes"></param>
        public static void Intern(IReadOnlyList<StatusCode> statusCodes)
        {
            var cur = s_statusCodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            foreach (StatusCode kvp in statusCodes)
            {
                if (kvp.SymbolicId == null)
                {
                    continue;
                }
                cur[kvp.Code & 0xFFFF0000] = kvp;
            }
#if NET8_0_OR_GREATER
            s_statusCodes = cur.ToFrozenDictionary();
#else
            s_statusCodes = new ReadOnlyDictionary<uint, StatusCode>(cur);
#endif
        }

        /// <summary>
        /// Gets the interned status codes
        /// </summary>
        public static StatusCodeCollection InternedStatusCodes => [.. s_statusCodes.Values];

        static StatusCode()
        {
            // Intern the Opc.Ua.Types internal status codes
            Intern(
            [
                StatusCodes.Good,
                StatusCodes.Uncertain,
                StatusCodes.Bad,
                StatusCodes.BadUnexpectedError,
                StatusCodes.BadEncodingError,
                StatusCodes.BadDecodingError,
                StatusCodes.BadEncodingLimitsExceeded,
                StatusCodes.BadUserAccessDenied,
                StatusCodes.BadTooManyArguments,
                StatusCodes.BadWaitingForInitialData,
                StatusCodes.BadNodeIdInvalid,
                StatusCodes.BadNodeIdUnknown,
                StatusCodes.BadAttributeIdInvalid,
                StatusCodes.BadIndexRangeInvalid,
                StatusCodes.BadIndexRangeNoData,
                StatusCodes.BadDataEncodingInvalid,
                StatusCodes.BadDataEncodingUnsupported,
                StatusCodes.BadNotReadable,
                StatusCodes.BadNotWritable,
                StatusCodes.BadNotSupported,
                StatusCodes.BadNotImplemented,
                StatusCodes.BadConfigurationError,
                StatusCodes.BadStructureMissing,
                StatusCodes.BadBrowseNameInvalid,
                StatusCodes.BadWriteNotSupported,
                StatusCodes.BadTypeMismatch,
                StatusCodes.BadArgumentsMissing,
                StatusCodes.BadNotExecutable,
                StatusCodes.BadInvalidArgument,
                StatusCodes.BadSyntaxError
            ]);
        }

#if NET8_0_OR_GREATER
#pragma warning disable IDE0301 // Cannot use collection initializer for FrozenDictionary
        private static FrozenDictionary<uint, StatusCode> s_statusCodes =
            FrozenDictionary<uint, StatusCode>.Empty;
#pragma warning restore IDE0301
#else
        private static ReadOnlyDictionary<uint, StatusCode> s_statusCodes
            = new(new Dictionary<uint, StatusCode>());
#endif

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
        /// A mask that selects the bit which identify the source of the
        /// value (raw, calculated, interpolated).
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
        /// Multiple values match the aggregate criteria (i.e. multiple minimum
        /// values at different timestamps within the same interval)
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
        /// <inheritdoc/>
        public StatusCodeCollection()
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(IEnumerable<StatusCode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The array of <see cref="StatusCode"/>
        /// values to return as a Collection</param>
        public static StatusCodeCollection ToStatusCodeCollection(
            StatusCode[] values)
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
        public static implicit operator StatusCodeCollection(
            StatusCode[] values)
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
    /// Helper to allow data contract serialization of StatusCode
    /// </summary>
    [DataContract(
        Name = "StatusCode",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableStatusCode : ISurrogateFor<StatusCode>
    {
        /// <inheritdoc/>
        public SerializableStatusCode()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableStatusCode(StatusCode value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public StatusCode Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The entire 32-bit status value.
        /// </summary>
        [DataMember(Name = "Code", Order = 1, IsRequired = false)]
        public uint Code
        {
            get => Value.Code;
            set => Value = new StatusCode(value);
        }
    }
}
