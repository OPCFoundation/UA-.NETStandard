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
    /// Which side of a connection a Historical Access profile (facet)
    /// applies to.
    /// </summary>
    public enum HistoricalAccessProfileSide
    {
        /// <summary>
        /// The profile is implemented by a Server and advertised on
        /// <c>Server/ServerCapabilities/ServerProfileArray</c>.
        /// </summary>
        Server,

        /// <summary>
        /// The profile is implemented by a Client.
        /// </summary>
        Client
    }

    /// <summary>
    /// The functional family a Historical Access profile (facet) belongs
    /// to, as defined by OPC UA Part 11.
    /// </summary>
    public enum HistoricalAccessProfileFamily
    {
        /// <summary>
        /// Reading raw historical values, including the ability to
        /// request Server (rather than Source) timestamps.
        /// </summary>
        RawAndServerTimestamp,

        /// <summary>
        /// Reading values that were previously modified or inserted
        /// (the "modified" history view).
        /// </summary>
        Modified,

        /// <summary>
        /// Reading historical values at specific instances in time.
        /// </summary>
        AtTime,

        /// <summary>
        /// Reading aggregated (processed) historical values.
        /// </summary>
        Aggregate,

        /// <summary>
        /// Reading and writing annotations attached to historical
        /// values.
        /// </summary>
        Annotation,

        /// <summary>
        /// Reading and updating historical values with a structured
        /// (complex) data type.
        /// </summary>
        Structured,

        /// <summary>
        /// Inserting, replacing, updating, or deleting raw historical
        /// data (<c>HistoryUpdate</c> on values).
        /// </summary>
        RawUpdates,

        /// <summary>
        /// Reading, inserting, replacing, updating, or deleting
        /// historical events.
        /// </summary>
        Events
    }

    /// <summary>
    /// Describes a single released OPC UA Part 11 Historical Access
    /// profile (facet) from the UACore 1.05 profile group.
    /// </summary>
    /// <param name="Name">
    /// The profile's display name, e.g. <code>Historical Raw Data 2022
    /// Server Facet</code>.
    /// </param>
    /// <param name="ProfileUri">
    /// The profile's unique URI, suitable for
    /// <c>Server/ServerCapabilities/ServerProfileArray</c> or a Client's
    /// self-description.
    /// </param>
    /// <param name="Side">
    /// Whether the profile applies to a Server or a Client.
    /// </param>
    /// <param name="Family">
    /// The functional family the profile belongs to.
    /// </param>
    /// <param name="MandatoryConformanceUnits">
    /// The names of every conformance unit the profile requires
    /// unconditionally. Optional conformance units (e.g. individual
    /// aggregate functions) are not included here.
    /// </param>
    /// <param name="IsAdvertised">
    /// <see langword="true"/> once this implementation's coverage of
    /// every mandatory conformance unit above has been verified and the
    /// profile may be safely advertised. The baseline inventory ships
    /// with this set to <see langword="false"/> for every profile so
    /// that nothing is advertised before it is verified.
    /// </param>
    /// <param name="Prerequisite">
    /// Explains what remains before a Server profile can be advertised, or
    /// why a verified Client profile is intentionally not published through
    /// a Server's <c>ServerProfileArray</c>. Empty for advertised profiles.
    /// </param>
    public sealed record HistoricalAccessProfileDescriptor(
        string Name,
        string ProfileUri,
        HistoricalAccessProfileSide Side,
        HistoricalAccessProfileFamily Family,
        ArrayOf<string> MandatoryConformanceUnits,
        bool IsAdvertised,
        string Prerequisite);
}
