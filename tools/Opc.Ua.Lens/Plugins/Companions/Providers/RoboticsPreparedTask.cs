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
using Opc.Ua.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Additional owned-simulation capability. Configured deployment rules alone cannot authorize a robot.
/// </summary>
internal interface IRoboticsSimulationPolicy
{
    /// <summary>
    /// Verifies the exact live sample, peer, explicit finite grant and requested Robotics operation.
    /// </summary>
    ValueTask<CompanionDeploymentGrant> AuthorizeRoboticsAsync(
        CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken);
}

/// <summary>
/// Pins immutable domain input, live evidence and one use to the borrowed primary session.
/// It never acquires, renews or releases remote resources during disposal or restore.
/// </summary>
internal sealed class RoboticsPreparedTask : CompanionTaskInput
{
    /// <summary>
    /// Copies input before it is reviewed or used to resolve any operation.
    /// </summary>
    public RoboticsPreparedTask(
        ICompanionProvider provider, CompanionContext context, CompanionTarget target, CompanionOperation operation,
        ArrayOf<CompanionValue> inputs, TimeProvider timeProvider, string review)
    {
        m_provider = provider;
        m_session = context.Session;
        m_messageContext = context.Session.MessageContext;
        m_certificate = context.Session.Endpoint.ServerCertificate.Copy();
        m_time = timeProvider;
        Review = review;
        Draft = new CompanionOperationDraft(target, operation, null, context.Session,
            timeProvider.GetUtcNow().AddMinutes(5), this,
            CompanionInputContract.Snapshot(inputs, context.Session.MessageContext));
    }

    /// <inheritdoc/>
    public override string Review { get; }

    /// <summary>
    /// Gets the session- and authorization-bound draft.
    /// </summary>
    public CompanionOperationDraft Draft { get; private set; }

    /// <summary>
    /// Gets the exact instance method and observable preconditions, when resolved.
    /// </summary>
    public RoboticsMethodBinding Binding { get; private set; } = new(NodeId.Null, NodeId.Null, [], []);

    /// <summary>
    /// Pins method metadata and state without dispatch.
    /// </summary>
    public void Bind(RoboticsMethodBinding binding)
    {
        Binding = binding;
    }

    /// <summary>
    /// Requires the same live primary session, identity, peer and metadata before and after each remote call.
    /// </summary>
    public void RequireCurrent(CompanionContext context)
    {
        if (!ReferenceEquals(context.Session, m_session) ||
            !ReferenceEquals(context.Session.MessageContext, m_messageContext) ||
            context.Session.Endpoint.ServerCertificate != m_certificate ||
            !Draft.Matches(context.Session, m_time.GetUtcNow()) ||
            (Binding.ExpiresAt is { } expires && m_time.GetUtcNow() >= expires))
        {
            throw new UnauthorizedAccessException(
                "The Robotics preparation expired or its session, identity, peer or namespaces changed.");
        }
    }

    /// <summary>
    /// Consumes before validation or dispatch; an ambiguous result can never replay this preparation.
    /// </summary>
    public void Consume(ICompanionProvider provider, CompanionTarget target, string operationId)
    {
        if (Interlocked.Exchange(ref m_consumed, 1) != 0)
        {
            throw new InvalidOperationException("The Robotics preparation is consumed. Inspect; never replay it.");
        }
        if (!ReferenceEquals(provider, m_provider) || target != Draft.Target || operationId != Draft.Operation.Id)
        {
            throw new ArgumentException("The preparation belongs to another Robotics provider, target or operation.");
        }
    }

    /// <summary>
    /// Obtains or revalidates the same finite owned-run grant. Authority is not authorization.
    /// </summary>
    public async ValueTask AuthorizeAsync(
        CompanionContext context, IRoboticsSimulationPolicy? policy, CancellationToken cancellationToken)
    {
        RequireCurrent(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (Draft.Operation.Safety == CompanionOperationSafety.ReadOnly)
        {
            return;
        }
        if (policy is null ||
            context.Session.Identity.TokenType is not (UserTokenType.UserName or UserTokenType.Certificate) ||
            context.Session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt)
        {
            throw new UnauthorizedAccessException(
                "Robotics commands require an authenticated, explicitly authorized owned simulation; " +
                "no anonymous fallback.");
        }
        CompanionDeploymentGrant grant = await policy.AuthorizeRoboticsAsync(context, Draft, cancellationToken)
            .ConfigureAwait(false);
        if (grant is null ||
            string.IsNullOrWhiteSpace(grant.RuleId) ||
            string.IsNullOrWhiteSpace(grant.Revision) ||
            grant.ExpiresAt <= m_time.GetUtcNow() ||
            grant.ExpiresAt > Draft.ExpiresAt ||
            (Draft.DeploymentGrant is { } previous && grant != previous))
        {
            throw new UnauthorizedAccessException("The Robotics owned-run authorization changed or expired.");
        }
        Draft = Draft.WithGrant(grant);
        RequireCurrent(context);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Rejects changed targets, methods, state or capabilities rather than silently rebinding.
    /// </summary>
    public void RequireBinding(RoboticsMethodBinding current)
    {
        if (Binding.Owner != current.Owner ||
            Binding.Method != current.Method ||
            Binding.ExpiresAt != current.ExpiresAt ||
            !Binding.Nodes.Span.SequenceEqual(current.Nodes.Span) ||
            !Binding.Values.Span.SequenceEqual(current.Values.Span))
        {
            throw new ServiceResultException(StatusCodes.BadInvalidState,
                "The Robotics binding or state changed. Inspect and prepare again.");
        }
    }

    private readonly ICompanionProvider m_provider;
    private readonly ISession m_session;
    private readonly IServiceMessageContext m_messageContext;
    private readonly ByteString m_certificate;
    private readonly TimeProvider m_time;
    private int m_consumed;
}

/// <summary>
/// Exact instance nodes and typed observable preconditions; timestamps are deliberately not preconditions.
/// </summary>
/// <param name="Owner">Object receiving the generated method call.</param>
/// <param name="Method">Verified instance method, or null for a local preview.</param>
/// <param name="Nodes">Exact nodes supplying the preconditions.</param>
/// <param name="Values">Copied values observed at those nodes.</param>
/// <param name="ExpiresAt">Optional server-published broker expiry; absence never creates a local lease.</param>
internal sealed record RoboticsMethodBinding(
    NodeId Owner, NodeId Method, ArrayOf<NodeId> Nodes, ArrayOf<Variant> Values, DateTimeOffset? ExpiresAt = null);
