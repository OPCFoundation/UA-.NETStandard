/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Mutable property bag used by
    /// <see cref="IDeviceBuilder{TDevice}.WithIdentification(System.Action{DeviceIdentificationData})"/>
    /// to populate the standard DI nameplate properties defined in
    /// OPC 10000-100 §5.10–§5.11 (<c>IVendorNameplateType</c> and
    /// <c>ITagNameplateType</c>).
    /// </summary>
    /// <remarks>
    /// All members are <see langword="null"/> by default; only properties
    /// that are assigned by the configuration delegate are written back
    /// onto the underlying <see cref="ComponentState"/>. This keeps the
    /// builder additive and lets callers populate a partial nameplate
    /// without overwriting existing values.
    /// </remarks>
    public sealed class DeviceIdentificationData
    {
        /// <summary>
        /// Manufacturer of the device. The <c>default</c> value (an
        /// empty <see cref="LocalizedText"/> whose <c>IsNull</c> is
        /// <see langword="true"/>) means "not assigned" and is skipped
        /// during configuration.
        /// </summary>
        public LocalizedText Manufacturer { get; set; }

        /// <summary>
        /// Globally unique manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; set; }

        /// <summary>
        /// Model designation of the device. The <c>default</c> value
        /// means "not assigned" (see <see cref="Manufacturer"/>).
        /// </summary>
        public LocalizedText Model { get; set; }

        /// <summary>
        /// Hardware revision level.
        /// </summary>
        public string? HardwareRevision { get; set; }

        /// <summary>
        /// Software revision level.
        /// </summary>
        public string? SoftwareRevision { get; set; }

        /// <summary>
        /// Overall device revision level.
        /// </summary>
        public string? DeviceRevision { get; set; }

        /// <summary>
        /// Manufacturer-defined product code.
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// URI of the device manual.
        /// </summary>
        public string? DeviceManual { get; set; }

        /// <summary>Device class — for example, <c>"Pump"</c> or <c>"Sensor"</c>.</summary>
        public string? DeviceClass { get; set; }

        /// <summary>
        /// Manufacturer-assigned serial number.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Globally unique product-instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; set; }

        /// <summary>
        /// Revision counter incremented by the device firmware.
        /// </summary>
        public int? RevisionCounter { get; set; }
    }
}
