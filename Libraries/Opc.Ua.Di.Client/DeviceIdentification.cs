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
