/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Captures the node managers a history continuation depends on.
        /// </summary>
        /// <param name="context">The operation whose routing view owns the read.</param>
        /// <param name="state">The continuation state to capture.</param>
        /// <param name="validateSource">Whether restoration requires a current serving source.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>Good if captured or not required; otherwise the reason it cannot be captured.</returns>
        internal ValueTask<ServiceResult> CaptureHistoryContinuationAsync(
            OperationContext context,
            HistorianContinuationState state,
            bool validateSource,
            CancellationToken cancellationToken)
        {
            return m_serviceDispatch.CaptureHistoryContinuationAsync(
                context, state, cancellationToken, validateSource);
        }
    }
}
