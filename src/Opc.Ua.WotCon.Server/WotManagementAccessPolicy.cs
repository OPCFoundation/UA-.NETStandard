// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Access policy applied to selected remote management operations in
    /// the WoT Registry and Connectivity address spaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This policy protects the five methods exposed by the standard
    /// <c>WoTAssetConnectionManagement</c> object and selected WoT Registry
    /// management methods and dependency-observation property reads.
    /// </para>
    /// <para>
    /// The defaults are intentionally restrictive: a remote caller
    /// must be using a <see cref="MessageSecurityMode.SignAndEncrypt"/>
    /// channel, present a non-anonymous user identity, and have been
    /// granted the
    /// <see cref="Ua.ObjectIds.WellKnownRole_SecurityAdmin"/>
    /// role by the OPC UA server's role-mapping layer.
    /// </para>
    /// <para>
    /// Calls made without a session <see cref="Opc.Ua.Server.OperationContext"/> are not
    /// affected by this policy; the check is applied only to protected
    /// address-space operations.
    /// </para>
    /// </remarks>
    public sealed class WotManagementAccessPolicy
    {
        /// <summary>
        /// The role a caller must hold to invoke management methods.
        /// Defaults to
        /// <see cref="Ua.ObjectIds.WellKnownRole_SecurityAdmin"/>.
        /// </summary>
        public NodeId RequiredRoleId { get; init; } =
            Ua.ObjectIds.WellKnownRole_SecurityAdmin;

        /// <summary>
        /// The minimum acceptable channel security mode. Defaults to
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/>; channels
        /// using <see cref="MessageSecurityMode.None"/> or
        /// <see cref="MessageSecurityMode.Sign"/> are rejected. A conformant
        /// registry mutation surface shall not configure a weaker mode.
        /// </summary>
        public MessageSecurityMode MinimumSecurityMode { get; init; } =
            MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// Whether anonymous user identities are permitted. Defaults
        /// to <c>false</c>; an anonymous session is rejected even if
        /// the channel is encrypted.
        /// </summary>
        public bool AllowAnonymous { get; init; }
    }
}
