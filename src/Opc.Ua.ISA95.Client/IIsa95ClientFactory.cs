// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.ISA95.Client
{
    /// <summary>
    /// Creates ISA-95 clients from existing sessions or the registered managed
    /// session connection.
    /// </summary>
    public interface IIsa95ClientFactory
    {
        /// <summary>
        /// Creates an ISA-95 client over an existing session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <returns>The created ISA-95 client.</returns>
        Isa95Client Create(ISession session);

        /// <summary>
        /// Gets the lazily connected ISA-95 client.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The connected ISA-95 client.</returns>
        ValueTask<Isa95Client> ConnectAsync(
            CancellationToken cancellationToken = default);
    }
}
