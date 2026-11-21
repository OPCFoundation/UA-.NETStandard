// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The DI identification of a LADS device, read from its
    /// <c>1:Identification</c> object with the device itself as fallback.
    /// </summary>
    public sealed record LadsIdentification
    {
        /// <summary>
        /// Gets the manufacturer.
        /// </summary>
        public string? Manufacturer { get; init; }

        /// <summary>
        /// Gets the model.
        /// </summary>
        public string? Model { get; init; }

        /// <summary>
        /// Gets the serial number.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Gets the software revision.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// Gets the hardware revision.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// Gets the device revision.
        /// </summary>
        public string? DeviceRevision { get; init; }

        /// <summary>
        /// Gets the asset id.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// Gets the component name.
        /// </summary>
        public string? ComponentName { get; init; }

        /// <summary>
        /// Gets the product instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>
        /// Gets the manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Gets the product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// Gets the device manual address.
        /// </summary>
        public string? DeviceManual { get; init; }

        /// <summary>
        /// Gets the AMB operational location.
        /// </summary>
        public string? OperationalLocation { get; init; }

        /// <summary>
        /// Gets the AMB hierarchical location.
        /// </summary>
        public string? HierarchicalLocation { get; init; }
    }
}
