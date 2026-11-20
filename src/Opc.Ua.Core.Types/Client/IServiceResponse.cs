// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a service response.
    /// </summary>
    public interface IServiceResponse : IEncodeable
    {
        /// <summary>
        /// The header for the response.
        /// </summary>
        /// <value>The response header.</value>
        ResponseHeader ResponseHeader { get; }
    }
}
