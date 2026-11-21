// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A reverse connection source that also reports which servers currently
    /// hold a connection it can hand out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With reverse connect (OPC 10000-6, 7.1.3) every member of a
    /// non-transparent redundant Server set (OPC 10000-4, 6.6.2) opens its own
    /// connections to the client. Set a source that implements this interface
    /// as <see cref="ServerRedundancyOptions.MemberConnections"/> to reach the
    /// members over their connections instead of dialing them.
    /// </para>
    /// <para>
    /// <see cref="ReverseConnectManager"/> implements this interface and
    /// reports the connections it holds while no registration matches them.
    /// </para>
    /// </remarks>
    public interface IReverseConnectionAvailability : IReverseConnectionSource
    {
        /// <summary>
        /// Gets the endpoint urls that the admitted connections of a server
        /// announced in their ReverseHello message, for the connections that
        /// are not yet handed out.
        /// </summary>
        /// <param name="serverUri">The application uri of the server.</param>
        /// <returns>The announced endpoint urls, or an empty list when the
        /// server holds no such connection.</returns>
        IReadOnlyList<Uri> GetAvailableEndpointUrls(string serverUri);
    }
}
