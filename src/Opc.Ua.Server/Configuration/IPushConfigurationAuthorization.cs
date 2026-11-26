// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Adds asynchronous host policy to standard certificate-push and KeyCredential-push operations
    /// without replacing channel, SecurityAdmin or transaction-owner checks. Configure before the
    /// server creates its address space.
    /// </summary>
    /// <remarks>
    /// The hook runs after the server's <see cref="IServerAuthorizationPolicy"/> grants
    /// <see cref="ServerAuthorizationDecision.CertificatePush"/>,
    /// <see cref="ServerAuthorizationDecision.CertificatePushEncrypted"/> or
    /// <see cref="ServerAuthorizationDecision.KeyCredentialPush"/>. Use it for a decision that is
    /// specific to one push target (as the GDS does for each managed application); use an
    /// <see cref="IServerAuthorizationPolicy"/> for server-wide rules.
    /// </remarks>
    public interface IPushConfigurationAuthorization
    {
        /// <summary>
        /// Requires authorization before key generation, certificate staging, TrustList writes, apply,
        /// cancellation or reset. Exceptions reject the operation before its configuration effects.
        /// The context contains the server-owned authenticated channel and session evidence.
        /// </summary>
        ValueTask AuthorizeAsync(ISystemContext context, CancellationToken cancellationToken = default);
    }
}
