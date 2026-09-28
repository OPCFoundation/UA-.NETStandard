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

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// One value read from an OPC 40223 pump, together with the metadata the
    /// model publishes alongside it.
    /// </summary>
    /// <remarks>
    /// Nearly every variable OPC 40223 declares is optional, and most of the
    /// analog ones are <c>AnalogUnitType</c>/<c>AnalogItemType</c> carrying an
    /// engineering unit and an instrument range. A reader that only takes the
    /// number loses the unit, and a pump reading without its unit is not
    /// interpretable - 350 kPa and 350 Pa are different machines. Both travel
    /// with the value here, and are <see langword="null"/> when the server
    /// does not publish them.
    /// </remarks>
    public sealed record PumpValue
    {
        /// <summary>
        /// The variable the value was read from.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The variable's browse name, without namespace index.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The value as read.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// The read status.
        /// </summary>
        public StatusCode StatusCode { get; init; }

        /// <summary>
        /// The server timestamp of the read, when requested.
        /// </summary>
        public DateTime? SourceTimestamp { get; init; }

        /// <summary>
        /// The engineering unit, for a variable typed by <c>AnalogUnitType</c>
        /// or <c>AnalogUnitRangeType</c>.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The instrument range, for a variable typed by <c>AnalogItemType</c>
        /// or one of its subtypes.
        /// </summary>
        public Range? EuRange { get; init; }

        /// <summary>
        /// Gets the value as a <see cref="double"/>, or <see langword="null"/>
        /// when it is absent, bad, or not a number.
        /// </summary>
        public double? AsDouble()
        {
            if (StatusCode.IsBad(StatusCode))
            {
                return null;
            }
            if (Value.TryGetValue(out double number))
            {
                return number;
            }
            if (Value.TryGetValue(out float single))
            {
                return single;
            }
            return Value.TryGetValue(out int integer) ? integer : null;
        }

        /// <summary>
        /// Gets the value as a <see cref="bool"/>, or <see langword="null"/>
        /// when it is absent, bad, or not a boolean.
        /// </summary>
        public bool? AsBoolean()
        {
            return !StatusCode.IsBad(StatusCode) && Value.TryGetValue(out bool flag)
                ? flag
                : null;
        }

        /// <summary>
        /// Gets the value as a <see cref="string"/>, or <see langword="null"/>
        /// when it is absent, bad, or not textual.
        /// </summary>
        public string? AsString()
        {
            if (StatusCode.IsBad(StatusCode))
            {
                return null;
            }
            if (Value.TryGetValue(out string text))
            {
                return text;
            }
            return Value.TryGetValue(out LocalizedText localized) ? localized.Text : null;
        }

        /// <summary>
        /// Gets the value as a string array, or <see langword="null"/> when it
        /// is absent, bad, or not an array of strings - the shape of the
        /// OPC 40223 identifier lists such as <c>RedundantPumpIDs</c>.
        /// </summary>
        public ArrayOf<string>? AsStringArray()
        {
            if (StatusCode.IsBad(StatusCode))
            {
                return null;
            }
            return Value.TryGetValue(out ArrayOf<string> texts) ? texts : (ArrayOf<string>?)null;
        }

        /// <summary>
        /// Gets the value as a <see cref="uint"/>, or <see langword="null"/>
        /// when it is absent, bad, or not an unsigned integer.
        /// </summary>
        public uint? AsUInt32()
        {
            if (StatusCode.IsBad(StatusCode))
            {
                return null;
            }
            if (Value.TryGetValue(out uint number))
            {
                return number;
            }
            return Value.TryGetValue(out int signed) && signed >= 0 ? (uint)signed : null;
        }

        /// <summary>
        /// Gets the value as the enumeration <typeparamref name="TEnum"/>, or
        /// <see langword="null"/> when it is absent, bad, or not an
        /// enumeration.
        /// </summary>
        /// <typeparam name="TEnum">The generated OPC 40223 enumeration.</typeparam>
        public TEnum? AsEnum<TEnum>() where TEnum : struct, Enum
        {
            return !StatusCode.IsBad(StatusCode) && Value.TryGetValue(out TEnum value)
                ? value
                : null;
        }

        /// <summary>
        /// Gets the value as a UTC <see cref="DateTime"/>, or
        /// <see langword="null"/> when it is absent, bad, or not a date.
        /// </summary>
        public DateTime? AsDateTime()
        {
            return !StatusCode.IsBad(StatusCode) && Value.TryGetValue(out DateTimeUtc value)
                ? value.ToDateTime()
                : null;
        }
    }
}
