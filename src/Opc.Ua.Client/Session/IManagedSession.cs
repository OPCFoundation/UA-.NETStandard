// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// A managed session: the single client entry point that transparently handles
    /// reconnect and redundancy. The same session works against a non-redundant
    /// server, a transparent or non-transparent redundant set, and (via a client
    /// replica set) a session replicated across clients - no special API is needed.
    /// Redundancy is built in; <see cref="RedundancySupport"/> reports what the
    /// connected server offers.
    /// </summary>
    public interface IManagedSession : ISession
    {
        /// <summary>
        /// The redundancy support reported by the connected server, or
        /// <see cref="RedundancySupport.None"/> when the server is not redundant.
        /// </summary>
        RedundancySupport RedundancySupport { get; }

        /// <summary>
        /// The server redundancy snapshot of the last refresh: the redundancy mode, the <c>ServiceLevel</c> of
        /// the Server in use, the members of the redundant Server set with their resolved endpoints and
        /// errors, and the status of each redundancy Node read. <see langword="null"/> without a redundancy
        /// handler or before the first refresh.
        /// </summary>
        ServerRedundancyInfo? RedundancyInfo { get; }
    }
}
