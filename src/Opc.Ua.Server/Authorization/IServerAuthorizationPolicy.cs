// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The single authorization policy point of the server for administrative operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every built-in administrator check (role and user management, certificate and
    /// KeyCredential push, diagnostics, alias mutation and <see cref="RoleAuthorizationGate"/>)
    /// asks the server's policy for a named <see cref="ServerAuthorizationDecision"/>.
    /// </para>
    /// <para>
    /// Register an implementation in the service container, or override
    /// <c>StandardServer.CreateAuthorizationPolicy</c>. To keep the standard rules and add host
    /// rules, derive from <see cref="ServerAuthorizationPolicy"/> and call the base method first.
    /// </para>
    /// <para>
    /// Synchronous call sites (attribute read callbacks, <see cref="RoleAuthorizationGate"/>) call
    /// <see cref="Authorize"/>; asynchronous method handlers call <see cref="AuthorizeAsync"/>.
    /// An implementation must return the same result from both.
    /// </para>
    /// </remarks>
    public interface IServerAuthorizationPolicy
    {
        /// <summary>
        /// Decides whether the caller of <paramref name="context"/> may perform
        /// <paramref name="request"/>.
        /// </summary>
        /// <param name="context">The calling context.</param>
        /// <param name="request">The decision to make, with its target and owning component rule.</param>
        /// <returns>
        /// <c>Good</c> when access is granted; otherwise the status to return to the client,
        /// normally <c>Bad_SecurityModeInsufficient</c> or <c>Bad_UserAccessDenied</c>.
        /// </returns>
        ServiceResult Authorize(ISystemContext context, ServerAuthorizationRequest request);

        /// <summary>
        /// Asynchronous form of <see cref="Authorize"/>.
        /// </summary>
        /// <param name="context">The calling context.</param>
        /// <param name="request">The decision to make, with its target and owning component rule.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask<ServiceResult> AuthorizeAsync(
            ISystemContext context,
            ServerAuthorizationRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implemented by an <see cref="IServerInternal"/> that exposes its authorization policy.
    /// </summary>
    public interface IServerAuthorizationPolicyProvider
    {
        /// <summary>
        /// The authorization policy of the server.
        /// </summary>
        IServerAuthorizationPolicy AuthorizationPolicy { get; }
    }
}
