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
    /// Routes the OPC UA MonitoredItem service set (Part 4 §5.13) —
    /// CreateMonitoredItems, ModifyMonitoredItems, SetMonitoringMode,
    /// SetTriggering, DeleteMonitoredItems — as REST endpoints per the
    /// OpenAPI mapping (Part 6 §G.3).
    /// </summary>
    [ApiController]
    public sealed class MonitoredItemController : WebApiControllerBase
    {
        /// <inheritdoc/>
        public MonitoredItemController(IWebApiServer server, ILoggerFactory loggerFactory)
            : base(server, loggerFactory)
        {
        }

        /// <summary>
        /// Creates one or more monitored items in a subscription
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13.2"/>).
        /// </summary>
        [HttpPost("/createmonitoreditems")]
        public Task CreateMonitoredItemsAsync(CancellationToken ct)
            => ExecuteAsync<CreateMonitoredItemsRequest, CreateMonitoredItemsResponse>(ct);

        /// <summary>
        /// Modifies one or more existing monitored items
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13.3"/>).
        /// </summary>
        [HttpPost("/modifymonitoreditems")]
        public Task ModifyMonitoredItemsAsync(CancellationToken ct)
            => ExecuteAsync<ModifyMonitoredItemsRequest, ModifyMonitoredItemsResponse>(ct);

        /// <summary>
        /// Sets the monitoring mode of one or more monitored items
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13.4"/>).
        /// </summary>
        [HttpPost("/setmonitoringmode")]
        public Task SetMonitoringModeAsync(CancellationToken ct)
            => ExecuteAsync<SetMonitoringModeRequest, SetMonitoringModeResponse>(ct);

        /// <summary>
        /// Creates or removes triggering links between monitored items
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13.5"/>).
        /// </summary>
        [HttpPost("/settriggering")]
        public Task SetTriggeringAsync(CancellationToken ct)
            => ExecuteAsync<SetTriggeringRequest, SetTriggeringResponse>(ct);

        /// <summary>
        /// Deletes one or more monitored items
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.13.6"/>).
        /// </summary>
        [HttpPost("/deletemonitoreditems")]
        public Task DeleteMonitoredItemsAsync(CancellationToken ct)
            => ExecuteAsync<DeleteMonitoredItemsRequest, DeleteMonitoredItemsResponse>(ct);
    }
}
