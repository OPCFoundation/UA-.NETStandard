// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Performs awaited initialization that requires a published address space and
    /// initialized server subsystems, including registration of dependent NodeManagers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// StandardServer invokes this optional participant for its initial NodeManagers.
    /// The lifecycle invokes it for each newly committed Add or reload generation.
    /// Both await completion before returning to their caller. Custom hosts must invoke
    /// it after address-space creation and server initialization, not during preparation.
    /// </para>
    /// <para>
    /// Runtime readiness runs outside registration serialization. The generation remains
    /// published and owned by its startup operation: competing removal or reload is
    /// rejected until that operation completes. Runtime publication invokes readiness with
    /// a non-cancelable token; canceling the original registration request cannot abandon
    /// committed initialization. A readiness failure is a post-commit failure, not a rollback.
    /// The live registration remains available
    /// for recovery or removal. DeleteAddressSpaceAsync must release any dependencies
    /// created before the failure.
    /// </para>
    /// </remarks>
    public interface INodeManagerReadinessNotification
    {
        /// <summary>
        /// Completes initialization once runtime lifecycle operations are available.
        /// </summary>
        /// <param name="cancellationToken">
        /// The host's startup cancellation token, or a non-cancelable token for committed runtime publication.
        /// </param>
        ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Synchronous counterpart of <see cref="INodeManagerReadinessNotification"/> for
    /// <see cref="INodeManager"/> implementations. <see cref="AsyncNodeManagerAdapter"/> invokes it
    /// when the wrapped manager implements it.
    /// </summary>
    public interface ISyncNodeManagerReadinessNotification
    {
        /// <summary>
        /// Completes initialization once runtime lifecycle operations are available.
        /// </summary>
        void OnServerReady();
    }

    /// <summary>
    /// Readiness participation helpers for adapted and directly implemented managers.
    /// </summary>
    internal static class NodeManagerReadiness
    {
        /// <summary>
        /// Gets whether a manager or its wrapped implementation has a readiness callback.
        /// </summary>
        /// <param name="nodeManager">The manager to inspect.</param>
        /// <returns>True when the manager has a callback that must complete before readiness is released.</returns>
        internal static bool IsParticipant(IAsyncNodeManager nodeManager)
        {
            return nodeManager switch
            {
                AsyncNodeManagerAdapter adapter => adapter.HasReadinessParticipant,
                _ => nodeManager is INodeManagerReadinessNotification
            };
        }
    }
}
