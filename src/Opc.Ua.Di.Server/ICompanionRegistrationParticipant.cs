// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Enlists companion bindings in another builder's single subtree registration.
    /// </summary>
    /// <remarks>
    /// The owner prepares every participant, registers the root once, binds every
    /// participant, then commits them. Preparation and binding may fail; commitment
    /// must be non-throwing and must not perform I/O. Disposal aborts an uncommitted
    /// participant, releasing only its bindings and reservations, never the owner's
    /// tree. After commitment the node manager owns the binding lifetime.
    /// Participants are single-use and their callbacks are serialized by the owner.
    /// </remarks>
    public interface ICompanionRegistrationParticipant : IAsyncDisposable
    {
        /// <summary>
        /// Gets the manager which must perform the registration.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the exact, unregistered root instance being bound.
        /// </summary>
        BaseObjectState Root { get; }

        /// <summary>
        /// Configures and validates bindings before any node is published.
        /// </summary>
        ValueTask PrepareAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches runtime bindings after publication, without registering nodes again.
        /// </summary>
        ValueTask BindAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits prepared bindings after all fallible work has succeeded.
        /// </summary>
        void Commit();
    }
}
