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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Additional, host-supplied authorization for the exact AI request and its disclosed destination.
    /// This cannot replace the workspace's deployment policy or fresh user confirmation.
    /// No implementation is registered by default: non-local inference remains denied.
    /// </summary>
    internal interface IAITaskEgressPolicy
    {
        ValueTask AuthorizeAsync(
            CompanionContext context,
            AICompanionTaskInput input,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Immutable AI preparation. The borrowed session is only an identity boundary, never an owned resource.
    /// Payloads are kept out of Review and are not written to workspace state or logs.
    /// </summary>
    internal sealed class AICompanionTaskInput : CompanionTaskInput
    {
        public AICompanionTaskInput(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            string review,
            AIDeploymentSnapshot? deployment = null,
            ByteString payload = default,
            string contentType = "",
            string capability = "",
            double timeout = 0,
            NodeId subjectId = default,
            NodeId stateId = default,
            string subjectIdentity = "",
            NodeId modelId = default,
            NodeId datasetId = default,
            int phase = 0,
            uint observations = 1,
            ulong maximumBytes = 0,
            ByteString expectedDigest = default,
            ulong expectedSize = 0)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(review);
            m_session = context.Session;
            m_sessionId = context.Session.SessionId;
            m_endpointUrl = context.Session.Endpoint.EndpointUrl ??
                throw new InvalidOperationException("The source endpoint is unavailable.");
            m_securityPolicy = context.Session.Endpoint.SecurityPolicyUri ??
                throw new InvalidOperationException("The source security policy is unavailable.");
            m_securityMode = context.Session.Endpoint.SecurityMode;
            Target = target;
            OperationId = operationId;
            Review = review;
            Deployment = deployment;
            m_payload = payload.Copy();
            ContentType = contentType;
            Capability = capability;
            Timeout = timeout;
            SubjectId = subjectId;
            StateId = stateId;
            SubjectIdentity = subjectIdentity;
            ModelId = modelId;
            DatasetId = datasetId;
            Phase = phase;
            Observations = observations;
            MaximumBytes = maximumBytes;
            m_expectedDigest = expectedDigest.Copy();
            ExpectedSize = expectedSize;
        }

        public override string Review { get; }

        public CompanionTarget Target { get; }

        public string OperationId { get; }

        public AIDeploymentSnapshot? Deployment { get; }

        public ByteString Payload => m_payload.Copy();

        public string ContentType { get; }

        public string Capability { get; }

        public double Timeout { get; }

        public NodeId SubjectId { get; }

        public NodeId StateId { get; }

        public string SubjectIdentity { get; }

        public NodeId ModelId { get; }

        public NodeId DatasetId { get; }

        public int Phase { get; }

        public uint Observations { get; }

        public ulong MaximumBytes { get; }

        public ByteString ExpectedDigest => m_expectedDigest.Copy();

        public ulong ExpectedSize { get; }

        public void RequireMatch(CompanionContext context, CompanionTarget target, string operationId)
        {
            if (!ReferenceEquals(context.Session, m_session) ||
                context.Session.SessionId != m_sessionId ||
                !context.Session.Connected ||
                target != Target ||
                operationId != OperationId ||
                context.Session.Endpoint.EndpointUrl != m_endpointUrl ||
                context.Session.Endpoint.SecurityPolicyUri != m_securityPolicy ||
                context.Session.Endpoint.SecurityMode != m_securityMode)
            {
                throw new InvalidOperationException("The AI task or session changed. Prepare the task again.");
            }
        }

        private readonly ISession m_session;
        private readonly NodeId m_sessionId;
        private readonly string m_endpointUrl;
        private readonly string m_securityPolicy;
        private readonly MessageSecurityMode m_securityMode;
        private readonly ByteString m_payload;
        private readonly ByteString m_expectedDigest;
    }
}
