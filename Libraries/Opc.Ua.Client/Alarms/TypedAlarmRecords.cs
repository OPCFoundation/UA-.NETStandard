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

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Record for <c>LimitAlarmType</c> events. Adds limit values
    /// (HighHigh, High, Low, LowLow) and adaptive base limits.
    /// </summary>
    public record LimitAlarmRecord : AlarmRecord
    {
        /// <summary>The HighHigh limit.</summary>
        public double? HighHighLimit { get; init; }
        /// <summary>The High limit.</summary>
        public double? HighLimit { get; init; }
        /// <summary>The Low limit.</summary>
        public double? LowLimit { get; init; }
        /// <summary>The LowLow limit.</summary>
        public double? LowLowLimit { get; init; }
        /// <summary>Base (non-adaptive) HighHigh limit.</summary>
        public double? BaseHighHighLimit { get; init; }
        /// <summary>Base (non-adaptive) High limit.</summary>
        public double? BaseHighLimit { get; init; }
        /// <summary>Base (non-adaptive) Low limit.</summary>
        public double? BaseLowLimit { get; init; }
        /// <summary>Base (non-adaptive) LowLow limit.</summary>
        public double? BaseLowLowLimit { get; init; }
    }

    /// <summary>
    /// Record for <c>ExclusiveLimitAlarmType</c> events. The
    /// <see cref="CurrentLimitState"/> identifies which single
    /// sub-state (HighHigh, High, Low, LowLow) is active.
    /// </summary>
    public record ExclusiveLimitAlarmRecord : LimitAlarmRecord
    {
        /// <summary>
        /// The current limit state (one of HighHigh, High, Low, LowLow).
        /// </summary>
        public LocalizedText CurrentLimitState { get; init; }

        /// <summary>The NodeId of the current limit state.</summary>
        public NodeId? CurrentLimitStateId { get; init; }
    }

    /// <summary>
    /// Record for <c>NonExclusiveLimitAlarmType</c> events. Multiple
    /// limit sub-states can be active simultaneously.
    /// </summary>
    public record NonExclusiveLimitAlarmRecord : LimitAlarmRecord
    {
        /// <summary>Whether the HighHigh state is active.</summary>
        public bool? HighHighStateId { get; init; }
        /// <summary>Whether the High state is active.</summary>
        public bool? HighStateId { get; init; }
        /// <summary>Whether the Low state is active.</summary>
        public bool? LowStateId { get; init; }
        /// <summary>Whether the LowLow state is active.</summary>
        public bool? LowLowStateId { get; init; }
    }

    /// <summary>
    /// Record for <c>DiscreteAlarmType</c> events. Adds no new fields
    /// on top of <see cref="AlarmRecord"/> — provided to allow callers
    /// to distinguish discrete from limit alarms at the type level.
    /// </summary>
    public record DiscreteAlarmRecord : AlarmRecord;

    /// <summary>
    /// Record for <c>OffNormalAlarmType</c> events. Adds the
    /// <see cref="NormalState"/> NodeId reference.
    /// </summary>
    public record OffNormalAlarmRecord : DiscreteAlarmRecord
    {
        /// <summary>The NodeId of the value that represents "normal".</summary>
        public NodeId? NormalState { get; init; }
    }

    /// <summary>
    /// Record for <c>CertificateExpirationAlarmType</c> events. Adds
    /// expiration metadata.
    /// </summary>
    public record CertificateExpirationAlarmRecord : OffNormalAlarmRecord
    {
        /// <summary>The certificate expiration date.</summary>
        public DateTime? ExpirationDate { get; init; }
        /// <summary>Time before expiration when the alarm fires.</summary>
        public TimeSpan? ExpirationLimit { get; init; }
        /// <summary>The certificate type NodeId.</summary>
        public NodeId? CertificateType { get; init; }
        /// <summary>The certificate raw bytes.</summary>
        public ByteString Certificate { get; init; }
    }

    /// <summary>
    /// Record for <c>DiscrepancyAlarmType</c> events. Indicates the
    /// monitored value does not match the expected target.
    /// </summary>
    public record DiscrepancyAlarmRecord : AlarmRecord
    {
        /// <summary>The NodeId of the expected target value.</summary>
        public NodeId? TargetValueNode { get; init; }
        /// <summary>Time after which the discrepancy triggers an alarm.</summary>
        public double? ExpectedTime { get; init; }
        /// <summary>Allowed discrepancy tolerance.</summary>
        public double? Tolerance { get; init; }
    }
}