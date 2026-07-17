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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for <see cref="LdsPeerDiscovery"/>, which discovers redundant
    /// peers from a Local Discovery Server (LDS / LDS-ME) via <c>FindServers</c> / <c>FindServersOnNetwork</c>
    /// (OPC 10000-4 §6.4).
    /// </summary>
    public sealed class LdsPeerDiscoveryOptions
    {
        /// <summary>
        /// Gets or sets this server's own ApplicationUri, which is excluded from the discovered peer set so a
        /// replica does not list itself.
        /// </summary>
        public string LocalApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether only server applications (<see cref="ApplicationType.Server"/>
        /// and <see cref="ApplicationType.ClientAndServer"/>) are kept. Defaults to <c>true</c>.
        /// </summary>
        public bool ServersOnly { get; set; } = true;
    }
}
