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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Authorizes a particular prepared deployment request. Denial throws; a grant
    /// never replaces server-side authorization or the caller's fresh confirmation.
    /// </summary>
    internal interface ICompanionDeploymentPolicy
    {
        ValueTask<CompanionDeploymentGrant> AuthorizeAsync(
            CompanionContext context,
            CompanionOperationDraft draft,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Non-secret policy evidence pinned during preparation and checked again on Run.
    /// </summary>
    internal sealed record CompanionDeploymentGrant(string RuleId, string Revision, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Trusted host configuration for one exact operation. Predicates inspect identity
    /// and immutable input; neither display names nor saved workspace data grant access.
    /// </summary>
    internal sealed class CompanionDeploymentRule
    {
        public CompanionDeploymentRule(
            string id,
            string revision,
            string endpointUrl,
            string serverApplicationUri,
            string securityPolicyUri,
            string providerId,
            ExpandedNodeId targetId,
            string operationId,
            DateTimeOffset expiresAt,
            Func<IUserIdentity, bool> acceptsIdentity,
            Func<CompanionOperationDraft, bool> acceptsInput)
        {
            ConnectionReference.Validate(id);
            ConnectionReference.Validate(revision);
            ConnectionReference.Validate(providerId);
            ConnectionReference.Validate(operationId);
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new ArgumentException("An exact absolute endpoint without userinfo or fragment is required.",
                    nameof(endpointUrl));
            }
            ConnectionReference.ValidateUri(serverApplicationUri);
            if (string.IsNullOrWhiteSpace(securityPolicyUri) ||
                securityPolicyUri.Length > 2048 ||
                !Uri.TryCreate(securityPolicyUri, UriKind.Absolute, out Uri? policy) ||
                !string.IsNullOrEmpty(policy.UserInfo) ||
                !string.IsNullOrEmpty(policy.Query) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                throw new ArgumentException("Deployment operations require a secure channel policy.",
                    nameof(securityPolicyUri));
            }
            if (targetId.IsNull ||
                targetId.ServerIndex != 0 ||
                (targetId.NamespaceIndex != 0 && string.IsNullOrEmpty(targetId.NamespaceUri)))
            {
                throw new ArgumentException("Use a namespace-URI target on the primary server.", nameof(targetId));
            }
            ArgumentNullException.ThrowIfNull(acceptsIdentity);
            ArgumentNullException.ThrowIfNull(acceptsInput);
            Id = id;
            Revision = revision;
            EndpointUrl = endpointUrl;
            ServerApplicationUri = serverApplicationUri;
            SecurityPolicyUri = securityPolicyUri;
            ProviderId = providerId;
            TargetId = targetId;
            OperationId = operationId;
            ExpiresAt = expiresAt;
            m_acceptsIdentity = acceptsIdentity;
            m_acceptsInput = acceptsInput;
        }

        public string Id { get; }

        public string Revision { get; }

        public string EndpointUrl { get; }

        public string ServerApplicationUri { get; }

        public string SecurityPolicyUri { get; }

        public string ProviderId { get; }

        public ExpandedNodeId TargetId { get; }

        public string OperationId { get; }

        public DateTimeOffset ExpiresAt { get; }

        internal bool Matches(CompanionContext context, CompanionOperationDraft draft, DateTimeOffset now)
        {
            EndpointDescription endpoint = context.Session.Endpoint;
            return now < ExpiresAt &&
                draft.Operation.Safety == CompanionOperationSafety.DeploymentMutation &&
                endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                string.Equals(endpoint.EndpointUrl, EndpointUrl, StringComparison.Ordinal) &&
                string.Equals(endpoint.Server?.ApplicationUri, ServerApplicationUri, StringComparison.Ordinal) &&
                string.Equals(endpoint.SecurityPolicyUri, SecurityPolicyUri, StringComparison.Ordinal) &&
                string.Equals(draft.Target.ProviderId, ProviderId, StringComparison.Ordinal) &&
                string.Equals(draft.Operation.Id, OperationId, StringComparison.Ordinal) &&
                ExpandedNodeId.ToNodeId(TargetId, context.Session.NamespaceUris) == draft.Target.NodeId &&
                m_acceptsIdentity(context.Session.Identity) &&
                m_acceptsInput(draft);
        }

        private readonly Func<IUserIdentity, bool> m_acceptsIdentity;
        private readonly Func<CompanionOperationDraft, bool> m_acceptsInput;
    }

    /// <summary>
    /// Default-deny deployment policy. All matching dimensions and both predicates
    /// must agree, and overlapping grants are rejected rather than selected by order.
    /// </summary>
    internal sealed class ConfiguredCompanionDeploymentPolicy : ICompanionDeploymentPolicy
    {
        public ConfiguredCompanionDeploymentPolicy(
            ArrayOf<CompanionDeploymentRule> rules = default,
            TimeProvider? timeProvider = null)
        {
            if (rules.Count > 256)
            {
                throw new ArgumentException("At most 256 deployment rules can be registered.", nameof(rules));
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompanionDeploymentRule rule in rules)
            {
                ArgumentNullException.ThrowIfNull(rule);
                if (!ids.Add(rule.Id))
                {
                    throw new ArgumentException("Deployment rule identifiers must be unique.", nameof(rules));
                }
            }
            m_rules = rules.ConvertAll(static rule => rule);
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        public ValueTask<CompanionDeploymentGrant> AuthorizeAsync(
            CompanionContext context,
            CompanionOperationDraft draft,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(draft);
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            if (!draft.Matches(context.Session, now))
            {
                throw new UnauthorizedAccessException("The prepared deployment request changed or expired.");
            }
            CompanionDeploymentRule? selected = null;
            foreach (CompanionDeploymentRule rule in m_rules)
            {
                if (!rule.Matches(context, draft, now))
                {
                    continue;
                }
                if (selected is not null)
                {
                    throw new UnauthorizedAccessException("More than one deployment rule authorizes this request.");
                }
                selected = rule;
            }
            if (selected is null)
            {
                throw new UnauthorizedAccessException(
                    "No configured deployment rule authorizes this exact task, identity and input.");
            }
            DateTimeOffset expires = selected.ExpiresAt < draft.ExpiresAt ? selected.ExpiresAt : draft.ExpiresAt;
            return ValueTask.FromResult(new CompanionDeploymentGrant(selected.Id, selected.Revision, expires));
        }

        private readonly ArrayOf<CompanionDeploymentRule> m_rules;
        private readonly TimeProvider m_timeProvider;
    }
}
