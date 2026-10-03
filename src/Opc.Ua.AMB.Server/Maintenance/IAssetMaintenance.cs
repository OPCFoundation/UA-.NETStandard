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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Server.Maintenance
{
    /// <summary>
    /// The current and future maintenance activities of a registered asset
    /// (OPC 10000-110 §12).
    /// </summary>
    public interface IAssetMaintenance
    {
        /// <summary>
        /// Gets the maintenance activities, in registration order.
        /// </summary>
        ArrayOf<IMaintenanceActivity> Activities { get; }

        /// <summary>
        /// Finds a maintenance activity by the name it was registered with.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="activity">The activity, when found.</param>
        /// <returns><see langword="true"/> when the asset has the activity.</returns>
        bool TryGetActivity(string name, [NotNullWhen(true)] out IMaintenanceActivity? activity);
    }

    /// <summary>
    /// A maintenance activity of an asset: a condition that implements
    /// <c>IMaintenanceEventType</c> and moves through the
    /// <c>MaintenanceEventStateMachineType</c> (OPC 10000-110 §12.2, §12.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// While the activity is planned or executing, the condition is active
    /// and retained (§12.1). Finishing it makes the condition inactive; it
    /// stays retained until a client acknowledges it. Every transition and
    /// every update reports an event, so the event history keeps the planned
    /// and the actual values apart.
    /// </para>
    /// <para>
    /// A failed execution is reported through the message (§12.1): finish
    /// the activity with a message that says so.
    /// </para>
    /// </remarks>
    public interface IMaintenanceActivity
    {
        /// <summary>
        /// Gets the name the activity was registered with, which is also its
        /// browse name and <c>ConditionName</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the NodeId of the condition, which is also its
        /// <c>ConditionId</c>.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the maintenance condition class the activity reports.
        /// </summary>
        AmbConditionClass ConditionClass { get; }

        /// <summary>
        /// Gets the current state of the activity.
        /// </summary>
        MaintenanceStateKind State { get; }

        /// <summary>
        /// Gets a copy of what the activity currently publishes.
        /// </summary>
        MaintenanceActivityDetails Details { get; }

        /// <summary>
        /// Starts executing the activity (<c>FromPlannedToExecuting</c>).
        /// </summary>
        /// <param name="message">The message of the event; the description when empty.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the activity is not
        /// planned, or a transition guard refused the transition.
        /// </exception>
        ValueTask StartAsync(LocalizedText message = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finishes the activity (<c>FromExecutingToFinished</c>).
        /// </summary>
        /// <param name="message">
        /// The message of the event; the description when empty. Say here when
        /// the execution failed.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the activity is not
        /// executing, or a transition guard refused the transition.
        /// </exception>
        ValueTask FinishAsync(LocalizedText message = default, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plans the next occurrence of a recurring activity
        /// (<c>FromFinishedToPlanned</c>, §12.3).
        /// </summary>
        /// <param name="plannedDate">The date of the next occurrence, when known.</param>
        /// <param name="message">The message of the event; the description when empty.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the activity is not
        /// finished, or a transition guard refused the transition.
        /// </exception>
        ValueTask ReplanAsync(
            DateTimeUtc? plannedDate = null,
            LocalizedText message = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes what the activity publishes, in any state, and reports the
        /// change.
        /// </summary>
        /// <param name="update">Changes a copy of the current details.</param>
        /// <param name="message">The message of the event; the description when empty.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is <c>null</c>.</exception>
        ValueTask UpdateAsync(
            Action<MaintenanceActivityDetails> update,
            LocalizedText message = default,
            CancellationToken cancellationToken = default);
    }
}
