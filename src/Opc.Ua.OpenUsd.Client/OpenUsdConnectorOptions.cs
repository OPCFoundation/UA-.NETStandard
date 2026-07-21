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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Configures an <see cref="OpenUsdConnector"/>. The progressive-disclosure entry
    /// points (<c>new OpenUsdConnector(session, sink)</c> and the DI
    /// <c>AddOpenUsdConnector(...)</c> extensions) build one of these; advanced callers
    /// construct it directly.
    /// </summary>
    public sealed class OpenUsdConnectorOptions
    {
        /// <summary>
        /// Opt-in gate for actuating <c>UsdToUaCommand</c> bindings. Command bindings are
        /// always discovered, but the connector refuses to actuate one unless this is
        /// <c>true</c> (fail-closed). Read-only telemetry/alarm/history bindings are
        /// unaffected. Default <c>false</c>.
        /// </summary>
        public bool EnableCommands { get; set; }

        /// <summary>
        /// Optional factory that opens sessions to other servers for cross-server
        /// components (§5.14). When <c>null</c>, a cross-server component is composed
        /// structurally (its reference prim is authored) but its remote bindings are not
        /// driven.
        /// </summary>
        public Func<string, CancellationToken, Task<ISession>>? RemoteSessionFactory { get; set; }

        /// <summary>
        /// Maximum number of bytes read for a single served USD asset. Fails closed
        /// against a server that streams unbounded data. Default 64 MiB.
        /// </summary>
        public int MaxAssetBytes { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Maximum total number of bytes read across a whole asset-closure fetch. Default
        /// 256 MiB.
        /// </summary>
        public long MaxTotalAssetBytes { get; set; } = 256L * 1024 * 1024;
    }
}
