// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ModelChange
{
    /// <summary>
    /// Provides the client side namespace table together with the
    /// ability to re-read it from the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server may append namespace uris while it is running — for
    /// example when a NodeManager is registered live. It signals this
    /// by updating <c>Server_NamespaceArray</c> and
    /// <c>Server_UrisVersion</c> and reporting an address space model
    /// change. A <see cref="ModelChangeTracker"/> that is given an
    /// implementation of this interface re-reads the namespace table
    /// when it observes such a change, so NodeIds from newly added
    /// namespaces keep resolving.
    /// </para>
    /// <para>
    /// Both <see cref="Session"/> and <see cref="ManagedSession"/>
    /// implement this interface, so either can be handed to the
    /// tracker directly. Supply a custom implementation to control
    /// how and when the table is refreshed.
    /// </para>
    /// </remarks>
    public interface INamespaceTableRefresher
    {
        /// <summary>
        /// The table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Re-reads the server's namespace and server uri tables and
        /// updates <see cref="NamespaceUris"/> in place.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task FetchNamespaceTablesAsync(CancellationToken ct = default);
    }
}
