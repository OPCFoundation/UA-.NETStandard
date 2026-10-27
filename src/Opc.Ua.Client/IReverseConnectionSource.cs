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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Supplies server-initiated (reverse connect) connections to a
    /// <see cref="ManagedSession"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With reverse connect (OPC 10000-6, 7.1.3) the server opens the
    /// connection, so the client cannot dial a new one when it has to
    /// re-establish a session (OPC 10000-4, 6.7). A managed session that is
    /// configured with a source awaits it for the initial connect, unless an
    /// initial connection is passed, and once per recovery attempt of its
    /// reconnect policy.
    /// </para>
    /// <para>
    /// <see cref="ReverseConnectManager"/> implements this interface. Implement
    /// it when the application accepts reverse connections itself, for example
    /// to admit them before the OPC UA handshake or to hand over connections
    /// accepted elsewhere.
    /// </para>
    /// </remarks>
    public interface IReverseConnectionSource
    {
        /// <summary>
        /// Waits for the next reverse connection of a server endpoint.
        /// </summary>
        /// <param name="endpointUrl">The endpoint url the server announced
        /// in its ReverseHello message.</param>
        /// <param name="serverUri">The application uri of the server, or
        /// <c>null</c> to accept any server at <paramref name="endpointUrl"/>.
        /// </param>
        /// <param name="ct">Cancelled when the caller no longer needs the
        /// connection, for example when the connect or recovery attempt ends
        /// or the session is disposed.</param>
        /// <returns>The connection. Its ownership passes to the caller, which
        /// closes it when it does not use it.</returns>
        ValueTask<ITransportWaitingConnection> WaitForConnectionAsync(
            Uri endpointUrl,
            string? serverUri,
            CancellationToken ct);
    }
}
