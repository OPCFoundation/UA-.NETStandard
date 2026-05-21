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
using System.Runtime.InteropServices;
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
    [StructLayout(LayoutKind.Auto)]
    public readonly struct DataValue : INullable, IFormattable, IEquatable<DataValue>
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public DataValue()
        {
            m_value = Variant.Null;
            m_statusCode = StatusCodes.Good;
            m_sourceTimestamp = DateTimeUtc.MinValue;
            m_serverTimestamp = DateTimeUtc.MinValue;
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
            m_statusCode = StatusCodes.Good;
            m_sourceTimestamp = DateTimeUtc.MinValue;
            m_serverTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a status code.
        /// </summary>
        /// <param name="statusCode">The StatusCode to set</param>
        [Obsolete("Use DataValue.FromStatusCode() to avoid overload ambiguity with numeric types.")]
        public DataValue(StatusCode statusCode)
        {
            m_value = Variant.Null;
            m_statusCode = statusCode;
            m_sourceTimestamp = DateTimeUtc.MinValue;
            m_serverTimestamp = DateTimeUtc.MinValue;
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
            m_statusCode = statusCode;
            m_serverTimestamp = serverTimestamp;
            m_sourceTimestamp = DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Initializes the object with a value and a status code.
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <param name="statusCode">The status code to set</param>
        public DataValue(Variant value, StatusCode statusCode)
        {
            m_value = value;
            m_statusCode = statusCode;
            m_sourceTimestamp = DateTimeUtc.MinValue;
            m_serverTimestamp = DateTimeUtc.MinValue;
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
            m_statusCode = statusCode;
            m_sourceTimestamp = sourceTimestamp;
            m_serverTimestamp = DateTimeUtc.MinValue;
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
            m_statusCode = statusCode;
            m_sourceTimestamp = sourceTimestamp;
            m_serverTimestamp = serverTimestamp;
        }

        /// <summary>
        /// Initializes the object with all fields including picosecond resolution.
        /// </summary>
        /// <param name="value">The variant value to set</param>
        /// <param name="statusCode">The status code to set</param>
        /// <param name="sourceTimestamp">The source timestamp to set</param>
        /// <param name="serverTimestamp">The servers timestamp to set</param>
        /// <param name="sourcePicoseconds">Additional resolution for the source timestamp</param>
        /// <param name="serverPicoseconds">Additional resolution for the server timestamp</param>
        public DataValue(
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp,
            DateTimeUtc serverTimestamp,
            ushort sourcePicoseconds,
            ushort serverPicoseconds)
        {
            m_value = value;
            m_statusCode = statusCode;
            m_sourceTimestamp = sourceTimestamp;
            m_serverTimestamp = serverTimestamp;
            m_sourcePicoseconds = sourcePicoseconds;
            m_serverPicoseconds = serverPicoseconds;
        }

        /// <summary>
        /// Creates a DataValue with only a status code (no value).
        /// </summary>
        /// <param name="statusCode">The status code to set.</param>
        /// <returns>A new <see cref="DataValue"/> with the specified status code.</returns>
        public static DataValue FromStatusCode(StatusCode statusCode)
        {
            return new DataValue(Variant.Null, statusCode);
        }

        /// <summary>
        /// Creates a DataValue with a status code and a server timestamp (no value).
        /// </summary>
        /// <param name="statusCode">The status code to set.</param>
        /// <param name="serverTimestamp">The server timestamp to set.</param>
        /// <returns>A new <see cref="DataValue"/> with the specified status code and server timestamp.</returns>
        public static DataValue FromStatusCode(StatusCode statusCode, DateTimeUtc serverTimestamp)
        {
            return new DataValue(
                Variant.Null,
                statusCode,
                DateTimeUtc.MinValue,
                serverTimestamp);
        }

        // ----------------------------------------------------------------
        // With<Property>() fluent mutators.
        //
        // Each mutator returns a new DataValue with the named field
        // overwritten and every other field carried through unchanged.
        // Decoders, NodeState read paths, and any other code that
        // historically built a DataValue by mutating its properties
        // after construction migrate to chained With* calls:
        //
        //   DataValue v = new DataValue();
        //   v = v.WithWrappedValue(ReadVariant(null));
        //   v = v.WithStatus(ReadStatusCode(null));
        //   return v;
        //
        // Once DataValue is flipped to a `readonly struct` in a follow-up
        // step the JIT folds a default+With-chain into a single ctor
        // call.
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="WrappedValue"/> replaced.
        /// </summary>
        public DataValue WithWrappedValue(Variant value)
        {
            return new DataValue(
                value,
                m_statusCode,
                m_sourceTimestamp,
                m_serverTimestamp,
                m_sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="StatusCode"/> replaced.
        /// </summary>
        public DataValue WithStatus(StatusCode statusCode)
        {
            return new DataValue(
                m_value,
                statusCode,
                m_sourceTimestamp,
                m_serverTimestamp,
                m_sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="SourceTimestamp"/> replaced.
        /// </summary>
        public DataValue WithSourceTimestamp(DateTimeUtc sourceTimestamp)
        {
            return new DataValue(
                m_value,
                m_statusCode,
                sourceTimestamp,
                m_serverTimestamp,
                m_sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="SourcePicoseconds"/> replaced.
        /// </summary>
        public DataValue WithSourcePicoseconds(ushort sourcePicoseconds)
        {
            return new DataValue(
                m_value,
                m_statusCode,
                m_sourceTimestamp,
                m_serverTimestamp,
                sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="ServerTimestamp"/> replaced.
        /// </summary>
        public DataValue WithServerTimestamp(DateTimeUtc serverTimestamp)
        {
            return new DataValue(
                m_value,
                m_statusCode,
                m_sourceTimestamp,
                serverTimestamp,
                m_sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// Returns a copy of this <see cref="DataValue"/> with
        /// <see cref="ServerPicoseconds"/> replaced.
        /// </summary>
        public DataValue WithServerPicoseconds(ushort serverPicoseconds)
        {
            return new DataValue(
                m_value,
                m_statusCode,
                m_sourceTimestamp,
                m_serverTimestamp,
                m_sourcePicoseconds,
                serverPicoseconds);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is DataValue other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(DataValue other)
        {
            return StatusCode == other.StatusCode &&
                ServerTimestamp == other.ServerTimestamp &&
                SourceTimestamp == other.SourceTimestamp &&
                ServerPicoseconds == other.ServerPicoseconds &&
                SourcePicoseconds == other.SourcePicoseconds &&
                m_value == other.m_value;
        }

        /// <inheritdoc/>
        public static bool operator ==(DataValue a, DataValue b) => a.Equals(b);

        /// <inheritdoc/>
        public static bool operator !=(DataValue a, DataValue b) => !a.Equals(b);

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
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", m_value);
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Creates a deep copy of the DataValue, deep-copying the
        /// Variant payload so that reference-type bodies (e.g.
        /// ExtensionObject) are not shared with the source.
        /// </summary>
        /// <returns>A new <see cref="DataValue"/> that is a deep copy of this instance.</returns>
        public DataValue Copy()
        {
            return new DataValue(
                m_value.Copy(),
                m_statusCode,
                m_sourceTimestamp,
                m_serverTimestamp,
                m_sourcePicoseconds,
                m_serverPicoseconds);
        }

        /// <summary>
        /// True when the struct holds no payload (all-default fields).
        /// </summary>
        public bool IsNull
            => m_value.IsNull &&
               m_statusCode.Code == 0 &&
               m_sourceTimestamp == DateTimeUtc.MinValue &&
               m_serverTimestamp == DateTimeUtc.MinValue &&
               m_sourcePicoseconds == 0 &&
               m_serverPicoseconds == 0;

        /// <summary>
        /// The value of data value.
        /// </summary>
        [Obsolete("Use WrappedValue to access The value.")]
        public object? Value
        {
            get => m_value.AsBoxedObject(Variant.BoxingBehavior.Legacy);
        }

        /// <summary>
        /// The value of data value.
        /// </summary>
        public Variant WrappedValue => m_value;

        /// <summary>
        /// The status code associated with the value.
        /// </summary>
        public StatusCode StatusCode => m_statusCode;

        /// <summary>
        /// The source timestamp associated with the value.
        /// </summary>
        public DateTimeUtc SourceTimestamp => m_sourceTimestamp;

        /// <summary>
        /// Additional resolution for the source timestamp.
        /// </summary>
        public ushort SourcePicoseconds => m_sourcePicoseconds;

        /// <summary>
        /// The server timestamp associated with the value.
        /// </summary>
        public DateTimeUtc ServerTimestamp => m_serverTimestamp;

        /// <summary>
        /// Additional resolution for the server timestamp.
        /// </summary>
        public ushort ServerPicoseconds => m_serverPicoseconds;

        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsGood(DataValue value) => StatusCode.IsGood(value.StatusCode);

        /// <summary>
        /// Returns true if the status is bad or uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotGood(DataValue value) => StatusCode.IsNotGood(value.StatusCode);

        /// <summary>
        /// Returns true if the status code is uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsUncertain(DataValue value) => StatusCode.IsUncertain(value.StatusCode);

        /// <summary>
        /// Returns true if the status is good or bad.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotUncertain(DataValue value) => StatusCode.IsNotUncertain(value.StatusCode);

        /// <summary>
        /// Returns true if the status code is bad.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsBad(DataValue value) => StatusCode.IsBad(value.StatusCode);

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        /// <param name="value">The value to check the quality of</param>
        public static bool IsNotBad(DataValue value) => StatusCode.IsNotBad(value.StatusCode);

        /// <summary>
        /// Ensures the data value contains a value with the specified type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use WrappedValue and Variant API")]
        public object? GetValue(Type expectedType)
        {
            object? value = WrappedValue.AsBoxedObject();

            if (expectedType != null && value != null)
            {
                // return null for a DataValue with bad status code.
                if (StatusCode.IsBad(StatusCode))
                {
                    return null;
                }

                if (value is ExtensionObject extension &&
                    extension.TryGetValue(out IEncodeable? encodeable))
                {
                    value = encodeable!;
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
        public T? GetValueOrDefault<T>()
        {
            // return default for a DataValue with bad status code.
            if (StatusCode.IsBad(StatusCode))
            {
                return default;
            }

            if (!WrappedValue.IsNull)
            {
                if (WrappedValue.TryGetValue(out ExtensionObject extension) &&
                    extension.TryGetValue(out IEncodeable? encodeable) &&
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
                extension.TryGetValue(out IEncodeable? encodeable) &&
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

        private readonly Variant m_value;
        private readonly StatusCode m_statusCode;
        private readonly DateTimeUtc m_sourceTimestamp;
        private readonly DateTimeUtc m_serverTimestamp;
        private readonly ushort m_sourcePicoseconds;
        private readonly ushort m_serverPicoseconds;
    }
}
