// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Applies optional pause, resume, and completion operations for control functions.
    /// </summary>
    /// <remarks>
    /// The callback completes only after the requested physical change has been acknowledged.
    /// A bad result prevents the control function from entering the corresponding stable state.
    /// </remarks>
    public interface ILadsControlFunctionRunningProvider : ILadsFunctionProvider
    {
        /// <summary>
        /// Changes the execution of a control function before its running state is updated.
        /// </summary>
        /// <param name="function">The control function.</param>
        /// <param name="target">The acknowledged execution state.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The hardware acknowledgment; a bad result rejects the operation.</returns>
        ValueTask<ServiceResult> ChangeExecutionAsync(
            ILadsControlFunction function,
            LadsRunningStateValue target,
            CancellationToken cancellationToken);
    }
}
