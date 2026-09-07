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

namespace Opc.Ua
{
    /// <summary>
    /// One reading from an <see cref="IGeoLocationProvider"/>: where something
    /// is, how good the reading is, and when it was taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both <see cref="Position"/> and <see cref="Labels"/> are optional, and a
    /// provider may supply either or both. A satellite receiver reports a
    /// <see cref="Position"/>; an asset register that only knows
    /// <c>"Building 4, Detroit Plant"</c> reports a label and no coordinates.
    /// Consumers that require coordinates - OPC 10000-211 (GPOS) - reject a
    /// sample without a <see cref="Position"/>, while OPC 10030 (ISA-95)
    /// accepts either because its variable carries text.
    /// </para>
    /// <para>
    /// A <see cref="SourceTimestamp"/> of <see cref="DateTimeUtc.MinValue"/>
    /// asks the consumer to substitute the current UTC time when it applies the
    /// sample, which lets a provider that does not track acquisition time stay
    /// simple.
    /// </para>
    /// </remarks>
    /// <param name="Position">
    /// The geodetic position, or <c>null</c> when the provider only knows a
    /// textual location.
    /// </param>
    /// <param name="Orientation">
    /// The orientation of the located body, or <c>null</c> when unknown.
    /// Consumers that do not model orientation ignore it.
    /// </param>
    /// <param name="Labels">
    /// Human-readable location literals - a site name, a postal address, a
    /// pre-formatted geometry - published in addition to
    /// <paramref name="Position"/>. May be empty.
    /// </param>
    /// <param name="StatusCode">
    /// The OPC UA quality of the reading.
    /// </param>
    /// <param name="SourceTimestamp">
    /// When the reading was taken, or <see cref="DateTimeUtc.MinValue"/> to let
    /// the consumer substitute the current UTC time.
    /// </param>
    /// <param name="SourceId">
    /// The source this reading belongs to, when the provider chooses to echo
    /// it. A consumer that receives a non-<c>null</c> value not matching the
    /// source it asked for rejects the sample, which catches a provider
    /// mixing up its sources. <c>null</c> disables that check.
    /// </param>
    public readonly record struct GeoLocationSample(
        GeoPosition? Position,
        GeoOrientation? Orientation,
        ArrayOf<string> Labels,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp,
        string? SourceId = null)
    {
        /// <summary>
        /// Creates a good-quality sample carrying a position.
        /// </summary>
        /// <param name="position">The geodetic position.</param>
        /// <param name="sourceTimestamp">
        /// When the reading was taken; omit to let the consumer substitute the
        /// current UTC time.
        /// </param>
        /// <returns>
        /// A good-quality sample.
        /// </returns>
        public static GeoLocationSample Good(
            GeoPosition position,
            DateTimeUtc sourceTimestamp = default)
        {
            return new GeoLocationSample(
                position,
                null,
                default,
                StatusCodes.Good,
                sourceTimestamp);
        }

        /// <summary>
        /// Creates a good-quality sample carrying a position and an
        /// orientation.
        /// </summary>
        /// <param name="position">The geodetic position.</param>
        /// <param name="orientation">The orientation of the located body.</param>
        /// <param name="sourceTimestamp">
        /// When the reading was taken; omit to let the consumer substitute the
        /// current UTC time.
        /// </param>
        /// <returns>
        /// A good-quality sample.
        /// </returns>
        public static GeoLocationSample Good(
            GeoPosition position,
            GeoOrientation orientation,
            DateTimeUtc sourceTimestamp = default)
        {
            return new GeoLocationSample(
                position,
                orientation,
                default,
                StatusCodes.Good,
                sourceTimestamp);
        }

        /// <summary>
        /// Creates a good-quality sample carrying only textual location
        /// literals, for a source that has no coordinates.
        /// </summary>
        /// <param name="labels">The location literals.</param>
        /// <param name="sourceTimestamp">
        /// When the reading was taken; omit to let the consumer substitute the
        /// current UTC time.
        /// </param>
        /// <returns>
        /// A good-quality sample.
        /// </returns>
        public static GeoLocationSample Good(
            ArrayOf<string> labels,
            DateTimeUtc sourceTimestamp = default)
        {
            return new GeoLocationSample(
                null,
                null,
                labels,
                StatusCodes.Good,
                sourceTimestamp);
        }

        /// <summary>
        /// Creates a sample that carries no location, only the reason one is
        /// unavailable.
        /// </summary>
        /// <param name="statusCode">The status explaining the failure.</param>
        /// <param name="sourceTimestamp">
        /// When the failure was observed; omit to let the consumer substitute
        /// the current UTC time.
        /// </param>
        /// <returns>
        /// A sample carrying <paramref name="statusCode"/>.
        /// </returns>
        public static GeoLocationSample Unavailable(
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp = default)
        {
            return new GeoLocationSample(
                null,
                null,
                default,
                statusCode,
                sourceTimestamp);
        }

        /// <summary>
        /// Returns <see cref="SourceTimestamp"/>, substituting the current UTC
        /// time when the provider left it unset.
        /// </summary>
        /// <returns>
        /// The effective source timestamp.
        /// </returns>
        public DateTimeUtc GetEffectiveSourceTimestamp()
        {
            return SourceTimestamp == DateTimeUtc.MinValue
                ? DateTimeUtc.Now
                : SourceTimestamp;
        }
    }
}
