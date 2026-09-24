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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Overrides one stage within its existing server-wide capacity.
    /// </summary>
    public sealed class ResourceIsolationStageOptions
    {
        /// <summary>
        /// The stage to configure. Duplicate stages are rejected.
        /// </summary>
        public ResourceIsolationStage Stage { get; set; }

        /// <summary>
        /// Optional additional aggregate ceiling, never greater than the existing total.
        /// RequestQueueBytes defaults to maximum encoded-message cost times the combined
        /// queued, executing and parked slot bounds; it is not coupled to retained transport bytes.
        /// </summary>
        public long? Capacity { get; set; }

        /// <summary>
        /// Non-borrowable bootstrap floor; null sizes one operation or maximum retained message.
        /// Zero explicitly disables the stage's bootstrap guarantee.
        /// </summary>
        public long? BootstrapReserved { get; set; }

        /// <summary>
        /// Non-borrowable reconnect floor; null sizes one operation or maximum retained message.
        /// Zero explicitly disables the stage's reconnect guarantee.
        /// </summary>
        public long? ReconnectReserved { get; set; }

        /// <summary>
        /// Non-borrowable verified-control floor. Null sizes one operation at decoded-request
        /// stages and zero elsewhere. Zero explicitly disables this stage's control guarantee.
        /// </summary>
        public long? ControlReserved { get; set; }

        /// <summary>
        /// Optional hard ceiling per owner. Null permits the entire pool, while protected floors
        /// remain non-borrowable. Set an explicit ceiling to constrain one owner's shared borrowing.
        /// </summary>
        public long? OwnerHardLimit { get; set; }
    }

    /// <summary>
    /// Explicit trusted-owner capacity for one resource stage.
    /// </summary>
    public readonly record struct TrustedResourceReservation(
        ResourceIsolationStage Stage,
        long Reserved,
        long? HardLimit = null);

    /// <summary>
    /// Provisioned trusted owner. Classification must independently verify this key.
    /// </summary>
    public sealed class TrustedResourceOwnerOptions
    {
        /// <summary>
        /// The operator-provisioned key returned by the classifier.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Relative decoded-request scheduling weight.
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// Stage-specific non-borrowable floors. Omitted stages reserve one operation/message.
        /// </summary>
        public ArrayOf<TrustedResourceReservation> Reservations { get; set; }
    }

    /// <summary>
    /// Immutable limits of one resource pool.
    /// </summary>
    public sealed class ResourceIsolationStagePlan
    {
        internal ResourceIsolationStagePlan(
            ResourceIsolationStage stage,
            long capacity,
            long bootstrapReserved,
            long reconnectReserved,
            long trustedReserved,
            long ownerHardLimit,
            long controlReserved = 0)
        {
            Stage = stage;
            Capacity = capacity;
            BootstrapReserved = bootstrapReserved;
            ReconnectReserved = reconnectReserved;
            TrustedReserved = trustedReserved;
            OwnerHardLimit = ownerHardLimit;
            ControlReserved = controlReserved;
        }

        /// <summary>
        /// The accounted resource.
        /// </summary>
        public ResourceIsolationStage Stage { get; }

        /// <summary>
        /// Aggregate hard ceiling, including all floors.
        /// </summary>
        public long Capacity { get; }

        /// <summary>
        /// Non-borrowable bootstrap capacity.
        /// </summary>
        public long BootstrapReserved { get; }

        /// <summary>
        /// Non-borrowable reconnect capacity.
        /// </summary>
        public long ReconnectReserved { get; }

        /// <summary>
        /// Sum of individually provisioned trusted-owner floors.
        /// </summary>
        public long TrustedReserved { get; }

        /// <summary>
        /// Non-borrowable capacity for verified recovery/control requests.
        /// </summary>
        public long ControlReserved { get; }

        /// <summary>
        /// Work-conserving capacity available to every class.
        /// </summary>
        public long SharedCapacity =>
            Capacity - BootstrapReserved - ReconnectReserved - TrustedReserved - ControlReserved;

        /// <summary>
        /// Ordinary owner's hard ceiling.
        /// </summary>
        public long OwnerHardLimit { get; }
    }
}
