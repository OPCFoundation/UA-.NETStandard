// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides peer descriptions for a non-transparent redundant server set.
    /// </summary>
    public interface IRedundantServerSetProvider
    {
        /// <summary>
        /// Gets peer <see cref="ApplicationDescription"/> entries returned by
        /// <c>FindServers</c> for redundant server discovery.
        /// </summary>
        /// <returns>The peer server descriptions.</returns>
        ArrayOf<ApplicationDescription> GetRedundantServerSet();
    }
}
