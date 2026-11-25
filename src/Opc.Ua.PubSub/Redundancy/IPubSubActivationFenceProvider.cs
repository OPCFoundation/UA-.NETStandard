// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Exposes the current, locally confirmed activation lease fence to the PubSub runtime.
    /// </summary>
    public interface IPubSubActivationFenceProvider
    {
        /// <summary>
        /// Reads an unexpired lease fence without acquiring a lease or performing store I/O.
        /// </summary>
        /// <param name="componentId">Stable component identity registered with the activation coordinator.</param>
        /// <param name="fencingToken">The positive fence, when the lease is still valid.</param>
        /// <returns>Whether a current lease is confirmed for this instance.</returns>
        bool TryGetActiveFence(string componentId, out long fencingToken);
    }
}
