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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Well known OPC UA server capability identifiers as defined in
    /// <see href="https://reference.opcfoundation.org/" />.
    /// </summary>
    public static class WellKnownServerCapabilities
    {
        /// <summary>No information is available.</summary>
        public const string NoInformation = "NA";

        /// <summary>The server supports live data.</summary>
        public const string LiveData = "DA";

        /// <summary>The server supports alarms and conditions.</summary>
        public const string AlarmsAndConditions = "AC";

        /// <summary>The server supports historical data.</summary>
        public const string HistoricalData = "HD";

        /// <summary>The server supports historical events.</summary>
        public const string HistoricalEvents = "HE";

        /// <summary>The server is a global discovery server.</summary>
        public const string GlobalDiscoveryServer = "GDS";

        /// <summary>The server is a local discovery server.</summary>
        public const string LocalDiscoveryServer = "LDS";

        /// <summary>The server supports the device integration (DI) information model.</summary>
        public const string DI = "DI";
    }
}
