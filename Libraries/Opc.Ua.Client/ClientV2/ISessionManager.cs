#if OPCUA_CLIENT_V2
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
    using Opc.Ua;
    using Opc.Ua.Client.Sessions;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Found endpoints
    /// </summary>
    /// <param name="Description"></param>
    /// <param name="AccessibleEndpointUrl"></param>
    /// <param name="Capabilities"></param>
    public sealed record class FoundEndpoint(EndpointDescription Description,
        string AccessibleEndpointUrl, HashSet<string> Capabilities);

    /// <summary>
    /// Manages connecting sessions
    /// </summary>
    public interface ISessionManager : IDisposable
    {
        /// <summary>
        /// Connect a session or get a session from the session pool. The session
        /// in the pool is retrieved using the same options are passed. If the
        /// session needs to be created it is created using the configured
        /// resiliency options. The returned object can be used to release the
        /// ownership of the session back to the pool.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ISessionHandle> GetOrConnectAsync(PooledSessionOptions connection,
            CancellationToken ct = default);

        /// <summary>
        /// Connect a session directly to the endpoint. The session is not pooled.
        /// Max settings are bypassed. Uses the resiliency strategy configured to
        /// connect or none if none was specified.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="options"></param>
        /// <param name="useReverseConnect"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ISessionHandle> ConnectAsync(EndpointDescription endpoint,
            SessionCreateOptions? options = null, bool useReverseConnect = false,
            CancellationToken ct = default);

        /// <summary>
        /// Test connecting to an endpoint using the specified options.
        /// The session is created and closed and result returned. No
        /// resiliency policy is applied.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="useReverseConnect"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ServiceResult> TestAsync(EndpointDescription endpoint,
            bool useReverseConnect = false, CancellationToken ct = default);
    }
}
#endif
