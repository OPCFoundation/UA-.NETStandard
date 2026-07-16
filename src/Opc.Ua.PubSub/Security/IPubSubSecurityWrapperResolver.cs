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

namespace Opc.Ua.PubSub.Security
{
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
