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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Describes a durable ConfigurationFile update that has not been confirmed or reverted.
    /// </summary>
    /// <param name="UpdateId">The update identifier returned to the Client.</param>
    /// <param name="ApplyAtUtc">The earliest time the update may be applied.</param>
    /// <param name="RevertAtUtc">
    /// The time after which the update must be reverted, or <see langword="null"/> when no deadline exists.
    /// </param>
    /// <param name="IsApplied">
    /// <see langword="true"/> when the new configuration is active; otherwise, it is still staged.
    /// </param>
    public readonly record struct PendingApplicationConfigurationUpdate(
        Uuid UpdateId,
        DateTime ApplyAtUtc,
        DateTime? RevertAtUtc,
        bool IsApplied);

    /// <summary>
    /// Adds durable transaction storage and restart recovery to an application configuration file provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Providers that report <see cref="IApplicationConfigurationFileProvider.RequiresConfirmation"/> as
    /// <see langword="true"/> must implement this interface. The server rejects a confirmation-required
    /// update with <see cref="StatusCodes.BadNotSupported"/> before validation or provider mutation when the
    /// configured provider does not implement this contract.
    /// </para>
    /// <para>
    /// The provider must durably retain the previous configuration, the proposed configuration and targets,
    /// the update identifier, apply time and optional revert deadline before
    /// <see cref="BeginUpdateAsync"/> returns. Apply, confirm and revert operations must be atomic and
    /// idempotent for that identifier. A different identifier must never confirm or revert the pending
    /// update. During server startup, after configuring the ConfigurationFile handler and before serving
    /// requests, the server calls <see cref="GetPendingUpdateAsync"/> and restores or schedules the pending
    /// transition according to its apply and revert times.
    /// </para>
    /// <para>
    /// <see cref="ApplyPendingUpdateAsync"/> must apply the staged content and targets once, update
    /// <see cref="IApplicationConfigurationFileProvider.CurrentVersion"/> and
    /// <see cref="IApplicationConfigurationFileProvider.LastUpdateTime"/>, and report the applied state
    /// through <see cref="PendingApplicationConfigurationUpdate.IsApplied"/>. A failed apply must leave
    /// the active configuration unchanged. <see cref="RevertPendingUpdateAsync"/> restores the retained
    /// previous configuration and clears the pending transaction.
    /// </para>
    /// </remarks>
    public interface IRecoverableApplicationConfigurationFileProvider : IApplicationConfigurationFileProvider
    {
        /// <summary>
        /// Durably stages an update and its rollback state before the update is applied.
        /// </summary>
        /// <param name="updateId">The identifier that will be returned to the Client.</param>
        /// <param name="configuration">The validated configuration content.</param>
        /// <param name="targets">The validated update targets.</param>
        /// <param name="applyAtUtc">The earliest time the provider may apply the update.</param>
        /// <param name="revertAtUtc">
        /// The rollback deadline, or <see langword="null"/> when the update has no automatic deadline.
        /// </param>
        /// <param name="cancellationToken">A token used to cancel the staging operation.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when another update is pending or the update cannot be persisted.
        /// </exception>
        ValueTask BeginUpdateAsync(
            Uuid updateId,
            ByteString configuration,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            DateTime applyAtUtc,
            DateTime? revertAtUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the durable pending update, or <see langword="null"/> when none exists.
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the read.</param>
        /// <returns>The pending update state, when one exists.</returns>
        ValueTask<PendingApplicationConfigurationUpdate?> GetPendingUpdateAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies the update identified by <paramref name="updateId"/> and durably records that it is active.
        /// </summary>
        /// <param name="updateId">The exact pending update to apply.</param>
        /// <param name="cancellationToken">A token used to cancel the apply.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the update is unknown, not yet due, or cannot be applied atomically.
        /// </exception>
        ValueTask ApplyPendingUpdateAsync(
            Uuid updateId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finalizes the pending update identified by <paramref name="updateId"/>.
        /// </summary>
        /// <param name="updateId">The exact pending update to confirm.</param>
        /// <param name="cancellationToken">A token used to cancel the confirmation.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the identifier does not match an applied pending update.
        /// </exception>
        ValueTask ConfirmPendingUpdateAsync(
            Uuid updateId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores the previous configuration for the pending update identified by <paramref name="updateId"/>.
        /// </summary>
        /// <param name="updateId">The exact pending update to revert.</param>
        /// <param name="cancellationToken">A token used to cancel the revert.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the identifier does not match the pending update or restoration fails.
        /// </exception>
        ValueTask RevertPendingUpdateAsync(
            Uuid updateId,
            CancellationToken cancellationToken = default);
    }
}
