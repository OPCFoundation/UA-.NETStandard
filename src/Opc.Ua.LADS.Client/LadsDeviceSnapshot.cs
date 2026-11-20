/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A snapshot of a LADS device.
    /// </summary>
    /// <param name="DeviceId">The NodeId of the device.</param>
    /// <param name="DisplayName">The display name of the device.</param>
    /// <param name="Identification">The DI identification.</param>
    public sealed record LadsDeviceSnapshot(NodeId DeviceId, string DisplayName, LadsIdentification Identification)
    {
        /// <summary>
        /// Gets the state of the mandatory <c>DeviceState</c> state machine.
        /// </summary>
        public LadsStateSnapshot? DeviceState { get; init; }

        /// <summary>
        /// Gets the state of the Machinery <c>MachineryItemState</c>, when present.
        /// </summary>
        public LadsStateSnapshot? MachineryItemState { get; init; }

        /// <summary>
        /// Gets the state of the Machinery <c>MachineryOperationMode</c>, when present.
        /// </summary>
        public LadsStateSnapshot? OperationMode { get; init; }

        /// <summary>
        /// Gets the DI <c>DeviceHealth</c>, when present.
        /// </summary>
        public Variant DeviceHealth { get; init; }

        /// <summary>
        /// Gets the display names of the functional units of the device.
        /// </summary>
        public ArrayOf<string> FunctionalUnitNames { get; init; } = [];
    }
}
