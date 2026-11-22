// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a service request.
    /// </summary>
    public interface IServiceRequest : IEncodeable
    {
        /// <summary>
        /// The header for the request.
        /// </summary>
        /// <value>The request header.</value>
        RequestHeader RequestHeader { get; set; }
    }
}
