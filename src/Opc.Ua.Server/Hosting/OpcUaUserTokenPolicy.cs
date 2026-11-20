// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Binder-friendly description of one OPC UA user-token policy. Each
    /// entry expressed in <see cref="OpcUaServerOptions.UserTokenPolicies"/>
    /// is turned into a call to
    /// <c>IApplicationConfigurationBuilderServerSelected.AddUserTokenPolicy</c>
    /// by the hosted service.
    /// </summary>
    /// <remarks>
    /// When the list is empty, the hosted service adds an
    /// <see cref="UserTokenType.Anonymous"/> entry by default — matching
    /// the historical builder behaviour when no token policy is supplied.
    /// </remarks>
    public sealed class OpcUaUserTokenPolicy
    {
        /// <summary>
        /// The token type to advertise. Defaults to
        /// <see cref="UserTokenType.Anonymous"/>.
        /// </summary>
        public UserTokenType TokenType { get; set; } = UserTokenType.Anonymous;
    }
}
