/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Default executor used when a host publishes the address space before an application executor is supplied.
    /// </summary>
    public sealed class RobotIntentRejectingExecutor : IIntentExecutor
    {
        /// <inheritdoc/>
        public ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IntentOutcome>(IntentOutcome.Fail(
                IntentFailureEnum.CapabilityNotSupported,
                "No Robot Intent executor is registered."));
        }

        /// <inheritdoc/>
        public bool CanCancel(IntentExecution execution)
        {
            return true;
        }
    }
}
