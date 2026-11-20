// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// The decision of an <see cref="ISubscriptionAcknowledgementProvider"/> for a batch of
    /// acknowledgements: every acknowledgement is sent with the next Publish request, except
    /// the deferred ones, which are presented again with a later request.
    /// </summary>
    /// <param name="Deferred">Zero-based indices of the acknowledgements to defer.
    /// Indices outside the batch are ignored.</param>
    public readonly record struct AcknowledgementDecision(ArrayOf<int> Deferred)
    {
        /// <summary>
        /// Sends every acknowledgement of the batch.
        /// </summary>
        public static AcknowledgementDecision SendAll => default;
    }
}
