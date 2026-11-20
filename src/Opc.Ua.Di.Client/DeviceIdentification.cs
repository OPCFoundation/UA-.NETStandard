/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Holds the standard DI device identification properties
    /// defined in OPC 10000-100 §5.11 (IVendorNameplateType and
    /// ITagNameplateType).
    /// </summary>
    /// <param name="Manufacturer">Device manufacturer name.</param>
    /// <param name="Model">Device model name.</param>
    /// <param name="SerialNumber">Device serial number.</param>
    /// <param name="HardwareRevision">Hardware revision level.</param>
    /// <param name="SoftwareRevision">Software revision level.</param>
    /// <param name="DeviceRevision">Overall device revision level.</param>
    /// <param name="DeviceClass">Device class (e.g. "Sensor").</param>
    /// <param name="ProductInstanceUri">Globally unique product instance URI.</param>
    public sealed record DeviceIdentification(
        string? Manufacturer,
        string? Model,
        string? SerialNumber,
        string? HardwareRevision,
        string? SoftwareRevision,
        string? DeviceRevision,
        string? DeviceClass,
        string? ProductInstanceUri);
}
