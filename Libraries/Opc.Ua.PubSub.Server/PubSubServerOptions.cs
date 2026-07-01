/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Options consumed by <see cref="PubSubNodeManager"/> when it
    /// mounts the standard <c>PublishSubscribe</c> Object (Part 14
    /// §9.1) onto a hosting OPC UA Server's address space.
    /// </summary>
    /// <remarks>
    /// Bound from the <c>OpcUa:Server:PubSub</c> configuration
    /// section by default. Mirrors the pattern used by
    /// <c>Opc.Ua.WotCon.Server.WotConnectivityServerOptions</c>:
    /// every property is settable so the AOT-safe configuration
    /// binding source generator can populate the instance from
    /// <c>IConfiguration</c>.
    /// </remarks>
    public sealed class PubSubServerOptions
    {
        /// <summary>
        /// When <see langword="true"/>, exposes the standard
        /// <c>PubSubKeyServiceType</c> Object (Part 14 §8.3.1) by
        /// binding the <c>GetSecurityKeys</c>,
        /// <c>GetSecurityGroup</c>, <c>AddSecurityGroup</c> and
        /// <c>RemoveSecurityGroup</c> methods to the registered
        /// <see cref="Opc.Ua.PubSub.Security.Sks.IPubSubKeyServiceServer"/>.
        /// </summary>
        public bool ExposeSecurityKeyService { get; set; }

        /// <summary>
        /// When <see langword="true"/> (the default), binds the
        /// configuration methods (<c>SetSecurityKeys</c>,
        /// <c>AddConnection</c>, <c>RemoveConnection</c>) on the
        /// <c>PublishSubscribe</c> Object. Disable to expose a
        /// read-only PubSub model.
        /// </summary>
        public bool ExposeConfigurationMethods { get; set; } = true;

        /// <summary>
        /// Optional convenience: when set and
        /// <see cref="ExposeSecurityKeyService"/> is
        /// <see langword="true"/>, a SecurityGroup with this
        /// identifier is created on start-up (no-op if it already
        /// exists).
        /// </summary>
        public string? DefaultSecurityGroupId { get; set; }

        /// <summary>
        /// Optional default SecurityPolicyUri used when seeding the
        /// default SecurityGroup. Defaults to
        /// <c>http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR</c>.
        /// </summary>
        public string? DefaultSecurityPolicyUri { get; set; }

        /// <summary>
        /// Optional default key lifetime applied to the default
        /// SecurityGroup. Defaults to one hour.
        /// </summary>
        public double DefaultKeyLifetimeMs { get; set; } = 3_600_000;

        /// <summary>
        /// Optional caller identities allowed to pull keys from the default SecurityGroup.
        /// </summary>
        public string[]? DefaultAuthorizedCallerIdentities { get; set; }

        /// <summary>
        /// Optional RolePermissions controlling GetSecurityKeys Call access for the default SecurityGroup.
        /// </summary>
        public RolePermissionType[]? DefaultSecurityGroupRolePermissions { get; set; }

        /// <summary>
        /// Controls how much of the standard
        /// <c>PubSubDiagnosticsType</c> sub-tree is bound to the
        /// runtime <see cref="Opc.Ua.PubSub.Diagnostics.IPubSubDiagnostics"/>.
        /// </summary>
        public PubSubDiagnosticsExposure DiagnosticsExposure { get; set; }
            = PubSubDiagnosticsExposure.Counters;
    }
}
