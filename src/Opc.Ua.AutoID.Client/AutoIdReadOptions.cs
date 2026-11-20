/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// Controls how much <see cref="AutoIdClient.ReadDeviceAsync"/> fetches.
    /// </summary>
    /// <remarks>
    /// The defaults read the mandatory variables and everything optional the device publishes: the
    /// nameplate, the optional device properties and the last scan. <see cref="StatusOnly"/>
    /// limits the value read to <c>DeviceStatus</c> and <c>DeviceName</c>; each call still
    /// browses the reader to discover its published variables and methods. To poll a device
    /// without browsing, pass the snapshot to <see cref="AutoIdClient.RefreshDeviceAsync"/>.
    /// </remarks>
    public sealed record AutoIdReadOptions
    {
        /// <summary>The defaults: everything the device publishes.</summary>
        public static readonly AutoIdReadOptions Default = new();

        /// <summary>Only <c>DeviceStatus</c> and <c>DeviceName</c>.</summary>
        public static readonly AutoIdReadOptions StatusOnly = new()
        {
            IncludeNameplate = false,
            IncludeOptionalProperties = false,
            IncludeLastScan = false
        };

        /// <summary>
        /// Gets whether to read the DI nameplate (manufacturer, model, serial number and the
        /// revisions). Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeNameplate { get; init; } = true;

        /// <summary>
        /// Gets whether to read the optional device properties (<c>DeviceInfo</c>,
        /// <c>DeviceLocation</c>, <c>DeviceLocationName</c> and, for RFID readers,
        /// <c>AntennaNames</c>). Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeOptionalProperties { get; init; } = true;

        /// <summary>
        /// Gets whether to read <c>LastScanData</c> and <c>LastScanTimestamp</c>. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        public bool IncludeLastScan { get; init; } = true;
    }
}
