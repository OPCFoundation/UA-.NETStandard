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
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// A single-use prepared operation bound to an inspected target and session.
    /// Input and authorization are never part of saved workspace configuration.
    /// </summary>
    internal sealed class CompanionOperationDraft
    {
        internal CompanionOperationDraft(
            CompanionTarget target,
            CompanionOperation operation,
            string? input,
            ISession session,
            DateTimeOffset expiresAt,
            CompanionTaskInput? taskInput = null,
            ArrayOf<CompanionValue> inputs = default)
        {
            if (!Uri.TryCreate(session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new InvalidOperationException(
                    "Prepare requires an absolute endpoint without userinfo or a fragment.");
            }
            Target = target;
            Operation = operation;
            Input = input;
            TaskInput = taskInput;
            EndpointUrl = endpoint.GetLeftPart(UriPartial.Path);
            ExpiresAt = expiresAt;
            m_sessionId = session.SessionId;
            m_identity = session.Identity;
            m_namespaces = session.NamespaceUris.ToArray();
            m_securityMode = session.Endpoint.SecurityMode;
            m_securityPolicy = session.Endpoint.SecurityPolicyUri;
            m_endpointUrl = session.Endpoint.EndpointUrl;
            m_applicationUri = session.Endpoint.Server?.ApplicationUri;
            m_messageContext = inputs.Count == 0 ? null : session.MessageContext;
            m_inputs = CopyInputs(inputs);
        }

        private CompanionOperationDraft(CompanionOperationDraft source, CompanionDeploymentGrant grant)
        {
            Target = source.Target;
            Operation = source.Operation;
            Input = source.Input;
            TaskInput = source.TaskInput;
            EndpointUrl = source.EndpointUrl;
            ExpiresAt = grant.ExpiresAt;
            DeploymentGrant = grant;
            m_sessionId = source.m_sessionId;
            m_identity = source.m_identity;
            m_namespaces = source.m_namespaces;
            m_securityMode = source.m_securityMode;
            m_securityPolicy = source.m_securityPolicy;
            m_endpointUrl = source.m_endpointUrl;
            m_applicationUri = source.m_applicationUri;
            m_messageContext = source.m_messageContext;
            m_inputs = source.m_inputs;
        }

        public CompanionTarget Target { get; }

        public CompanionOperation Operation { get; }

        public string? Input { get; }

        public CompanionTaskInput? TaskInput { get; }

        public ArrayOf<CompanionValue> Inputs => CopyInputs(m_inputs);

        public CompanionDeploymentGrant? DeploymentGrant { get; }

        public string EndpointUrl { get; }

        public DateTimeOffset ExpiresAt { get; }

        public string Summary =>
            $"{Operation.DisplayName}\nTarget: {Target.DisplayName} ({Target.Identifier})\n" +
            $"Endpoint: {EndpointUrl}\nEffect: {Operation.Safety}\n" +
            (TaskInput is null ? string.Empty : TaskInput.Review + "\n") +
            (DeploymentGrant is null ? string.Empty :
                $"Deployment rule: {DeploymentGrant.RuleId} (revision {DeploymentGrant.Revision})\n") +
            "Preparation expires within five minutes. Run consumes this preparation once.";

        internal CompanionOperationDraft WithGrant(CompanionDeploymentGrant grant)
        {
            ArgumentNullException.ThrowIfNull(grant);
            return new CompanionOperationDraft(this, grant);
        }

        internal bool Matches(ISession session, DateTimeOffset now)
        {
            return now < ExpiresAt &&
                session.Connected &&
                session.SessionId == m_sessionId &&
                ReferenceEquals(session.Identity, m_identity) &&
                string.Equals(session.Endpoint.EndpointUrl, m_endpointUrl, StringComparison.Ordinal) &&
                string.Equals(session.Endpoint.Server?.ApplicationUri, m_applicationUri, StringComparison.Ordinal) &&
                session.Endpoint.SecurityMode == m_securityMode &&
                string.Equals(session.Endpoint.SecurityPolicyUri, m_securityPolicy, StringComparison.Ordinal) &&
                m_namespaces.Span.SequenceEqual(session.NamespaceUris.ToArray());
        }

        private ArrayOf<CompanionValue> CopyInputs(ArrayOf<CompanionValue> inputs)
        {
            return inputs.ConvertAll(value => new CompanionValue(value.Name,
                DataValueCodec.Snapshot(value.Value, m_messageContext ??
                    throw new InvalidOperationException("Typed input snapshots require an encoding context."))));
        }

        private readonly NodeId m_sessionId;
        private readonly IUserIdentity m_identity;
        private readonly ArrayOf<string> m_namespaces;
        private readonly MessageSecurityMode m_securityMode;
        private readonly string? m_securityPolicy;
        private readonly string m_endpointUrl;
        private readonly string? m_applicationUri;
        private readonly ArrayOf<CompanionValue> m_inputs;
        private readonly IServiceMessageContext? m_messageContext;
    }
}
