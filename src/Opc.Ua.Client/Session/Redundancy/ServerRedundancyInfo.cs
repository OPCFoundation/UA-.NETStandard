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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Server redundancy information read from the Server's
    /// <c>Server.ServerRedundancy</c> object.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-4 §6.6.2 and OPC 10000-5 §6.3.7.
    /// </remarks>
    public sealed class ServerRedundancyInfo
    {
        /// <summary>
        /// The <c>RedundancySupport</c> mode reported by the Server.
        /// </summary>
        public RedundancySupport Mode { get; init; }

        /// <summary>
        /// The Servers in the <c>RedundantServerSet</c>.
        /// </summary>
        public ArrayOf<RedundantServer> RedundantServers { get; init; } = [];

        /// <summary>
        /// The current <c>ServiceLevel</c> of the connected Server.
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// Whether <see cref="ServiceLevel"/> was read from the server.
        /// </summary>
        public bool ServiceLevelAccessible { get; init; }

        /// <summary>
        /// The OPC 10000-4 §6.6.2.4.2 subrange of the current Server's <c>ServiceLevel</c>.
        /// </summary>
        public ServiceLevelSubrange ServiceLevelSubrange { get; init; }

        /// <summary>
        /// The OPC 10000-4 §6.6.5 estimated time when a Server in Maintenance is expected to return.
        /// </summary>
        public DateTime EstimatedReturnTime { get; init; }

        /// <summary>
        /// The current Server identifier for Transparent redundancy.
        /// </summary>
        public string CurrentServerId { get; init; } = string.Empty;
    }
}
