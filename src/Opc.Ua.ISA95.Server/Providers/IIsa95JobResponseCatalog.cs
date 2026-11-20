/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Provides the snapshot published by a <c>JobOrderResponseList</c> variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="IIsa95JobOrderCatalog"/> for the response
    /// side. It exists because <see cref="IIsa95JobResponseProviderV2"/> can only
    /// be asked for responses matching one job order id or one job order state:
    /// its state filter names a single top-level state, so "every response"
    /// cannot be expressed as a query and the list variable has no source
    /// without this.
    /// </para>
    /// <para>
    /// Job Control V1 declares no response list variable, so there is no V1
    /// counterpart here.
    /// </para>
    /// </remarks>
    public interface IIsa95JobResponseCatalog
    {
        /// <summary>
        /// Gets the current Job Control V2 job-response list.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// Every response the provider currently holds.
        /// </returns>
        ValueTask<ArrayOf<V2.ISA95JobResponseDataType>> GetJobResponsesV2Async(
            CancellationToken cancellationToken = default);
    }
}
