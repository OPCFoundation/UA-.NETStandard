// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Records host security state after all staged store mutations, before transaction ownership is released.
    /// Implementations must not activate endpoints or replay installation effects. A failure compensates the
    /// preceding store operations; recovery must detect, rather than reinstall, any mismatched recorded state.
    /// </summary>
    public interface IPushConfigurationCompletion
    {
        /// <summary>
        /// Records the committed stores while the owning session still excludes other configuration writers.
        /// </summary>
        ValueTask CompleteAsync(ISystemContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Appends a host completion operation atomically when taking ownership of the staged commit batch.
    /// </summary>
    public interface IPushConfigurationTransactionCompletion
    {
        /// <summary>
        /// Runs completion last, only for a valid nonempty transaction. Earlier failures skip completion;
        /// completion failure compensates preceding operations before ownership is released.
        /// </summary>
        ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects committedEffects,
            PushConfigurationOperation completion,
            CancellationToken cancellationToken = default);
    }
}
