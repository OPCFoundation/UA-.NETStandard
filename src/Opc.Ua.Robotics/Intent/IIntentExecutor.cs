// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.RobotIntent
{
    /// <summary>
    /// Carries out intents on the robot.
    /// </summary>
    /// <remarks>
    /// The host owns admission, queueing, the state machine, cancellation and the
    /// result; an implementation of this interface owns only the doing. Translating an
    /// intent into whatever the controller actually executes - URScript, RAPID, KRL, a
    /// TP program - is the whole of its job.
    /// </remarks>
    public interface IIntentExecutor
    {
        /// <summary>
        /// Executes one intent.
        /// </summary>
        /// <remarks>
        /// The cancellation token is signalled when a cancel has been ACCEPTED, which
        /// is the point at which the operation enters Cancelling. An implementation
        /// reads <see cref="IntentExecution.StopMode"/> to choose the requested
        /// application stop urgency, brings motion to a controlled end and then
        /// returns; it need not return Cancelled, because the host records the
        /// cancellation itself.
        /// </remarks>
        ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken);

        /// <summary>
        /// Decides whether a cancel may be accepted for an intent that is executing.
        /// </summary>
        /// <remarks>
        /// Some motions cannot be abandoned part-way without leaving the cell in a
        /// worse state than completing them - a tool change mid-exchange, a placement
        /// mid-release. Returning false refuses this one occasion; declaring
        /// CancelSupported false in the capability refuses the whole intent type in
        /// advance.
        /// <para>
        /// This has no default implementation because the library targets .NET
        /// Framework, which has none, and because whether a motion can be safely
        /// abandoned is a decision worth making deliberately. An executor that has no
        /// such motions returns true.
        /// </para>
        /// </remarks>
        bool CanCancel(IntentExecution execution);
    }
}
