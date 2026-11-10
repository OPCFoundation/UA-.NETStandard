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
 *
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Supplies one standalone ECM measurement sample.
    /// </summary>
    /// <param name="BrowseName">The standardized ECM measurement BrowseName.</param>
    /// <param name="Value">
    /// The declared scalar measurement value, or <see cref="Variant.Null"/> when quality is not Good.
    /// E0 requires the exact generated AcPeDataType; CLR subclasses are not supported.
    /// </param>
    /// <param name="DataType">The namespace-qualified OPC UA DataType of the value.</param>
    /// <param name="StatusCode">The measurement quality.</param>
    /// <param name="SourceTimestamp">The UTC time at which the value was acquired.</param>
    /// <param name="EngineeringUnits">
    /// The optional standardized engineering units. The binding supplies the published unit when omitted and
    /// rejects a supplied value with a different namespace URI or unit ID.
    /// </param>
    /// <param name="MeasurementId">
    /// The optional standardized measurement identifier. The binding supplies the published identifier when omitted
    /// and rejects a different supplied identifier.
    /// </param>
    /// <param name="AccuracyClass">The optional class within the accuracy domain.</param>
    /// <param name="AccuracyDomain">The optional accuracy-domain NodeId, or <see cref="NodeId.Null"/>.</param>
    /// <param name="AccuracyRange">The optional accuracy range.</param>
    public sealed record EcmMeasurementReading(
        string BrowseName,
        Variant Value,
        ExpandedNodeId DataType,
        StatusCode StatusCode,
        DateTime SourceTimestamp,
        EUInformation? EngineeringUnits = null,
        ushort? MeasurementId = null,
        uint? AccuracyClass = null,
        NodeId AccuracyDomain = default,
        float? AccuracyRange = null);

    /// <summary>
    /// A snapshot of the current application tag and standalone ECM measurements.
    /// </summary>
    /// <param name="ApplicationTag">The current application tag.</param>
    /// <param name="Measurements">The measurements represented by this snapshot.</param>
    public sealed record EcmMeasurementSnapshot(
        string ApplicationTag,
        ArrayOf<EcmMeasurementReading> Measurements)
    {
        /// <summary>
        /// Gets the beginning of the statistics collection for E2, E3 and D1 measurement points.
        /// It is required when the snapshot contains statistic members and ignored otherwise.
        /// </summary>
        public DateTimeUtc StatisticsStartTime { get; init; }
    }

    /// <summary>
    /// A committed statistics observation returned by a statistics reset.
    /// </summary>
    /// <param name="StartTime">The new beginning of the statistics collection.</param>
    /// <param name="Measurements">
    /// Exactly the statistic members of the published profiles, with unchanged metadata.
    /// </param>
    public sealed record EcmStatisticsSnapshot(
        DateTimeUtc StartTime,
        ArrayOf<EcmMeasurementReading> Measurements);

    /// <summary>
    /// Provides standalone ECM profile measurements and retains application-tag writes as its source of truth.
    /// </summary>
    public interface IEcmMeasurementProvider
    {
        /// <summary>
        /// Reads the current measurements and persisted application tag.
        /// </summary>
        /// <param name="cancellationToken">Cancels the acquisition.</param>
        ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stores an application-tag update so subsequent reads return the new value.
        /// </summary>
        /// <param name="applicationTag">The new tag.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Extends a measurement provider with the shared IA statistics collection used by E2, E3 and D1.
    /// </summary>
    /// <remarks>
    /// The statistic members and the collection StartTime are published through the shared
    /// <c>Opc.Ua.IA.Server</c> statistics runtime. Every statistic member of the selected profiles shares
    /// one grouped reset. The binding serializes reset with refresh and application-tag writes.
    /// </remarks>
    public interface IEcmStatisticsProvider : IEcmMeasurementProvider
    {
        /// <summary>
        /// Checks the requesting user's permission to reset every statistic of the measurement point.
        /// </summary>
        /// <param name="requestContext">The calling user's context.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
            ISystemContext requestContext,
            CancellationToken cancellationToken);

        /// <summary>
        /// Resets every statistic and returns the committed collection, including its new StartTime.
        /// </summary>
        /// <param name="cancellationToken">Cancels the reset before it commits.</param>
        ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken);
    }
}
