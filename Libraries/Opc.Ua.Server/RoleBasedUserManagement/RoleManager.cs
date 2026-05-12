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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Per-role mutable state plus identity-mapping algorithm per OPC UA Part 18 §4.4
    /// (RoleType) and §6.4 (RoleSetType). Each well-known role in the address space
    /// gets a backing <see cref="RoleEntry"/> here; the RoleStateBinding (in this
    /// namespace) surfaces method calls and variable reads to/from this manager.
    /// </summary>
    /// <remarks>
    /// All state is in-memory; rules added at runtime do not persist across server
    /// restart. This is spec-allowed (Part 18 §6.4: "the management of these Roles
    /// is server-specific"). Integrators that need persistence should implement
    /// <see cref="IRoleManager"/> directly and inject the instance via
    /// <see cref="IServerInternal.SetRoleManager"/>.
    /// </remarks>
    public sealed class RoleManager : IRoleManager
    {
        private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
        private readonly Dictionary<NodeId, RoleEntry> m_roles
            = new(EqualityComparer<NodeId>.Default);
        private readonly HashSet<NodeId> m_dynamicRoles
            = new(EqualityComparer<NodeId>.Default);
        private uint m_nextDynamicRoleId = 1;

        /// <summary>
        /// Namespace index used to issue NodeIds for dynamically created roles.
        /// Initialized by <see cref="RoleStateBinding.Bind"/> from the diagnostics
        /// node manager's namespace. Defaults to 0 if not initialized.
        /// </summary>
        public ushort DynamicRoleNamespaceIndex { get; set; }

        /// <summary>
        /// Creates a new role manager with empty per-role state. Call
        /// <see cref="EnsureRole"/> for each role you intend to manage at startup.
        /// </summary>
        public RoleManager()
        {
        }

        /// <summary>
        /// Ensures a <see cref="RoleEntry"/> exists for <paramref name="roleId"/>.
        /// Idempotent.
        /// </summary>
        public RoleEntry EnsureRole(NodeId roleId)
        {
            if (roleId.IsNull) { throw new ArgumentException("roleId cannot be null.", nameof(roleId)); }

            m_lock.EnterWriteLock();
            try
            {
                if (!m_roles.TryGetValue(roleId, out RoleEntry entry))
                {
                    entry = new RoleEntry(roleId);
                    m_roles[roleId] = entry;
                }
                return entry;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Registered role NodeIds.
        /// </summary>
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

        /// <summary>
        /// Adds an identity-mapping rule to the role. Idempotent — duplicate rules
        /// are silently dropped.
        /// </summary>
        public ServiceResult AddIdentity(NodeId roleId, IdentityMappingRuleType rule)
        {
            if (rule == null) { throw new ArgumentNullException(nameof(rule)); }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!entry.Identities.Any(r => RuleEquals(r, rule)))
                {
                    entry.Identities.Add(Clone(rule));
                }
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a previously added identity-mapping rule. Returns
        /// BadNotFound if the rule isn't present.
        /// </summary>
        public ServiceResult RemoveIdentity(NodeId roleId, IdentityMappingRuleType rule)
        {
            if (rule == null) { throw new ArgumentNullException(nameof(rule)); }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                int idx = entry.Identities.FindIndex(r => RuleEquals(r, rule));
                if (idx < 0)
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                entry.Identities.RemoveAt(idx);
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds an application URI to the role's <see cref="RoleEntry.Applications"/> list.
        /// </summary>
        public ServiceResult AddApplication(NodeId roleId, string applicationUri)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!entry.Applications.Contains(applicationUri))
                {
                    entry.Applications.Add(applicationUri);
                }
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes an application URI; returns BadNotFound if absent.
        /// </summary>
        public ServiceResult RemoveApplication(NodeId roleId, string applicationUri)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!entry.Applications.Remove(applicationUri))
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds an endpoint description to the role's <see cref="RoleEntry.Endpoints"/> list.
        /// </summary>
        public ServiceResult AddEndpoint(NodeId roleId, EndpointType endpoint)
        {
            if (endpoint == null) { throw new ArgumentNullException(nameof(endpoint)); }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                if (!entry.Endpoints.Any(e => EndpointEquals(e, endpoint)))
                {
                    entry.Endpoints.Add(CloneEndpoint(endpoint));
                }
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a previously added endpoint; returns BadNotFound if absent.
        /// </summary>
        public ServiceResult RemoveEndpoint(NodeId roleId, EndpointType endpoint)
        {
            if (endpoint == null) { throw new ArgumentNullException(nameof(endpoint)); }
            RoleEntry entry = GetEntryOrFail(roleId, out ServiceResult error);
            if (entry == null)
            {
                return error;
            }

            m_lock.EnterWriteLock();
            try
            {
                int idx = entry.Endpoints.FindIndex(e => EndpointEquals(e, endpoint));
                if (idx < 0)
                {
                    return new ServiceResult(StatusCodes.BadNotFound);
                }
                entry.Endpoints.RemoveAt(idx);
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Read-only snapshot of the role's identities. Used by Variable read handlers.
        /// </summary>
        public IList<IdentityMappingRuleType> SnapshotIdentities(NodeId roleId)
        {
            m_lock.EnterReadLock();
            try
            {
                return m_roles.TryGetValue(roleId, out RoleEntry entry)
                    ? entry.Identities.ConvertAll(Clone)
                    : [];
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Read-only snapshot of the role's application URIs.
        /// </summary>
        public IList<string> SnapshotApplications(NodeId roleId, out bool exclude)
        {
            m_lock.EnterReadLock();
            try
            {
                if (!m_roles.TryGetValue(roleId, out RoleEntry entry))
                {
                    exclude = false;
                    return [];
                }
                exclude = entry.ApplicationsExclude;
                return [.. entry.Applications];
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Read-only snapshot of the role's endpoints.
        /// </summary>
        public IList<EndpointType> SnapshotEndpoints(NodeId roleId, out bool exclude)
        {
            m_lock.EnterReadLock();
            try
            {
                if (!m_roles.TryGetValue(roleId, out RoleEntry entry))
                {
                    exclude = false;
                    return [];
                }
                exclude = entry.EndpointsExclude;
                return entry.Endpoints.ConvertAll(CloneEndpoint);
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Sets the ApplicationsExclude flag (true = role is granted to apps NOT in the list).
        /// </summary>
        public void SetApplicationsExclude(NodeId roleId, bool exclude)
        {
            RoleEntry entry = EnsureRole(roleId);
            m_lock.EnterWriteLock();
            try
            {
                entry.ApplicationsExclude = exclude;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sets the EndpointsExclude flag (true = role is granted on endpoints NOT in the list).
        /// </summary>
        public void SetEndpointsExclude(NodeId roleId, bool exclude)
        {
            RoleEntry entry = EnsureRole(roleId);
            m_lock.EnterWriteLock();
            try
            {
                entry.EndpointsExclude = exclude;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Computes the set of additional roles to grant a session given its identity,
        /// client cert, and endpoint per Part 18 §4.4.4.
        /// </summary>
        /// <remarks>
        /// Returns role NodeIds. Caller should layer these on top of any roles already
        /// granted (e.g. anonymous gets <see cref="Role.Anonymous"/> by default).
        /// </remarks>
        public IList<NodeId> ResolveGrantedRoles(
            IUserIdentity identity,
            Certificate clientCertificate,
            EndpointDescription endpoint)
        {
            if (identity == null) { throw new ArgumentNullException(nameof(identity)); }

            string clientApplicationUri = clientCertificate != null
                ? X509Utils.GetApplicationUrisFromCertificate(clientCertificate).FirstOrDefault()
                : null;
            string endpointUrl = endpoint?.EndpointUrl;

            var granted = new List<NodeId>();

            m_lock.EnterReadLock();
            try
            {
                foreach (RoleEntry entry in m_roles.Values)
                {
                    if (RoleMatches(entry, identity, clientCertificate, clientApplicationUri, endpointUrl, granted))
                    {
                        granted.Add(entry.RoleId);
                    }
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            return granted;
        }

        private bool RoleMatches(
            RoleEntry entry,
            IUserIdentity identity,
            Certificate clientCertificate,
            string clientApplicationUri,
            string endpointUrl,
            IReadOnlyList<NodeId> rolesGrantedSoFar)
        {
            // Application filter (per Part 18 §6.4: a role applies to an application iff
            // the URI is listed (Exclude=false) or NOT listed (Exclude=true)).
            if (entry.Applications.Count > 0 && clientApplicationUri != null)
            {
                bool inList = entry.Applications.Contains(clientApplicationUri);
                if (entry.ApplicationsExclude ? inList : !inList)
                {
                    return false;
                }
            }

            // Endpoint filter — same Exclude semantics.
            if (entry.Endpoints.Count > 0 && endpointUrl != null)
            {
                bool inList = entry.Endpoints.Any(e =>
                    string.Equals(e.EndpointUrl, endpointUrl, StringComparison.Ordinal));
                if (entry.EndpointsExclude ? inList : !inList)
                {
                    return false;
                }
            }

            foreach (IdentityMappingRuleType rule in entry.Identities)
            {
                if (IdentityRuleMatches(rule, identity, clientCertificate, rolesGrantedSoFar))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IdentityRuleMatches(
            IdentityMappingRuleType rule,
            IUserIdentity identity,
            Certificate clientCertificate,
            IReadOnlyList<NodeId> rolesGrantedSoFar)
        {
            UserTokenType tokenType = identity.TokenType;
            return rule.CriteriaType switch
            {
                IdentityCriteriaType.Anonymous => tokenType == UserTokenType.Anonymous,
                IdentityCriteriaType.AuthenticatedUser => tokenType != UserTokenType.Anonymous,
                IdentityCriteriaType.UserName => tokenType == UserTokenType.UserName
                    && string.Equals(identity.DisplayName, rule.Criteria, StringComparison.Ordinal),
                IdentityCriteriaType.Thumbprint => clientCertificate != null
                    && string.Equals(clientCertificate.Thumbprint, rule.Criteria, StringComparison.OrdinalIgnoreCase),
                IdentityCriteriaType.X509Subject => clientCertificate != null
                    && clientCertificate.Subject != null
                    && clientCertificate.Subject.Contains(rule.Criteria ?? string.Empty, StringComparison.Ordinal),
                IdentityCriteriaType.Role => rolesGrantedSoFar.Any(r => string.Equals(r.ToString(), rule.Criteria, StringComparison.Ordinal)),
                IdentityCriteriaType.Application => clientCertificate != null
                    && string.Equals(
                        X509Utils.GetApplicationUrisFromCertificate(clientCertificate).FirstOrDefault(),
                        rule.Criteria,
                        StringComparison.Ordinal),
                IdentityCriteriaType.TrustedApplication => clientCertificate != null,
                // GroupId: out-of-scope without an external group provider.
                IdentityCriteriaType.GroupId => false,
                _ => false
            };
        }

        private RoleEntry GetEntryOrFail(NodeId roleId, out ServiceResult error)
        {
            if (roleId.IsNull) { throw new ArgumentException("roleId cannot be null.", nameof(roleId)); }
            m_lock.EnterReadLock();
            try
            {
                if (m_roles.TryGetValue(roleId, out RoleEntry entry))
                {
                    error = ServiceResult.Good;
                    return entry;
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }
            error = new ServiceResult(StatusCodes.BadNotFound,
                new LocalizedText($"Role {roleId} is not registered."));
            return null;
        }

        private static bool RuleEquals(IdentityMappingRuleType a, IdentityMappingRuleType b)
        {
            return a.CriteriaType == b.CriteriaType
                && string.Equals(a.Criteria ?? string.Empty, b.Criteria ?? string.Empty, StringComparison.Ordinal);
        }

        private static IdentityMappingRuleType Clone(IdentityMappingRuleType rule)
        {
            return new IdentityMappingRuleType
            {
                CriteriaType = rule.CriteriaType,
                Criteria = rule.Criteria
            };
        }

        private static bool EndpointEquals(EndpointType a, EndpointType b)
        {
            return string.Equals(a.EndpointUrl ?? string.Empty, b.EndpointUrl ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(a.SecurityPolicyUri ?? string.Empty, b.SecurityPolicyUri ?? string.Empty, StringComparison.Ordinal)
                && a.SecurityMode == b.SecurityMode
                && string.Equals(a.TransportProfileUri ?? string.Empty, b.TransportProfileUri ?? string.Empty, StringComparison.Ordinal);
        }

        private static EndpointType CloneEndpoint(EndpointType e)
        {
            return new EndpointType
            {
                EndpointUrl = e.EndpointUrl,
                SecurityMode = e.SecurityMode,
                SecurityPolicyUri = e.SecurityPolicyUri,
                TransportProfileUri = e.TransportProfileUri
            };
        }

        /// <summary>
        /// Dynamically creates a new role and returns its NodeId. The role is
        /// tracked in memory only — no node is materialized in the address space.
        /// Callers that need address-space integration must add the corresponding
        /// <c>RoleType</c> instance separately.
        /// </summary>
        /// <param name="roleName">Browse name of the new role (must be non-empty).</param>
        /// <param name="namespaceUri">Namespace URI for the role NodeId. Currently
        /// reserved — the NodeId is always issued in the diagnostics namespace.
        /// </param>
        /// <param name="newRoleId">On success, the NodeId of the new role.</param>
        public ServiceResult AddRole(string roleName, string namespaceUri, out NodeId newRoleId)
        {
            newRoleId = NodeId.Null;
            if (string.IsNullOrEmpty(roleName))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("RoleName must be non-empty."));
            }

            m_lock.EnterWriteLock();
            try
            {
                // Reject duplicates by browse-name match against existing dynamic roles.
                foreach (RoleEntry existing in m_roles.Values)
                {
                    if (string.Equals(existing.BrowseName, roleName, StringComparison.Ordinal))
                    {
                        return new ServiceResult(StatusCodes.BadBrowseNameDuplicated,
                            new LocalizedText($"Role with name {roleName} already exists."));
                    }
                }

                uint id = m_nextDynamicRoleId++;
                newRoleId = new NodeId(id, DynamicRoleNamespaceIndex);
                var entry = new RoleEntry(newRoleId)
                {
                    BrowseName = roleName,
                    NamespaceUri = namespaceUri
                };
                m_roles[newRoleId] = entry;
                m_dynamicRoles.Add(newRoleId);
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a dynamically created role. Returns
        /// <see cref="StatusCodes.BadNotFound"/> if the role is unknown and
        /// <see cref="StatusCodes.BadInvalidState"/> if the role is well-known
        /// (well-known roles cannot be removed per Part 18 §6.4).
        /// </summary>
        public ServiceResult RemoveRole(NodeId roleId)
        {
            if (roleId.IsNull) { throw new ArgumentException("roleId cannot be null.", nameof(roleId)); }

            m_lock.EnterWriteLock();
            try
            {
                if (!m_roles.ContainsKey(roleId))
                {
                    return new ServiceResult(StatusCodes.BadNotFound,
                        new LocalizedText($"Role {roleId} is not registered."));
                }
                if (!m_dynamicRoles.Contains(roleId))
                {
                    return new ServiceResult(StatusCodes.BadInvalidState,
                        new LocalizedText($"Role {roleId} is well-known and cannot be removed."));
                }

                m_roles.Remove(roleId);
                m_dynamicRoles.Remove(roleId);
                return ServiceResult.Good;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Per-role state owned by <see cref="RoleManager"/>.
    /// </summary>
    public sealed class RoleEntry
    {
        internal RoleEntry(NodeId roleId)
        {
            RoleId = roleId;
            Identities = [];
            Applications = [];
            Endpoints = [];
        }

        /// <summary>
        /// The role's NodeId (e.g. <see cref="ObjectIds.WellKnownRole_Observer"/>).
        /// </summary>
        public NodeId RoleId { get; }

        /// <summary>
        /// Display / browse name of the role. Set for dynamically added roles via
        /// <see cref="RoleManager.AddRole"/>; null for well-known roles.
        /// </summary>
        public string BrowseName { get; internal set; }

        /// <summary>
        /// Namespace URI requested when the role was created. Currently used only
        /// for diagnostics.
        /// </summary>
        public string NamespaceUri { get; internal set; }

        /// <summary>
        /// Identity-mapping rules added via <c>AddIdentity</c>.
        /// </summary>
        internal List<IdentityMappingRuleType> Identities { get; }

        /// <summary>
        /// Application URIs added via <c>AddApplication</c>.
        /// </summary>
        internal List<string> Applications { get; }

        /// <summary>
        /// True if <see cref="Applications"/> is an exclude list.
        /// </summary>
        internal bool ApplicationsExclude { get; set; }

        /// <summary>
        /// Endpoints added via <c>AddEndpoint</c>.
        /// </summary>
        internal List<EndpointType> Endpoints { get; }

        /// <summary>
        /// True if <see cref="Endpoints"/> is an exclude list.
        /// </summary>
        internal bool EndpointsExclude { get; set; }
    }
}
