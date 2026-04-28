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

namespace Opc.Ua.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// Redundancy support mode reported by the server.
    /// </summary>
    /// <remarks>
    /// Maps to the OPC UA <c>RedundancySupport</c> enumeration
    /// defined in Part 5 §6.3.7.
    /// </remarks>
    public enum RedundancyMode
    {
        /// <summary>No redundancy.</summary>
        None = 0,

        /// <summary>Cold redundancy – backup servers are available but not running.</summary>
        Cold = 1,

        /// <summary>Warm redundancy – backup servers are running but not processing.</summary>
        Warm = 2,

        /// <summary>Hot redundancy – backup servers are running and processing.</summary>
        Hot = 3,

        /// <summary>Transparent redundancy – handled by infrastructure, invisible to clients.</summary>
        Transparent = 4,

        /// <summary>Hot and mirrored redundancy.</summary>
        HotAndMirrored = 5,
    }

    /// <summary>
    /// Information about a server in a redundant server set.
    /// </summary>
    /// <remarks>
    /// Corresponds to the OPC UA <c>RedundantServerDataType</c>
    /// defined in Part 5 §12.5.
    /// </remarks>
    public sealed class RedundantServerInfo
    {
        /// <summary>The URI that identifies the server.</summary>
        public string ServerUri { get; init; } = string.Empty;

        /// <summary>Service level (0–255, higher is better).</summary>
        public byte ServiceLevel { get; init; }

        /// <summary>The current state of the server.</summary>
        public ServerState ServerState { get; init; }
    }

    /// <summary>
    /// Server redundancy information read from the server's
    /// <c>Server.ServerRedundancy</c> object.
    /// </summary>
    /// <remarks>
    /// See OPC UA Part 4 §6.6.2 and Part 5 §6.3.7.
    /// </remarks>
    public sealed class ServerRedundancyInfo
    {
        /// <summary>The redundancy mode reported by the server.</summary>
        public RedundancyMode Mode { get; init; }

        /// <summary>The redundant servers in the set.</summary>
        public IReadOnlyList<RedundantServerInfo> RedundantServers { get; init; }
            = [];

        /// <summary>The current service level of the connected server.</summary>
        public byte ServiceLevel { get; init; }
    }
}
