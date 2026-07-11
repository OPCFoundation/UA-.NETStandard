/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Access policy applied to the five management methods exposed by
    /// the standard <c>WoTAssetConnectionManagement</c> object:
    /// <c>CreateAsset</c>, <c>DeleteAsset</c>, <c>DiscoverAssets</c>,
    /// <c>CreateAssetForEndpoint</c> and <c>ConnectionTest</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defaults are intentionally restrictive: a remote caller
    /// must be using a <see cref="MessageSecurityMode.SignAndEncrypt"/>
    /// channel, present a non-anonymous user identity, and have been
    /// granted the
    /// <see cref="Ua.ObjectIds.WellKnownRole_SecurityAdmin"/>
    /// role by the OPC UA server's role-mapping layer.
    /// </para>
    /// <para>
    /// Internal callers (those invoking the underlying
    /// <see cref="Assets.AssetRegistry"/> APIs directly without flowing
    /// an <c>OperationContext</c>) are not affected by this policy and
    /// continue to work without authentication; the check is applied
    /// only to address-space method invocations.
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
        /// <see cref="MessageSecurityMode.Sign"/> are rejected.
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
