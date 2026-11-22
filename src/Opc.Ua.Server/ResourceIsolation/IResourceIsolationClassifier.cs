// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Net;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A trusted host's classification result. Weights and limits are resolved from policy, not this result.
    /// </summary>
    public readonly record struct ResourceIsolationIdentity(string Key, ResourceIsolationClass Class);

    /// <summary>
    /// Explicit trust boundary for deployment-specific tenant and ingress mappings.
    /// </summary>
    /// <remarks>
    /// Implementations must use an authenticated ingress boundary or verified channel/session
    /// evidence, never request names, source-address history or unvalidated certificates.
    /// Returning false uses ordinary best-effort shared classification.
    /// </remarks>
    public interface IResourceIsolationClassifier
    {
        /// <summary>
        /// Classifies an observed endpoint using an explicitly trusted ingress mapping.
        /// This is the only way to protect a caller before protocol authentication.
        /// </summary>
        bool TryClassifyIngress(IPEndPoint? remoteEndpoint, out ResourceIsolationIdentity identity);

        /// <summary>
        /// Maps a transport-established channel and optional live verified session to an owner.
        /// None-policy certificates are removed before this method is called.
        /// A successful mapping is explicitly trusted host policy, not session authorization.
        /// </summary>
        bool TryClassify(
            SecureChannelContext channelContext,
            SessionBindingContext? sessionBinding,
            out ResourceIsolationIdentity identity);
    }
}
