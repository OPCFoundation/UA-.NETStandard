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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The server facets and conformance units of OPC 30010 (Tables 82 to 85
    /// and Annex A.2) that this package can advertise.
    /// </summary>
    /// <remarks>
    /// A server advertises a facet only when a reader it hosts implements
    /// every mandatory conformance unit of that facet. The
    /// <c>AutoID Device Parameters</c> and <c>AutoID Simple Sync Access Control</c>
    /// units are not implemented, so the Full AutoID Server Facet is never
    /// advertised.
    /// </remarks>
    public static class AutoIdProfiles
    {
        /// <summary>
        /// The Simple Sync AutoID Server Facet (Table 82).
        /// </summary>
        public const string SimpleSyncServer =
            "http://opcfoundation.org/UA-Profile/External/AutoID/SimpleAutoIdSyncServer";

        /// <summary>
        /// The Base Sync AutoID Server Facet (Table 83).
        /// </summary>
        public const string BaseSyncServer =
            "http://opcfoundation.org/UA-Profile/External/AutoID/BaseAutoIdSyncServer";

        /// <summary>
        /// The Base Async AutoID Server Facet (Table 84).
        /// </summary>
        public const string BaseAsyncServer =
            "http://opcfoundation.org/UA-Profile/External/AutoID/BaseAutoIdAsyncServer";

        /// <summary>
        /// The Full AutoID Server Facet (Table 85).
        /// </summary>
        public const string FullServer =
            "http://opcfoundation.org/UA-Profile/External/AutoID/FullAutoIdServer";

        /// <summary>
        /// Conformance unit: the base device type and the mandatory components of the device specific type.
        /// </summary>
        public const string DeviceTypeUnit = "AutoID DeviceType";

        /// <summary>
        /// Conformance unit: the <c>LastScanData</c> variable for synchronous access to scan data.
        /// </summary>
        public const string SimpleSyncAccessUnit = "AutoID Simple Sync Access";

        /// <summary>
        /// Conformance unit: the <c>LastScanData</c> variable and the <c>Scan</c> method.
        /// </summary>
        public const string SyncAccessUnit = "AutoID Sync Access";

        /// <summary>
        /// Conformance unit: the scan event types that inform clients about new scan results.
        /// </summary>
        public const string AsyncAccessUnit = "AutoID Async Access";

        /// <summary>
        /// Conformance unit: the <c>ScanStart</c> and <c>ScanStop</c> methods.
        /// </summary>
        public const string AsyncAccessControlUnit = "AutoID Async Access Control";
    }
}
