// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Bounds the optional server-wide event identity guarantee.
    /// </summary>
    public sealed record EventIdentityAdmissionOptions
    {
        /// <summary>
        /// Gets the maximum simultaneously tracked EventIds, including native
        /// publications, generation reservations and queued or retained occurrences.
        /// Native evidence is reclaimable after its event targets and notification
        /// references drain. Generation evidence also retains its active reservations.
        /// Reclamation can lag until unowned targets are collected.
        /// Exhaustion never evicts a live occurrence.
        /// </summary>
        public int MaxEventIdentities { get; init; } = 65_536;
    }

    /// <summary>
    /// Trusted provider evidence identifying one occurrence, independently of its
    /// EventId. Matching bytes or fields from different authorities are not evidence.
    /// </summary>
    public interface IEventIdentitySource
    {
        /// <summary>
        /// Determines whether both providers demonstrably describe the same occurrence.
        /// Admission requires agreement in both directions. Implementations must not
        /// perform I/O or call back into event publication.
        /// </summary>
        /// <param name="other">The event identity source.</param>
        bool IsSameOccurrence(IEventIdentitySource other);
    }
}
