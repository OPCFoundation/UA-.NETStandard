// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// One question to an <see cref="IServerAuthorizationPolicy"/>: the decision, the application it
    /// targets and, for decisions owned by a companion component, that component's standard rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ServerAuthorizationDecision"/> converts implicitly, so
    /// <c>policy.Authorize(context, ServerAuthorizationDecision.SecurityAdmin)</c> works.
    /// </para>
    /// <para>
    /// Decisions whose rules the server stack does not know (the GDS directory and certificate
    /// manager, for example) carry the owning component's <see cref="StandardRule"/>. The default
    /// <see cref="ServerAuthorizationPolicy"/> applies it; a host policy can deny first and then
    /// defer to it through <see cref="EvaluateStandardRule"/> or the base implementation.
    /// </para>
    /// </remarks>
    public readonly struct ServerAuthorizationRequest
    {
        /// <summary>
        /// Creates a request for a decision with the server's own standard rule.
        /// </summary>
        /// <param name="decision">The decision.</param>
        public ServerAuthorizationRequest(ServerAuthorizationDecision decision)
            : this(decision, NodeId.Null, null)
        {
        }

        /// <summary>
        /// Creates a request for a decision that targets an application and carries the standard
        /// rule of the component that owns the decision.
        /// </summary>
        /// <param name="decision">The decision.</param>
        /// <param name="applicationId">The application the operation targets, or <see cref="NodeId.Null"/>.</param>
        /// <param name="standardRule">
        /// The owning component's standard rule, or <see langword="null"/> to use the server's rule.
        /// </param>
        public ServerAuthorizationRequest(
            ServerAuthorizationDecision decision,
            NodeId applicationId,
            Func<ISystemContext, ServiceResult>? standardRule)
        {
            Decision = decision;
            ApplicationId = applicationId;
            StandardRule = standardRule;
        }

        /// <summary>
        /// The decision.
        /// </summary>
        public ServerAuthorizationDecision Decision { get; }

        /// <summary>
        /// The application the operation targets (for example the GDS application record), or
        /// <see cref="NodeId.Null"/>.
        /// </summary>
        public NodeId ApplicationId { get; }

        /// <summary>
        /// The standard rule of the component that owns the decision, or <see langword="null"/> when
        /// the server's <see cref="ServerAuthorizationPolicy"/> holds the rule.
        /// </summary>
        public Func<ISystemContext, ServiceResult>? StandardRule { get; }

        /// <summary>
        /// Applies <see cref="StandardRule"/>; denies with <c>Bad_UserAccessDenied</c> when the request
        /// carries none.
        /// </summary>
        /// <param name="context">The calling context.</param>
        public ServiceResult EvaluateStandardRule(ISystemContext context)
        {
            return StandardRule?.Invoke(context) ??
                new ServiceResult(
                    StatusCodes.BadUserAccessDenied,
                    new LocalizedText("No rule is configured for this authorization decision."));
        }

        /// <summary>
        /// Creates a request for <paramref name="decision"/> with the server's standard rule.
        /// </summary>
        /// <param name="decision">The decision.</param>
        public static implicit operator ServerAuthorizationRequest(ServerAuthorizationDecision decision)
        {
            return new ServerAuthorizationRequest(decision);
        }

        /// <summary>
        /// Creates a request for <paramref name="decision"/> with the server's standard rule.
        /// </summary>
        /// <param name="decision">The decision.</param>
        public static ServerAuthorizationRequest FromServerAuthorizationDecision(ServerAuthorizationDecision decision)
        {
            return new ServerAuthorizationRequest(decision);
        }
    }
}
