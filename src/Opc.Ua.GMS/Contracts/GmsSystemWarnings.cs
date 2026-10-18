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

using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Contracts
{
    public sealed partial record GmsSystemSnapshot
    {
        /// <summary>
        /// Gets all configured persistent additional-sensor warning identities, including inactive conditions.
        /// </summary>
        public ArrayOf<GmsSensorWarning> SensorWarnings { get; init; }
    }

    /// <summary>
    /// A provider-reported SensorWarningAlarmType condition for an additional sensor outside its allowed range.
    /// Sensors directly involved in geometrical measurement are excluded by OPC 40210 section 9.1.
    /// Standard acknowledgement is not a machine reset.
    /// </summary>
    public sealed record GmsSensorWarning
    {
        /// <summary>
        /// Gets the stable, per-instance condition key.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the key in the configured AdditionalSensors inventory that is the event source.
        /// Primary measurement probes in Sensors cannot be sources of this condition type.
        /// </summary>
        public string SensorKey { get; init; } = string.Empty;

        /// <summary>
        /// Gets the actual additional-sensor measured-variable NodeId.
        /// Null selects that same variable automatically. Sensor metadata, primary qualification countdowns
        /// and foreign inputs are never accepted.
        /// </summary>
        public NodeId InputNodeId { get; init; }

        /// <summary>
        /// Gets the actual manufacturer's error or warning code.
        /// </summary>
        public string ErrorCode { get; init; } = string.Empty;

        /// <summary>
        /// Gets the operator-facing warning message.
        /// </summary>
        public LocalizedText Message { get; init; }

        /// <summary>
        /// Gets whether the equipment currently reports the warning.
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Gets the reported warning severity, from one through 1000.
        /// </summary>
        public ushort Severity { get; init; } = 500;

        /// <summary>
        /// Gets the optional provider's native lower limit.
        /// </summary>
        public DataValue LowLimit { get; init; }

        /// <summary>
        /// Gets the optional provider's native upper limit.
        /// </summary>
        public DataValue HighLimit { get; init; }

        /// <summary>
        /// Gets the provider's allowed standard condition operations, in addition to server authorization.
        /// </summary>
        public MachineToolConditionOperations AllowedOperations { get; init; } = MachineToolConditionOperations.All;

        /// <summary>
        /// Gets acquisition time and quality. The runtime does not fabricate physical sensor warnings.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }
}
