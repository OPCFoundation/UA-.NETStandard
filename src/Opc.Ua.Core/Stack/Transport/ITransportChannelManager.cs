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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Creates channels and provides ability to monitor them
    /// </summary>
    public interface ITransportChannelManager
    {
        /// <summary>
        /// Callback to register and receive channel diagnostics
        /// </summary>
        event Action<ITransportChannel, TransportChannelDiagnostic>? OnDiagnostics;

        /// <summary>
        /// Opens a transport and transfers the supplied certificate handles to it, releasing them on failure.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <param name="context">The service message context.</param>
        /// <param name="clientCertificate">The client certificate handle whose ownership is transferred.</param>
        /// <param name="clientCertificateChain">The certificate chain whose ownership is transferred.</param>
        /// <param name="connection">An optional reverse-connect waiting connection.</param>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        /// <returns>The opened transport channel.</returns>
        /// <remarks>
        /// Ownership transfers when this method is called, even if the manager is disposed or argument validation
        /// fails before transport creation. Callers retaining reusable certificate material must pass independently
        /// owned references rather than reuse the transferred handles.
        /// </remarks>
        ValueTask<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            IServiceMessageContext context,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain = null,
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default);
    }
}
