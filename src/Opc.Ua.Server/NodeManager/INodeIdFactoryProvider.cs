// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Supplies the <see cref="IRebasableNodeIdFactory"/> that NodeManagers use
    /// to mint NodeIds for nodes created at runtime.
    /// </summary>
    /// <remarks>
    /// <see cref="ServerInternalData"/> implements this so that a factory
    /// registered once in dependency injection reaches every
    /// <see cref="AsyncCustomNodeManager"/> the server hosts, without each
    /// NodeManager having to take a dependency on the service provider.
    /// </remarks>
    public interface INodeIdFactoryProvider
    {
        /// <summary>
        /// The factory the server was configured with, or <c>null</c> to let
        /// each NodeManager use its own default.
        /// </summary>
        IRebasableNodeIdFactory? NodeIdFactory { get; }

        /// <summary>
        /// Whether the server's NodeManagers refuse to mint a NodeId they
        /// already gave a different browse path, or <c>null</c> to leave each
        /// factory on its own default.
        /// </summary>
        /// <remarks>
        /// Server-wide rather than per NodeManager, because the record a
        /// factory keeps to answer the question costs memory that grows with
        /// the address space - a decision about the whole server, not about
        /// one part of it.
        /// </remarks>
        bool? DetectNodeIdCollisions { get; }
    }
}
