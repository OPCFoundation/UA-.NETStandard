/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Common identification data exposed by Robotics components and the
    /// underlying OPC UA DI model.
    /// </summary>
    public sealed record RoboticsComponentIdentification
    {
        /// <summary>
        /// The component instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The component BrowseName.
        /// </summary>
        public QualifiedName BrowseName { get; init; } = QualifiedName.Null;

        /// <summary>
        /// The user-facing component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The DI asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// The manufacturer name.
        /// </summary>
        public LocalizedText Manufacturer { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer model name.
        /// </summary>
        public LocalizedText Model { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The manufacturer product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// The manufacturer serial number.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// A URI or location for the component manual.
        /// </summary>
        public string? DeviceManual { get; init; }

        /// <summary>
        /// The DI HardwareRevision, when published.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// The DI SoftwareRevision, when published.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// The DI ManufacturerUri, when published.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// The DI ProductInstanceUri, when published.
        /// </summary>
        public string? ProductInstanceUri { get; init; }
    }
}
