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

namespace Opc.Ua.Bindings.WebApi.Controllers
{
    /// <summary>
    /// Routes the OPC UA Discovery service set (Part 4 §5.5) —
    /// FindServers, GetEndpoints — as REST endpoints per the
    /// OpenAPI mapping (Part 6 §G.3).
    /// </summary>
    [ApiController]
    public sealed class DiscoveryController : WebApiControllerBase
    {
        /// <inheritdoc/>
        public DiscoveryController(IWebApiServer server, ILoggerFactory loggerFactory)
            : base(server, loggerFactory)
        {
        }

        /// <summary>
        /// Returns the list of servers known to the discovery endpoint
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.5.2"/>).
        /// </summary>
        [HttpPost("/findservers")]
        public Task FindServersAsync(CancellationToken ct)
            => ExecuteAsync<FindServersRequest, FindServersResponse>(ct);

        /// <summary>
        /// Returns the endpoints supported by a server
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.5.4"/>).
        /// </summary>
        [HttpPost("/getendpoints")]
        public Task GetEndpointsAsync(CancellationToken ct)
            => ExecuteAsync<GetEndpointsRequest, GetEndpointsResponse>(ct);
    }
}
