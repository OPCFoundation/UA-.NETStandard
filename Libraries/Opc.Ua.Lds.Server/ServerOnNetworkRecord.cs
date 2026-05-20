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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// Network-level record exposed to clients via
    /// <see cref="LdsServer.FindServersOnNetworkAsync"/>. Each
    /// <see cref="MdnsDiscoveryConfiguration"/> attached to a register call
    /// produces one record; observed mDNS peers also surface as records.
    /// </summary>
    public sealed class ServerOnNetworkRecord
    {
        /// <summary>
        /// The monotonic record id assigned by the store.
        /// </summary>
        public uint RecordId { get; internal set; }

        /// <summary>
        /// The server's <c>ApplicationUri</c>. Used to correlate the record back
        /// to a <see cref="RegistrationEntry"/> when one exists.
        /// </summary>
        public string ServerUri { get; set; }

        /// <summary>
        /// The mDNS server name (display string) for this record.
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        /// One discovery URL the client can use to reach the server.
        /// </summary>
        public string DiscoveryUrl { get; set; }

        /// <summary>
        /// The capabilities advertised by the server.
        /// </summary>
        public IList<string> ServerCapabilities { get; set; } = new List<string>();

        /// <summary>
        /// Wall-clock timestamp of the most recent observation. Used for TTL-based
        /// pruning of mDNS-observed records.
        /// </summary>
        public DateTime LastSeenUtc { get; internal set; }

        /// <summary>
        /// True if the record originated from an mDNS network observation as
        /// opposed to an explicit RegisterServer2 call.
        /// </summary>
        public bool ObservedViaMulticast { get; set; }
    }
}
