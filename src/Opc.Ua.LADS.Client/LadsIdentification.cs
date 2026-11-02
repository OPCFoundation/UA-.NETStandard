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
