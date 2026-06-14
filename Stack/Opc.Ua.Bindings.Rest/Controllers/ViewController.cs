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
    /// Routes the OPC UA View service set (Part 4 §5.9) — Browse,
    /// BrowseNext, TranslateBrowsePathsToNodeIds, RegisterNodes,
    /// UnregisterNodes — as REST endpoints per the OpenAPI mapping
    /// (Part 6 §G.3).
    /// </summary>
    [ApiController]
    public sealed class ViewController : RestApiControllerBase
    {
        /// <inheritdoc/>
        public ViewController(IRestApiServer server, ILoggerFactory loggerFactory)
            : base(server, loggerFactory)
        {
        }

        /// <summary>
        /// Browses the children of one or more nodes
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9.2"/>).
        /// </summary>
        [HttpPost("/browse")]
        public Task BrowseAsync(CancellationToken ct)
            => ExecuteAsync<BrowseRequest, BrowseResponse>(ct);

        /// <summary>
        /// Continues a previously-started browse using its continuation point
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9.3"/>).
        /// </summary>
        [HttpPost("/browsenext")]
        public Task BrowseNextAsync(CancellationToken ct)
            => ExecuteAsync<BrowseNextRequest, BrowseNextResponse>(ct);

        /// <summary>
        /// Translates BrowsePaths to NodeIds
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9.4"/>).
        /// </summary>
        [HttpPost("/translate")]
        public Task TranslateAsync(CancellationToken ct)
            => ExecuteAsync<TranslateBrowsePathsToNodeIdsRequest, TranslateBrowsePathsToNodeIdsResponse>(ct);

        /// <summary>
        /// Registers nodes with the server for low-overhead repeated access
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9.5"/>).
        /// </summary>
        [HttpPost("/registernodes")]
        public Task RegisterNodesAsync(CancellationToken ct)
            => ExecuteAsync<RegisterNodesRequest, RegisterNodesResponse>(ct);

        /// <summary>
        /// Unregisters previously-registered nodes
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.9.6"/>).
        /// </summary>
        [HttpPost("/unregisternodes")]
        public Task UnregisterNodesAsync(CancellationToken ct)
            => ExecuteAsync<UnregisterNodesRequest, UnregisterNodesResponse>(ct);
    }
}
