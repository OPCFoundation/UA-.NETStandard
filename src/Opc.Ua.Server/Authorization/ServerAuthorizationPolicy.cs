// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard <see cref="IServerAuthorizationPolicy"/>: the rules of OPC 10000-12 (push)
    /// and OPC 10000-18 (roles and users) that <see cref="RoleAuthorizationGate"/> enforced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that carries a <see cref="ServerAuthorizationRequest.StandardRule"/> (the GDS
    /// and onboarding decisions) is decided by that rule; a companion decision without one is
    /// denied.
    /// </para>
    /// <para>
    /// Every other decision requires an authenticated (non-anonymous) session that holds the
    /// SecurityAdmin well-known role on a SignAndEncrypt channel. ConfigureAdmin also satisfies
    /// <see cref="ServerAuthorizationDecision.ConfigureAdmin"/> and
    /// <see cref="ServerAuthorizationDecision.DiagnosticsConfiguration"/>; SecurityKeyServerPush also
    /// satisfies <see cref="ServerAuthorizationDecision.SecurityKeyPush"/>.
    /// <see cref="ServerAuthorizationDecision.CertificatePush"/> accepts a Sign channel as well. The
    /// certificate push, TrustList and security key push decisions allow server-internal calls
    /// (contexts without an <see cref="OperationContext"/>), so the host can drive them in process;
    /// every other decision denies them. The channel is checked first and reported as
    /// <c>Bad_SecurityModeInsufficient</c>; the identity and role are reported as
    /// <c>Bad_UserAccessDenied</c>.
    /// </para>
    /// </remarks>
    public class ServerAuthorizationPolicy : IServerAuthorizationPolicy
    {
        /// <summary>
        /// The shared instance with the standard rules.
        /// </summary>
        public static ServerAuthorizationPolicy Default { get; } = new ServerAuthorizationPolicy();

        /// <summary>
        /// Returns the policy of the server that owns <paramref name="context"/>, or
        /// <see cref="Default"/> when the context is not bound to a server that exposes one.
        /// </summary>
        /// <param name="context">The calling context.</param>
        public static IServerAuthorizationPolicy Resolve(ISystemContext? context)
        {
            if (context is ServerSystemContext { Server: IServerAuthorizationPolicyProvider provider } &&
                provider.AuthorizationPolicy is { } policy)
            {
                return policy;
            }
            return Default;
        }

        /// <inheritdoc/>
        public virtual ServiceResult Authorize(ISystemContext context, ServerAuthorizationRequest request)
        {
            if (request.StandardRule != null)
            {
                return request.EvaluateStandardRule(context);
            }

            ServerAuthorizationDecision decision = request.Decision;
            if (!HasServerRule(decision))
            {
                // A companion decision asked without its owning component's rule.
                return request.EvaluateStandardRule(context);
            }

            OperationContext? operationContext = GetOperationContext(context);
            if (operationContext == null)
            {
                return AllowsServerInternalCalls(decision)
                    ? ServiceResult.Good
                    : new ServiceResult(
                        StatusCodes.BadSecurityModeInsufficient,
                        new LocalizedText("Method must be invoked through an encrypted channel."));
            }

            MessageSecurityMode securityMode =
                operationContext.ChannelContext?.EndpointDescription?.SecurityMode ?? MessageSecurityMode.Invalid;
            if (decision == ServerAuthorizationDecision.CertificatePush)
            {
                if (securityMode is not (MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt))
                {
                    return new ServiceResult(
                        StatusCodes.BadSecurityModeInsufficient,
                        new LocalizedText("This Method must be called from an authenticated SecureChannel " +
                            "(MessageSecurityMode Sign or SignAndEncrypt)."));
                }
            }
            else if (securityMode != MessageSecurityMode.SignAndEncrypt)
            {
                return new ServiceResult(
                    StatusCodes.BadSecurityModeInsufficient,
                    new LocalizedText("Method must be invoked through an encrypted channel."));
            }

            IUserIdentity? identity = (context as ISessionSystemContext)?.UserIdentity ?? operationContext.UserIdentity;
            if (identity == null || identity.TokenType == UserTokenType.Anonymous)
            {
                return new ServiceResult(
                    StatusCodes.BadUserAccessDenied,
                    new LocalizedText("An authenticated SecurityAdmin session is required."));
            }

            if (HoldsRole(identity, Objects.WellKnownRole_SecurityAdmin))
            {
                return ServiceResult.Good;
            }

            switch (decision)
            {
                case ServerAuthorizationDecision.ConfigureAdmin:
                case ServerAuthorizationDecision.DiagnosticsConfiguration:
                    return HoldsRole(identity, Objects.WellKnownRole_ConfigureAdmin)
                        ? ServiceResult.Good
                        : new ServiceResult(
                            StatusCodes.BadUserAccessDenied,
                            new LocalizedText("The session does not hold the SecurityAdmin or ConfigureAdmin role."));
                case ServerAuthorizationDecision.SecurityKeyPush:
                    return HoldsRole(identity, Objects.WellKnownRole_SecurityKeyServerPush)
                        ? ServiceResult.Good
                        : new ServiceResult(
                            StatusCodes.BadUserAccessDenied,
                            new LocalizedText("The session does not hold the SecurityAdmin or SecurityKeyServerPush role."));
                default:
                    return new ServiceResult(
                        StatusCodes.BadUserAccessDenied,
                        new LocalizedText("The session does not hold the SecurityAdmin role."));
            }
        }

        /// <inheritdoc/>
        public virtual ValueTask<ServiceResult> AuthorizeAsync(
            ISystemContext context,
            ServerAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(Authorize(context, request));
        }

        /// <summary>
        /// Returns <see langword="true"/> when the server stack holds the standard rule of
        /// <paramref name="decision"/>; the companion decisions (GDS directory, certificate
        /// manager, KeyCredential service, onboarding) take their rule from the request.
        /// </summary>
        public static bool HasServerRule(ServerAuthorizationDecision decision)
        {
            return decision is not (
                ServerAuthorizationDecision.ApplicationRegistration or
                ServerAuthorizationDecision.ApplicationQuery or
                ServerAuthorizationDecision.CertificateManagement or
                ServerAuthorizationDecision.CertificateRevocation or
                ServerAuthorizationDecision.KeyCredentialManagement or
                ServerAuthorizationDecision.DeviceOnboarding);
        }

        /// <summary>
        /// Returns <see langword="true"/> when a server-internal call (no
        /// <see cref="OperationContext"/>) is granted <paramref name="decision"/>.
        /// </summary>
        protected virtual bool AllowsServerInternalCalls(ServerAuthorizationDecision decision)
        {
            return decision is
                ServerAuthorizationDecision.CertificatePush or
                ServerAuthorizationDecision.CertificatePushEncrypted or
                ServerAuthorizationDecision.TrustListRead or
                ServerAuthorizationDecision.TrustListWrite or
                ServerAuthorizationDecision.SecurityKeyPush;
        }

        /// <summary>
        /// Returns the operation context of a client request, or <see langword="null"/> for a
        /// server-internal call.
        /// </summary>
        protected static OperationContext? GetOperationContext(ISystemContext? context)
        {
            return context switch
            {
                SessionSystemContext { OperationContext: OperationContext sessionOperation } => sessionOperation,
                SystemContext { OperationContext: OperationContext operation } => operation,
                _ => null
            };
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="identity"/> holds the well-known role
        /// with the namespace 0 numeric identifier <paramref name="roleId"/>.
        /// </summary>
        /// <remarks>
        /// Well-known role NodeIds are always in namespace 0 (Part 3 §4.9.2, Part 6 §F.7), so a
        /// role NodeId that a custom <see cref="IRoleManager"/> builds in another representation
        /// still matches.
        /// </remarks>
        protected static bool HoldsRole(IUserIdentity identity, uint roleId)
        {
            foreach (NodeId nodeId in identity.GrantedRoleIds)
            {
                if (nodeId.NamespaceIndex == 0 &&
                    nodeId.IdType == IdType.Numeric &&
                    nodeId.TryGetValue(out uint id) &&
                    id == roleId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
