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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Rest.Controllers
{
    /// <summary>
    /// Routes the OPC UA Session service set (Part 4 §5.7) —
    /// CreateSession, ActivateSession, CloseSession, Cancel — as REST
    /// endpoints per the OpenAPI mapping (Part 6 §G.3).
    /// </summary>
    [ApiController]
    public sealed class SessionController : RestApiControllerBase
    {
        /// <inheritdoc/>
        public SessionController(IRestApiServer server, ILoggerFactory loggerFactory)
            : base(server, loggerFactory)
        {
        }

        /// <summary>
        /// Creates a new session with the server
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.7.2"/>).
        /// </summary>
        [HttpPost("/createsession")]
        public Task CreateSessionAsync(CancellationToken ct)
            => ExecuteAsync<CreateSessionRequest, CreateSessionResponse>(ct);

        /// <summary>
        /// Activates a previously-created session
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.7.3"/>).
        /// </summary>
        [HttpPost("/activatesession")]
        public Task ActivateSessionAsync(CancellationToken ct)
            => ExecuteAsync<ActivateSessionRequest, ActivateSessionResponse>(ct);

        /// <summary>
        /// Closes a session
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.7.4"/>).
        /// </summary>
        [HttpPost("/closesession")]
        public Task CloseSessionAsync(CancellationToken ct)
            => ExecuteAsync<CloseSessionRequest, CloseSessionResponse>(ct);

        /// <summary>
        /// Cancels outstanding requests on a session
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.7.5"/>).
        /// </summary>
        [HttpPost("/cancel")]
        public Task CancelAsync(CancellationToken ct)
            => ExecuteAsync<CancelRequest, CancelResponse>(ct);
    }
}
