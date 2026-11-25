// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Centralised authorization gate enforcing the Part 18 §4.2 / §4.4 / §5.2
    /// requirements that role/user management methods are invoked over an
    /// encrypted channel by a session holding an administrator role.
    /// </summary>
    /// <remarks>
    /// Used by both <see cref="RoleStateBinding"/> and the user management
    /// binding. Application code can also call <see cref="CheckAdmin"/> to
    /// enforce the same policy on custom methods.
    /// </remarks>
    public static class RoleAuthorizationGate
    {
        /// <summary>
        /// Returns <c>Good</c> if the calling session is authorised to invoke
        /// an administrator method on the role/user management API.
        /// Otherwise returns:
        /// <list type="bullet">
        ///   <item><c>Bad_SecurityModeInsufficient</c> if the secure channel
        ///   is not encrypted (Part 18 §4.2/§4.4/§5.2);</item>
        ///   <item><c>Bad_UserAccessDenied</c> if the session is not authenticated
        ///   or does not hold the <c>SecurityAdmin</c> role;</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// Asks the server's <see cref="IServerAuthorizationPolicy"/> for
        /// <see cref="ServerAuthorizationDecision.SecurityAdmin"/>.
        /// </remarks>
        public static ServiceResult CheckAdmin(ISystemContext context)
        {
            return Check(context, ServerAuthorizationDecision.SecurityAdmin);
        }

        /// <summary>
        /// Asks the <see cref="IServerAuthorizationPolicy"/> of the server that owns
        /// <paramref name="context"/> for <paramref name="decision"/>.
        /// </summary>
        /// <param name="context">The calling context.</param>
        /// <param name="decision">The decision to make.</param>
        public static ServiceResult Check(ISystemContext context, ServerAuthorizationDecision decision)
        {
            return ServerAuthorizationPolicy.Resolve(context).Authorize(context, decision);
        }

        /// <summary>
        /// Returns <c>Good</c> if the calling session uses an encrypted secure
        /// channel; <c>Bad_SecurityModeInsufficient</c> otherwise.
        /// </summary>
        public static ServiceResult CheckEncryptedChannel(ISystemContext context)
        {
            SecureChannelContext? channelContext = GetChannelContext(context);
            if (channelContext?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                return new ServiceResult(StatusCodes.BadSecurityModeInsufficient,
                    new LocalizedText("Method must be invoked through an encrypted channel."));
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns <c>Good</c> if the calling session is the user whose name
        /// matches <paramref name="userName"/>, the session uses a USERNAME
        /// identity token, and the channel is encrypted. Returns:
        /// <list type="bullet">
        ///   <item><c>Bad_SecurityModeInsufficient</c> if channel is not encrypted;</item>
        ///   <item><c>Bad_InvalidState</c> if the session token is not USERNAME;</item>
        ///   <item><c>Bad_UserAccessDenied</c> if the session user does not match.</item>
        /// </list>
        /// </summary>
        public static ServiceResult CheckSelfUserName(ISystemContext context, string userName)
        {
            ServiceResult channel = CheckEncryptedChannel(context);
            if (ServiceResult.IsBad(channel))
            {
                return channel;
            }

            IUserIdentity? identity = GetUserIdentity(context);
            if (identity == null || identity.TokenType != UserTokenType.UserName)
            {
                return new ServiceResult(StatusCodes.BadInvalidState,
                    new LocalizedText("Method requires a USERNAME identity token."));
            }

            if (!string.Equals(identity.DisplayName, userName, StringComparison.Ordinal))
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied,
                    new LocalizedText("Cannot change another user's password."));
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Extracts the session's <see cref="IUserIdentity"/>, walking through
        /// any wrapping <see cref="RoleBasedIdentity"/> instances unchanged.
        /// </summary>
        public static IUserIdentity? GetUserIdentity(ISystemContext? context)
        {
            return (context as ISessionSystemContext)?.UserIdentity;
        }

        private static SecureChannelContext? GetChannelContext(ISystemContext? context)
        {
            // The OPC UA Server hands a SessionSystemContext (concretely a
            // ServerSystemContext) to method-state OnCallAsync delegates.
            // Match both base classes defensively so the
            // gate works whether the caller is the server-side method
            // dispatcher (SessionSystemContext) or a fluent address-space
            // path that uses plain SystemContext.
            if (context is SessionSystemContext { OperationContext: OperationContext sessionOp })
            {
                return sessionOp.ChannelContext;
            }
            if (context is SystemContext { OperationContext: OperationContext op })
            {
                return op.ChannelContext;
            }
            return null;
        }
    }
}
