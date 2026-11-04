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
    /// <param name="Value">The encoded measurement value.</param>
    /// <param name="DataType">The namespace-qualified OPC UA DataType of the value.</param>
    /// <param name="StatusCode">The measurement quality.</param>
    /// <param name="SourceTimestamp">The UTC time at which the value was acquired.</param>
    /// <param name="EngineeringUnits">The optional engineering units.</param>
    /// <param name="MeasurementId">The optional standardized measurement identifier.</param>
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
}
