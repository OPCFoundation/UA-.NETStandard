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

using System;

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// The state of a reader device read in one pass by <see cref="AutoIdClient.ReadDeviceAsync"/>.
    /// </summary>
    /// <remarks>
    /// Members of optional variables the device does not publish, or that
    /// <see cref="AutoIdReadOptions"/> excluded, keep their default value: an empty string, a null
    /// <see cref="Variant"/>, a null <see cref="DateTimeUtc"/> or <see langword="null"/>.
    /// The NodeIds of the variables that change at runtime and the methods the device publishes
    /// come from the browse that precedes the read, whatever the options, so a client can monitor
    /// the device and knows which optional methods (OPC 30010 6.1, 6.5.3, 6.6.3) it may call
    /// without browsing again.
    /// </remarks>
    public sealed record AutoIdDeviceSnapshot
    {
        /// <summary>Gets the NodeId of the device.</summary>
        public NodeId NodeId { get; init; }

        /// <summary>Gets the NodeId of <c>DeviceStatus</c> (OPC 30010 6.1.3.15).</summary>
        public NodeId DeviceStatusId { get; init; }

        /// <summary>
        /// Gets the NodeId of <c>LastScanData</c> (OPC 30010 6.1.3.9), or a null NodeId when the
        /// device does not publish it.
        /// </summary>
        public NodeId LastScanDataId { get; init; }

        /// <summary>
        /// Gets the NodeId of <c>LastScanTimestamp</c>, or a null NodeId when the device does not
        /// publish it.
        /// </summary>
        public NodeId LastScanTimestampId { get; init; }

        /// <summary>
        /// Gets the NodeId of <c>DeviceLocation</c> (OPC 30010 6.1.3.12), or a null NodeId when the
        /// device does not publish it.
        /// </summary>
        public NodeId DeviceLocationId { get; init; }

        /// <summary>
        /// Gets the browse names of the methods the device publishes, in the order of the browse:
        /// the optional methods of its type (<c>Scan</c>, <c>ScanStart</c>, <c>ReadTag</c>,
        /// <c>GetLocation</c>, ...) and any vendor-specific ones.
        /// </summary>
        public ArrayOf<QualifiedName> Methods { get; init; }

        /// <summary>Gets the <c>DeviceName</c> (OPC 30010 6.1.3.14).</summary>
        public string DeviceName { get; init; } = string.Empty;

        /// <summary>Gets the <c>DeviceStatus</c> (OPC 30010 6.1.3.15).</summary>
        public DeviceStatusEnumeration DeviceStatus { get; init; }

        /// <summary>Gets the <c>AutoIdModelVersion</c> (OPC 30010 6.1.3.16).</summary>
        public string ModelVersion { get; init; } = string.Empty;

        /// <summary>Gets the <c>DeviceInfo</c>, or empty.</summary>
        public string DeviceInfo { get; init; } = string.Empty;

        /// <summary>Gets the <c>DeviceLocationName</c>, or empty.</summary>
        public string DeviceLocationName { get; init; } = string.Empty;

        /// <summary>Gets the <c>DeviceLocation</c>, or <see langword="null"/>.</summary>
        public Location? DeviceLocation { get; init; }

        /// <summary>Gets the <c>AntennaNames</c> of an RFID reader, or an empty array.</summary>
        public ArrayOf<AntennaNameIdPair> AntennaNames { get; init; }

        /// <summary>Gets the <c>LastScanData</c> value, or a null variant.</summary>
        public Variant LastScanData { get; init; }

        /// <summary>Gets the status of <c>LastScanData</c>; uncertain until the first scan.</summary>
        public StatusCode LastScanStatus { get; init; }

        /// <summary>Gets the <c>LastScanTimestamp</c>, or a null time.</summary>
        public DateTimeUtc LastScanTimestamp { get; init; }

        /// <summary>Gets the manufacturer of the DI nameplate, or an empty text.</summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>Gets the model of the DI nameplate, or an empty text.</summary>
        public LocalizedText Model { get; init; }

        /// <summary>Gets the serial number of the DI nameplate, or empty.</summary>
        public string SerialNumber { get; init; } = string.Empty;

        /// <summary>
        /// Gets whether the device publishes a method with the browse name <paramref name="name"/>
        /// in any namespace, for example <c>BrowseNames.ScanStart</c>.
        /// </summary>
        /// <param name="name">The name part of the method's browse name.</param>
        /// <returns><see langword="true"/> when <see cref="Methods"/> contains such a method.</returns>
        public bool HasMethod(string name)
        {
            foreach (QualifiedName method in Methods)
            {
                if (string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
