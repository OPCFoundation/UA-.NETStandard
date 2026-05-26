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
using System.Linq;
using System.Threading;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// In-memory default implementation of <see cref="IRoleManager"/>.
    /// Pre-populates the nine well-known roles per OPC UA Part 3 §4.9.2 and
    /// their default identities per Part 18 §4.3. All operations are
    /// thread-safe.
    /// </summary>
    /// <remarks>
    /// Per Part 18 §6.4 "the management of these Roles is server-specific" —
    /// this default keeps everything in memory and does not persist across
    /// server restarts. Integrators that need persistence should implement
    /// <see cref="IRoleManager"/> directly and inject the instance via
    /// <see cref="IServerInternal.SetRoleManager"/>.
    /// </remarks>
    public sealed class RoleManager : IRoleManager, IDisposable
    {
        private static readonly NodeId s_anonymous
            = ObjectIds.WellKnownRole_Anonymous;

        private static readonly NodeId s_authenticatedUser
            = ObjectIds.WellKnownRole_AuthenticatedUser;

        private static readonly NodeId s_trustedApplication
            = ObjectIds.WellKnownRole_TrustedApplication;

        private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
        private readonly Dictionary<NodeId, MutableRole> m_roles = [];

        private readonly Dictionary<string, NodeId> m_browseNameIndex
            = new(StringComparer.Ordinal);

        private readonly RoleConfigurationOptions m_options;

        private uint m_nextDynamicId = 1;
        private bool m_disposed;

        /// <summary>
        /// Creates a new role manager pre-populated with the nine well-known
        /// roles per Part 3 §4.9.2 and the default identity rules mandated by
        /// Part 18 §4.3.
        /// </summary>
        public RoleManager()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a new role manager with the supplied role-mapping options.
        /// </summary>
        /// <param name="options">Role criteria compatibility options. If
        /// <c>null</c>, the corrected default behaviour is used.</param>
        public RoleManager(RoleConfigurationOptions? options)
        {
            m_options = options ?? new RoleConfigurationOptions();

            // Anonymous role: identities = { Anonymous, AuthenticatedUser }
            AddBuiltInRole(s_anonymous, BrowseNames.WellKnownRole_Anonymous, isReserved: true)
                .Identities.Add(new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.Anonymous
                });
            m_roles[s_anonymous].Identities.Add(new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.AuthenticatedUser
            });

            // AuthenticatedUser role: identities = { AuthenticatedUser }
            AddBuiltInRole(s_authenticatedUser, BrowseNames.WellKnownRole_AuthenticatedUser, isReserved: true)
                .Identities.Add(new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.AuthenticatedUser
                });

            // TrustedApplication role: identities = { TrustedApplication }
            AddBuiltInRole(s_trustedApplication, BrowseNames.WellKnownRole_TrustedApplication, isReserved: true)
                .Identities.Add(new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.TrustedApplication
                });

            // Configurable well-known roles (no default identities).
            AddBuiltInRole(ObjectIds.WellKnownRole_Observer,
                BrowseNames.WellKnownRole_Observer, isReserved: false);
            AddBuiltInRole(ObjectIds.WellKnownRole_Operator,
                BrowseNames.WellKnownRole_Operator, isReserved: false);
            AddBuiltInRole(ObjectIds.WellKnownRole_Engineer,
                BrowseNames.WellKnownRole_Engineer, isReserved: false);
            AddBuiltInRole(ObjectIds.WellKnownRole_Supervisor,
                BrowseNames.WellKnownRole_Supervisor, isReserved: false);
            AddBuiltInRole(ObjectIds.WellKnownRole_ConfigureAdmin,
                BrowseNames.WellKnownRole_ConfigureAdmin, isReserved: false);
            AddBuiltInRole(ObjectIds.WellKnownRole_SecurityAdmin,
                BrowseNames.WellKnownRole_SecurityAdmin, isReserved: false);
        }

        /// <inheritdoc/>
        public event EventHandler<RoleConfigurationChangedEventArgs>? RoleConfigurationChanged;

        /// <inheritdoc/>
        public IReadOnlyList<NodeId> RoleIds
        {
            get
            {
                m_lock.EnterReadLock();
                try
                {
                    return [.. m_roles.Keys];
                }
                finally
                {
                    m_lock.ExitReadLock();
                }
            }
        }

        /// <inheritdoc/>
        public RoleEntry? GetRole(NodeId roleId)
        {
            if (roleId.IsNull)
            {
                return null;
            }

            m_lock.EnterReadLock();
            try
            {
                return m_roles.TryGetValue(roleId, out MutableRole? role)
                    ? role.Snapshot()
                    : null;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public ServiceResult AddIdentity(NodeId roleId, IdentityMappingRuleType rule)
        {
            ServiceResult validation = IdentityRuleValidator.Validate(rule);
            if (ServiceResult.IsBad(validation))
            {
                return validation;
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                if (role!.Identities.Any(r => IdentityRuleValidator.AreEquivalent(r, rule)))
                {
                    return new ServiceResult(StatusCodes.BadAlreadyExists,
                        new LocalizedText("An equivalent identity rule already exists."));
                }
                role.Identities.Add(Clone(rule));
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.IdentityAdded);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult RemoveIdentity(NodeId roleId, IdentityMappingRuleType rule)
        {
            if (rule == null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                int idx = role!.Identities.FindIndex(r => IdentityRuleValidator.AreEquivalent(r, rule));
                if (idx < 0)
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                role.Identities.RemoveAt(idx);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.IdentityRemoved);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult AddApplication(NodeId roleId, string applicationUri)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("ApplicationUri must be non-empty."));
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                if (role!.Applications.Contains(applicationUri))
                {
                    return new ServiceResult(StatusCodes.BadAlreadyExists,
                        new LocalizedText($"ApplicationUri '{applicationUri}' is already assigned to this role."));
                }
                role.Applications.Add(applicationUri);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.ApplicationAdded);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult RemoveApplication(NodeId roleId, string applicationUri)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                if (!role!.Applications.Remove(applicationUri))
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.ApplicationRemoved);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult AddEndpoint(NodeId roleId, EndpointType endpoint)
        {
            if (endpoint == null || string.IsNullOrEmpty(endpoint.EndpointUrl))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("Endpoint or its EndpointUrl must be non-empty."));
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                if (role!.Endpoints.Any(e => EndpointTypeComparer.RulesEqual(e, endpoint)))
                {
                    return new ServiceResult(StatusCodes.BadAlreadyExists,
                        new LocalizedText("An equivalent endpoint is already assigned to this role."));
                }
                role.Endpoints.Add(EndpointTypeComparer.Clone(endpoint));
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.EndpointAdded);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult RemoveEndpoint(NodeId roleId, EndpointType endpoint)
        {
            if (endpoint == null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }

                int idx = role!.Endpoints.FindIndex(e => EndpointTypeComparer.RulesEqual(e, endpoint));
                if (idx < 0)
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                role.Endpoints.RemoveAt(idx);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.EndpointRemoved);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult SetApplicationsExclude(NodeId roleId, bool value)
        {
            return SetFlag(roleId, value, role => role.ApplicationsExclude,
                (role, v) => role.ApplicationsExclude = v,
                RoleConfigurationChangeKind.ApplicationsExcludeChanged);
        }

        /// <inheritdoc/>
        public ServiceResult SetEndpointsExclude(NodeId roleId, bool value)
        {
            return SetFlag(roleId, value, role => role.EndpointsExclude,
                (role, v) => role.EndpointsExclude = v,
                RoleConfigurationChangeKind.EndpointsExcludeChanged);
        }

        /// <inheritdoc/>
        public ServiceResult SetCustomConfiguration(NodeId roleId, bool value)
        {
            return SetFlag(roleId, value, role => role.CustomConfiguration,
                (role, v) => role.CustomConfiguration = v,
                RoleConfigurationChangeKind.CustomConfigurationChanged);
        }

        private ServiceResult SetFlag(
            NodeId roleId,
            bool value,
            Func<MutableRole, bool> getter,
            Action<MutableRole, bool> setter,
            RoleConfigurationChangeKind kind)
        {
            bool changed;
            m_lock.EnterWriteLock();
            try
            {
                ServiceResult lookup = TryGetMutableRole(roleId, requireMutable: true, out MutableRole? role);
                if (ServiceResult.IsBad(lookup))
                {
                    return lookup;
                }
                changed = getter(role!) != value;
                setter(role!, value);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            if (changed)
            {
                RaiseChanged(roleId, kind);
            }
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult AddRole(
            string roleName,
            string? namespaceUri,
            NamespaceTable namespaces,
            ushort defaultNamespaceIndex,
            out NodeId newRoleId)
        {
            newRoleId = NodeId.Null;

            if (string.IsNullOrEmpty(roleName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("RoleName must be non-empty."));
            }
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }

            bool isWellKnown = false;
            bool useOpcUaNamespace =
                string.IsNullOrEmpty(namespaceUri) ||
                string.Equals(namespaceUri, Ua.Namespaces.OpcUa, StringComparison.Ordinal);

            // If naming a well-known role under the OPC UA namespace, reuse
            // the well-known NodeId per Part 18 §4.2.2.
            NodeId? candidate = null;
            ushort namespaceIndex = defaultNamespaceIndex;
            if (useOpcUaNamespace)
            {
                candidate = ResolveWellKnownNodeId(roleName);
                if (candidate != null)
                {
                    isWellKnown = true;
                    namespaceIndex = 0;
                }
            }

            if (candidate == null)
            {
                if (!useOpcUaNamespace)
                {
                    int idx = namespaces.GetIndex(namespaceUri!);
                    if (idx < 0)
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument,
                            new LocalizedText($"Namespace URI '{namespaceUri}' is not registered."));
                    }
                    namespaceIndex = (ushort)idx;
                }
                else
                {
                    // The caller asked for the default namespace (either no
                    // URI provided or the bare OPC UA URI) but the role name
                    // is not one of the reserved Part 3 §4.9 names. Allocate
                    // in the manager's dynamic namespace rather than ns=0,
                    // which is reserved for OPC UA core nodes per Part 5.
                    namespaceIndex = defaultNamespaceIndex;
                }
            }

            m_lock.EnterWriteLock();
            try
            {
                if (m_browseNameIndex.ContainsKey(roleName))
                {
                    return new ServiceResult(StatusCodes.BadAlreadyExists,
                        new LocalizedText($"A role with browse name '{roleName}' already exists."));
                }

                NodeId allocated;
                if (candidate != null)
                {
                    if (m_roles.ContainsKey(candidate.Value))
                    {
                        return new ServiceResult(StatusCodes.BadAlreadyExists,
                            new LocalizedText($"Well-known role '{roleName}' is already registered."));
                    }
                    allocated = candidate.Value;
                }
                else
                {
                    allocated = AllocateDynamicNodeId(namespaceIndex);
                }

                // Per §4.2.2: initial values of ApplicationsExclude/EndpointsExclude
                // shall be TRUE on newly created roles when the properties exist.
                m_roles[allocated] = new MutableRole(allocated, roleName, namespaceIndex,
                    isReserved: false, isWellKnown: isWellKnown)
                {
                    ApplicationsExclude = true,
                    EndpointsExclude = true
                };
                m_browseNameIndex[roleName] = allocated;
                newRoleId = allocated;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(newRoleId, RoleConfigurationChangeKind.RoleAdded);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ServiceResult RemoveRole(NodeId roleId)
        {
            if (roleId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!m_roles.TryGetValue(roleId, out MutableRole? role))
                {
                    return new ServiceResult(StatusCodes.BadNodeIdUnknown);
                }
                if (role.IsReserved)
                {
                    return new ServiceResult(StatusCodes.BadRequestNotAllowed,
                        new LocalizedText($"Role '{role.BrowseName}' cannot be removed (Part 18 §4.3)."));
                }
                m_roles.Remove(roleId);
                if (role.BrowseName != null)
                {
                    m_browseNameIndex.Remove(role.BrowseName);
                }
            }
            finally
            {
                m_lock.ExitWriteLock();
            }

            RaiseChanged(roleId, RoleConfigurationChangeKind.RoleRemoved);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public IList<NodeId> ResolveGrantedRoles(
            IUserIdentity identity,
            Certificate? clientCertificate,
            EndpointDescription? endpoint)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            IReadOnlyList<string> clientApplicationUris = clientCertificate != null
                ? X509Utils.GetApplicationUrisFromCertificate(clientCertificate)
                : [];
            string clientThumbprint = clientCertificate != null
                ? IdentityRuleValidator.NormaliseThumbprint(clientCertificate.Thumbprint)
                : string.Empty;
            string clientSubject = clientCertificate != null
                ? IdentityRuleValidator.NormaliseX509Subject(clientCertificate.Subject)
                : string.Empty;

            string endpointUrl = endpoint?.EndpointUrl ?? string.Empty;
            bool isSignedChannel = endpoint != null &&
                endpoint.SecurityMode is MessageSecurityMode.Sign
                    or MessageSecurityMode.SignAndEncrypt;
            bool isEncryptedChannel = endpoint?.SecurityMode == MessageSecurityMode.SignAndEncrypt;

            var candidate = new EndpointType
            {
                EndpointUrl = endpointUrl,
                SecurityMode = endpoint?.SecurityMode ?? MessageSecurityMode.Invalid,
                SecurityPolicyUri = endpoint?.SecurityPolicyUri,
                TransportProfileUri = endpoint?.TransportProfileUri
            };

            var granted = new List<NodeId>();

            m_lock.EnterReadLock();
            try
            {
                foreach (MutableRole role in m_roles.Values)
                {
                    if (RoleMatches(role, identity, clientCertificate, clientApplicationUris,
                            clientThumbprint, clientSubject, candidate, isSignedChannel,
                            isEncryptedChannel, granted))
                    {
                        granted.Add(role.RoleId);
                    }
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            return granted;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_lock.Dispose();
        }

        private bool RoleMatches(
            MutableRole role,
            IUserIdentity identity,
            Certificate? clientCertificate,
            IReadOnlyList<string> clientApplicationUris,
            string clientThumbprint,
            string clientSubject,
            EndpointType candidateEndpoint,
            bool isSignedChannel,
            bool isEncryptedChannel,
            IReadOnlyList<NodeId> rolesGrantedSoFar)
        {
            // Apply Application filter (Part 18 §4.4.1).
            if (role.Applications.Count > 0)
            {
                // "If Applications has entries in the array, the Role shall only
                // be granted if the Session uses a signed or signed and encrypted
                // communication channel." — §4.4.1
                if (!isSignedChannel || clientApplicationUris.Count == 0)
                {
                    if (!role.ApplicationsExclude)
                    {
                        return false;
                    }
                }
                // The certificate may advertise multiple ApplicationUris (Subject
                // Alternative Names); any match against the role's Applications list
                // counts as inclusion.
                bool inList = clientApplicationUris.Any(role.Applications.Contains);
                if (role.ApplicationsExclude ? inList : !inList)
                {
                    return false;
                }
            }

            // Apply Endpoint filter (Part 18 §4.4.1, §4.4.2).
            if (role.Endpoints.Count > 0)
            {
                bool matchesEndpoint = role.Endpoints.Any(
                    e => EndpointTypeComparer.Matches(e, candidateEndpoint));
                if (role.EndpointsExclude ? matchesEndpoint : !matchesEndpoint)
                {
                    return false;
                }
            }

            // Evaluate identity rules.
            if (role.Identities.Count == 0)
            {
                // Per §4.4.1: "If this Property is an empty array and
                // CustomConfiguration is not TRUE, then the Role cannot be
                // granted to any Session."
                return role.CustomConfiguration;
            }

            foreach (IdentityMappingRuleType rule in role.Identities)
            {
                if (IdentityRuleMatches(rule, identity, clientCertificate,
                        clientApplicationUris, clientThumbprint, clientSubject,
                        isSignedChannel, isEncryptedChannel, rolesGrantedSoFar))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IdentityRuleMatches(
            IdentityMappingRuleType rule,
            IUserIdentity identity,
            Certificate? clientCertificate,
            IReadOnlyList<string> clientApplicationUris,
            string clientThumbprint,
            string clientSubject,
            bool isSignedChannel,
            bool isEncryptedChannel,
            IReadOnlyList<NodeId> rolesGrantedSoFar)
        {
            UserTokenType tokenType = identity.TokenType;
            string criteria = rule.Criteria ?? string.Empty;
            IIdentityClaims? claims = identity as IIdentityClaims;

            return rule.CriteriaType switch
            {
                IdentityCriteriaType.Anonymous => tokenType == UserTokenType.Anonymous,
                IdentityCriteriaType.AuthenticatedUser => tokenType != UserTokenType.Anonymous,
                IdentityCriteriaType.UserName => tokenType == UserTokenType.UserName &&
                    string.Equals(identity.DisplayName, criteria, StringComparison.Ordinal),
                IdentityCriteriaType.Thumbprint => clientCertificate != null &&
                    string.Equals(clientThumbprint, criteria, StringComparison.Ordinal),
                IdentityCriteriaType.X509Subject => clientCertificate != null &&
                    !string.IsNullOrEmpty(clientSubject) &&
                    string.Equals(clientSubject, criteria, StringComparison.Ordinal),
                IdentityCriteriaType.Role => m_options.LegacyRoleCriteriaMatchesGrantedRoles
                    ? MatchesGrantedRole(criteria, rolesGrantedSoFar)
                    : claims != null && MatchClaimRole(claims, criteria),
                // The certificate may advertise multiple ApplicationUris; any one
                // matching the rule's criteria is sufficient.
                IdentityCriteriaType.Application => clientCertificate != null &&
                    isSignedChannel &&
                    clientApplicationUris.Any(uri => string.Equals(uri, criteria, StringComparison.Ordinal)),
                IdentityCriteriaType.TrustedApplication => clientCertificate != null &&
                    isSignedChannel,
                IdentityCriteriaType.GroupId => claims != null &&
                    claims.Groups.Contains(criteria, StringComparer.Ordinal),
                _ => false
            };
        }

        /// <summary>
        /// Matches a Part 18 §4.4.4 Role criterion against roles asserted in
        /// an access token, including the optional <c>iss/roleName</c> prefix.
        /// </summary>
        private static bool MatchClaimRole(IIdentityClaims claims, string criteria)
        {
            if (string.IsNullOrEmpty(criteria))
            {
                return false;
            }

            int separator = criteria.LastIndexOf('/');
            if (separator < 0)
            {
                return claims.Roles.Contains(criteria, StringComparer.Ordinal);
            }

            string issuer = criteria.Substring(0, separator);
            string roleName = criteria.Substring(separator + 1);
            return !string.IsNullOrEmpty(issuer) &&
                !string.IsNullOrEmpty(roleName) &&
                string.Equals(claims.Issuer, issuer, StringComparison.Ordinal) &&
                claims.Roles.Contains(roleName, StringComparer.Ordinal);
        }

        private static bool MatchesGrantedRole(string criteria, IReadOnlyList<NodeId> rolesGrantedSoFar)
        {
            if (string.IsNullOrEmpty(criteria))
            {
                return false;
            }
            foreach (NodeId nodeId in rolesGrantedSoFar)
            {
                if (string.Equals(nodeId.ToString(), criteria, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private MutableRole AddBuiltInRole(NodeId roleId, string browseName, bool isReserved)
        {
            var role = new MutableRole(roleId, browseName, roleId.NamespaceIndex,
                isReserved: isReserved, isWellKnown: true);
            m_roles[roleId] = role;
            m_browseNameIndex[browseName] = roleId;
            return role;
        }

        private NodeId AllocateDynamicNodeId(ushort namespaceIndex)
        {
            return new NodeId(m_nextDynamicId++, namespaceIndex);
        }

        private ServiceResult TryGetMutableRole(NodeId roleId, bool requireMutable, out MutableRole? role)
        {
            role = null;
            if (roleId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (!m_roles.TryGetValue(roleId, out role))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            if (requireMutable && role.IsReserved)
            {
                return new ServiceResult(StatusCodes.BadRequestNotAllowed,
                    new LocalizedText(
                        $"Role '{role.BrowseName}' is reserved and cannot be modified (Part 18 §4.3)."));
            }
            return ServiceResult.Good;
        }

        private void RaiseChanged(NodeId roleId, RoleConfigurationChangeKind kind)
        {
            RoleConfigurationChanged?.Invoke(this, new RoleConfigurationChangedEventArgs(roleId, kind));
        }

        private static IdentityMappingRuleType Clone(IdentityMappingRuleType rule)
        {
            return new IdentityMappingRuleType
            {
                CriteriaType = rule.CriteriaType,
                Criteria = rule.Criteria
            };
        }

        private static NodeId? ResolveWellKnownNodeId(string roleName)
        {
            return roleName switch
            {
                BrowseNames.WellKnownRole_Anonymous
                    => ObjectIds.WellKnownRole_Anonymous,
                BrowseNames.WellKnownRole_AuthenticatedUser
                    => ObjectIds.WellKnownRole_AuthenticatedUser,
                BrowseNames.WellKnownRole_TrustedApplication
                    => ObjectIds.WellKnownRole_TrustedApplication,
                BrowseNames.WellKnownRole_Observer
                    => ObjectIds.WellKnownRole_Observer,
                BrowseNames.WellKnownRole_Operator
                    => ObjectIds.WellKnownRole_Operator,
                BrowseNames.WellKnownRole_Engineer
                    => ObjectIds.WellKnownRole_Engineer,
                BrowseNames.WellKnownRole_Supervisor
                    => ObjectIds.WellKnownRole_Supervisor,
                BrowseNames.WellKnownRole_ConfigureAdmin
                    => ObjectIds.WellKnownRole_ConfigureAdmin,
                BrowseNames.WellKnownRole_SecurityAdmin
                    => ObjectIds.WellKnownRole_SecurityAdmin,
                _ => null
            };
        }

        /// <summary>
        /// Mutable per-role state held inside the manager. Not exposed
        /// outside this class; callers see immutable <see cref="RoleEntry"/>
        /// snapshots.
        /// </summary>
        private sealed class MutableRole
        {
            public MutableRole(NodeId roleId, string browseName, ushort namespaceIndex,
                bool isReserved, bool isWellKnown)
            {
                RoleId = roleId;
                BrowseName = browseName;
                NamespaceIndex = namespaceIndex;
                IsReserved = isReserved;
                IsWellKnown = isWellKnown;
                Identities = [];
                Applications = [];
                Endpoints = [];
            }

            public NodeId RoleId { get; }
            public string BrowseName { get; }
            public ushort NamespaceIndex { get; }
            public bool IsReserved { get; }
            public bool IsWellKnown { get; }
            public List<IdentityMappingRuleType> Identities { get; }
            public List<string> Applications { get; }
            public List<EndpointType> Endpoints { get; }
            public bool ApplicationsExclude { get; set; }
            public bool EndpointsExclude { get; set; }
            public bool CustomConfiguration { get; set; }

            public RoleEntry Snapshot()
            {
                return new RoleEntry(
                    RoleId,
                    BrowseName,
                    NamespaceIndex,
                    IsReserved,
                    IsWellKnown,
                    [.. Identities.Select(Clone)],
                    [.. Applications],
                    ApplicationsExclude,
                    [.. Endpoints.Select(EndpointTypeComparer.Clone)],
                    EndpointsExclude,
                    CustomConfiguration);
            }
        }
    }
}
