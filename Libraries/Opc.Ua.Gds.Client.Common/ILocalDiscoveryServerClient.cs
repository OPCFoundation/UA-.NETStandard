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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Public surface of <see cref="LocalDiscoveryServerClient"/>. Exposes the
    /// asynchronous discovery operations a client uses to find servers and
    /// query their endpoints from a Local Discovery Server.
    /// </summary>
    public interface ILocalDiscoveryServerClient : IAsyncDisposable
    {
        /// <summary>The application configuration in use.</summary>
        ApplicationConfiguration ApplicationConfiguration { get; }

        /// <summary>The message context derived from the configuration.</summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>Diagnostics flags requested for every discovery call.</summary>
        DiagnosticsMasks DiagnosticsMasks { get; }

        /// <summary>Locales preferred by the client.</summary>
        ArrayOf<string> PreferredLocales { get; set; }

        /// <summary>Default operation timeout for discovery calls.</summary>
        int DefaultOperationTimeout { get; set; }

        /// <summary>Finds servers known to the LDS.</summary>
        Task<ArrayOf<ApplicationDescription>> FindServersAsync(CancellationToken ct = default);

        /// <summary>Finds servers known to the LDS.</summary>
        Task<ArrayOf<ApplicationDescription>> FindServersAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            CancellationToken ct = default);

        /// <summary>Returns the endpoints exposed by the supplied server URL.</summary>
        Task<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            CancellationToken ct = default);

        /// <summary>Returns the endpoints exposed by the supplied server URL.</summary>
        Task<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            CancellationToken ct = default);

        /// <summary>Finds servers exposed via mDNS on the network.</summary>
        Task<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)>
            FindServersOnNetworkAsync(
                uint startingRecordId,
                uint maxRecordsToReturn,
                CancellationToken ct = default);

        /// <summary>Finds servers exposed via mDNS on the network.</summary>
        Task<(ArrayOf<ServerOnNetwork>, DateTimeUtc lastCounterResetTime)>
            FindServersOnNetworkAsync(
                string endpointUrl,
                string endpointTransportProfileUri,
                uint startingRecordId,
                uint maxRecordsToReturn,
                ArrayOf<string> serverCapabilityFilters,
                CancellationToken ct = default);
    }
}
