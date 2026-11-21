// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Shared retry budget passed from the outer reconnect driver
    /// (e.g. ManagedSession.ConnectionStateMachine) into the channel
    /// manager so both layers respect a single maximum total elapsed
    /// time. Implementations are thread-safe; multiple participants
    /// may observe / consume budget concurrently.
    /// </summary>
    public interface IRetryBudget
    {
        /// <summary>
        /// Total wall-clock time since the first attempt under this
        /// budget.
        /// </summary>
        TimeSpan ElapsedSinceFirstAttempt { get; }

        /// <summary>
        /// Whether the budget is exhausted (no more attempts allowed).
        /// </summary>
        bool IsExhausted { get; }

        /// <summary>
        /// Try to consume another attempt; returns the remaining
        /// time window if any.
        /// </summary>
        bool TryConsume(out TimeSpan remaining);

        /// <summary>
        /// Reset the elapsed counter (e.g. on successful reconnect so
        /// subsequent reconnect cycles get a fresh budget).
        /// </summary>
        void Reset();
    }
}
