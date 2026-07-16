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
