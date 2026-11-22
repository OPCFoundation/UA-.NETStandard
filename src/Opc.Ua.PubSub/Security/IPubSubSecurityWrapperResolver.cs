// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Optional, non-secret registration evidence for out-of-band security keys.
    /// Membership does not replace key validity checks or wrapper resolution.
    /// </summary>
    public interface IPubSubSecurityKeyProviderCatalog
    {
        /// <summary>
        /// Security groups with explicitly registered key providers. These groups
        /// need not advertise an SKS endpoint in their portable configuration.
        /// </summary>
        ArrayOf<string> SecurityGroupIds { get; }
    }

    /// <summary>
    /// Strategy used by <c>PubSubApplication</c> to materialise a
    /// <see cref="UadpSecurityWrapper"/> for a configured
    /// PubSubConnection. Implementations either return
    /// <see langword="null"/> (no security) or a fully-initialised
    /// wrapper bound to the connection's
    /// <c>SecurityKeyServices</c> / <c>SecurityGroupId</c>.
    /// </summary>
    /// <remarks>
    /// Resolves the per-connection security context per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// §8.3 Security Key Service</see>. The default resolver returns
    /// <see langword="null"/>; deployments wire a real resolver via
    /// the DI extensions.
    /// </remarks>
    public interface IPubSubSecurityWrapperResolver
    {
        /// <summary>
        /// Resolve the wrapper context for the supplied connection.
        /// Returns <see langword="null"/> when the connection is
        /// configured with <c>MessageSecurityMode = None</c> or when
        /// the resolver does not have the keys to construct a wrapper.
        /// </summary>
        /// <param name="connection">Connection configuration.</param>
        /// <returns>
        /// A <see cref="PubSubSecurityContext"/> describing the
        /// wrapper, the desired
        /// <see cref="UadpSecurityWrapOptions"/> and the underlying
        /// security policy, or <see langword="null"/> for an unsecured
        /// connection.
        /// </returns>
        PubSubSecurityContext? Resolve(PubSubConnectionDataType connection);
    }

    /// <summary>
    /// Per-connection security context returned by
    /// <see cref="IPubSubSecurityWrapperResolver"/>.
    /// </summary>
    /// <param name="Wrapper">Configured UADP security wrapper.</param>
    /// <param name="WrapOptions">Sign/encrypt selection.</param>
    public sealed record PubSubSecurityContext(
        UadpSecurityWrapper Wrapper,
        UadpSecurityWrapOptions WrapOptions);
}
