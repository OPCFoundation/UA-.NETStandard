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

using System;
using System.Collections.Generic;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Server-side identity-mapping &amp; role-configuration store for
    /// OPC UA Part 18 §4 (Role-Based Security). The default in-process
    /// implementation lives in <see cref="RoleManager"/>; integrators that
    /// want to back roles with a custom store (e.g. an LDAP directory or a
    /// database) implement this interface and inject an instance via
    /// <see cref="IServerInternal.SetRoleManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be thread-safe; callers may invoke these members
    /// from multiple session threads concurrently.
    /// </para>
    /// <para>
    /// Every successful mutator raises <see cref="RoleConfigurationChanged"/>
    /// so the <see cref="ISessionManager"/> can re-evaluate granted roles on
    /// active sessions per Part 18 §4.4.1.
    /// </para>
    /// </remarks>
    public interface IRoleManager
    {
        /// <summary>
        /// Raised after any successful mutation. Re-entrant: handlers must not
        /// call back into the manager on the same thread.
        /// </summary>
        event EventHandler<RoleConfigurationChangedEventArgs>? RoleConfigurationChanged;

        /// <summary>
        /// NodeIds of every role currently tracked by the manager.
        /// </summary>
        IReadOnlyList<NodeId> RoleIds { get; }

        /// <summary>
        /// Returns an immutable snapshot of the role's configuration, or
        /// <c>null</c> if no role with the supplied NodeId is registered.
        /// </summary>
        RoleEntry? GetRole(NodeId roleId);

        /// <summary>
        /// Adds an identity-mapping rule per Part 18 §4.4.5.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved (<c>Anonymous</c>,
        /// <c>AuthenticatedUser</c>, <c>TrustedApplication</c>);
        /// <c>Bad_InvalidArgument</c> if the rule is malformed;
        /// <c>Bad_AlreadyExists</c> if an equivalent rule already exists.
        /// </returns>
        ServiceResult AddIdentity(NodeId roleId, IdentityMappingRuleType rule);

        /// <summary>
        /// Removes a previously added identity-mapping rule per Part 18 §4.4.6.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved;
        /// <c>Bad_NotFound</c> if the rule is not present.
        /// </returns>
        ServiceResult RemoveIdentity(NodeId roleId, IdentityMappingRuleType rule);

        /// <summary>
        /// Adds an application URI per Part 18 §4.4.7.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved;
        /// <c>Bad_InvalidArgument</c> if the URI is empty;
        /// <c>Bad_AlreadyExists</c> if the URI is already assigned.
        /// </returns>
        ServiceResult AddApplication(NodeId roleId, string applicationUri);

        /// <summary>
        /// Removes a previously added application URI per Part 18 §4.4.8.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved;
        /// <c>Bad_NotFound</c> if the URI is not present.
        /// </returns>
        ServiceResult RemoveApplication(NodeId roleId, string applicationUri);

        /// <summary>
        /// Adds an endpoint per Part 18 §4.4.9.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved;
        /// <c>Bad_InvalidArgument</c> if the endpoint is null or its URL is empty;
        /// <c>Bad_AlreadyExists</c> if an equivalent endpoint already exists.
        /// </returns>
        ServiceResult AddEndpoint(NodeId roleId, EndpointType endpoint);

        /// <summary>
        /// Removes a previously added endpoint per Part 18 §4.4.10.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved;
        /// <c>Bad_NotFound</c> if no equivalent endpoint is present.
        /// </returns>
        ServiceResult RemoveEndpoint(NodeId roleId, EndpointType endpoint);

        /// <summary>
        /// Updates the <c>ApplicationsExclude</c> flag.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved.
        /// </returns>
        ServiceResult SetApplicationsExclude(NodeId roleId, bool value);

        /// <summary>
        /// Updates the <c>EndpointsExclude</c> flag.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved.
        /// </returns>
        ServiceResult SetEndpointsExclude(NodeId roleId, bool value);

        /// <summary>
        /// Updates the <c>CustomConfiguration</c> flag (Part 18 §4.4.1).
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved.
        /// </returns>
        ServiceResult SetCustomConfiguration(NodeId roleId, bool value);

        /// <summary>
        /// Adds a new role per Part 18 §4.2.2. If the
        /// <paramref name="namespaceUri"/> is the OPC UA URI (or empty) and
        /// the <paramref name="roleName"/> matches a well-known role
        /// (Part 3 §4.9.2), the spec-defined well-known NodeId is reused.
        /// Otherwise a fresh NodeId is allocated in <paramref name="defaultNamespaceIndex"/>.
        /// </summary>
        /// <param name="roleName">The browse name for the new role.</param>
        /// <param name="namespaceUri">
        /// The namespace URI qualifying the browse name; if null or empty the
        /// server's default namespace is used.
        /// </param>
        /// <param name="namespaces">Namespace table used to resolve the URI.</param>
        /// <param name="defaultNamespaceIndex">Namespace index used when a fresh NodeId is allocated.</param>
        /// <param name="newRoleId">On success, the new role's NodeId.</param>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_InvalidArgument</c> if <paramref name="roleName"/> is empty;
        /// <c>Bad_AlreadyExists</c> if a role with the same browse name already exists.
        /// </returns>
        ServiceResult AddRole(
            string roleName,
            string? namespaceUri,
            NamespaceTable namespaces,
            ushort defaultNamespaceIndex,
            out NodeId newRoleId);

        /// <summary>
        /// Removes a role per Part 18 §4.2.3.
        /// </summary>
        /// <returns>
        /// <c>Good</c> on success;
        /// <c>Bad_NodeIdUnknown</c> if the role is unknown;
        /// <c>Bad_RequestNotAllowed</c> if the role is reserved
        /// (<c>Anonymous</c>, <c>AuthenticatedUser</c>, <c>TrustedApplication</c>).
        /// </returns>
        ServiceResult RemoveRole(NodeId roleId);

        /// <summary>
        /// Computes the set of roles to grant a session given its
        /// authenticated identity, client certificate, and endpoint per
        /// Part 18 §4.4.1.
        /// </summary>
        /// <param name="identity">The authenticated user identity. Must be non-null.</param>
        /// <param name="clientCertificate">
        /// The client's ApplicationInstance certificate, if a secure channel
        /// was negotiated. Used to evaluate <c>Application</c>,
        /// <c>TrustedApplication</c>, <c>X509Subject</c> and the
        /// <c>Applications</c> filter.
        /// </param>
        /// <param name="endpoint">
        /// The endpoint used by the session. Used to evaluate the
        /// <c>Endpoints</c> filter and the "at least signed channel"
        /// requirement for <c>Application</c> / <c>TrustedApplication</c> rules.
        /// </param>
        /// <returns>The set of granted role NodeIds.</returns>
        IList<NodeId> ResolveGrantedRoles(
            IUserIdentity identity,
            Certificate? clientCertificate,
            EndpointDescription? endpoint);
    }
}
