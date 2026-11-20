/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Supplies an isolated connection boundary without bypassing registration authority or target provenance.
    /// Absent an injected implementation, the manager uses its normal managed-session connection path.
    /// </summary>
    internal interface IMcpSessionConnector
    {
        /// <summary>
        /// Creates one connected session whose lifetime is transferred to the manager.
        /// </summary>
        ValueTask<ISession> ConnectAsync(
            string name, string endpointUrl, string? securityMode, string? securityPolicy, string authType,
            string? username, string? password, bool autoAcceptCertificates, string? certificateIdentity,
            CancellationToken ct);

        /// <summary>
        /// Creates a JWT-authenticated session without repurposing another credential argument.
        /// </summary>
        ValueTask<ISession> ConnectJwtAsync(
            string name, string endpointUrl, string jwt, string? securityMode, string? securityPolicy,
            CancellationToken ct)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "The injected connector does not support JWT authentication.");
        }
    }
}
