/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Executes application configuration and deferred runtime effects within their deployment lifecycle.
    /// Implementations reject operations when that deployment has been reset or removed.
    /// </summary>
    public interface IApplicationConfigurationPushOperations
    {
        /// <summary>
        /// Executes an operation that returns a typed result.
        /// </summary>
        /// <typeparam name="T">The operation result type.</typeparam>
        ValueTask<T> ExecuteAsync<T>(
            Func<ValueTask<T>> operation,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a deferred runtime effect.
        /// </summary>
        ValueTask ExecuteAsync(
            Func<ValueTask> operation,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Supplies the independently owned application and its live push-management effects.
    /// The provider must never return the management server's configuration or certificate manager.
    /// The binding is the sole certificate and trust push writer for this target; providers must not expose
    /// a second independently coordinated push engine over the same stores.
    /// </summary>
    public interface IApplicationConfigurationPushTarget
    {
        /// <summary>
        /// Gets the target application's configuration, certificate manager and private certificate stores.
        /// These remain stable for the binding lifetime and are owned by the provider, not the binding.
        /// </summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Gets the target application's active endpoint descriptions, never those of the management server.
        /// Client-only applications return an empty array.
        /// </summary>
        ArrayOf<EndpointDescription> Endpoints { get; }

        /// <summary>
        /// Gets the target application's security-policy registry.
        /// </summary>
        ISecurityPolicyRegistry? SecurityPolicies { get; }

        /// <summary>
        /// Activates the committed certificate groups on the target application and retires channels using
        /// the supplied previous certificates. Called after registry reload and the response grace period.
        /// The provider must refresh endpoint descriptions and listeners, including initially empty slots.
        /// Previous certificate data is public material; the provider acquires keys through its certificate manager.
        /// </summary>
        ValueTask ApplyCertificateChangesAsync(
            ArrayOf<NodeId> certificateGroups,
            ArrayOf<ApplicationCertificateRotation> previousCertificates,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Revalidates the target application's channels and certificate user identities after trust changes.
        /// This must not enumerate or close channels or sessions belonging to the management server.
        /// </summary>
        ValueTask ApplyTrustListChangesAsync(
            ArrayOf<TrustListChangeEffect> effects,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Identifies a previous application certificate whose channels must be retired after a committed rotation.
    /// </summary>
    public sealed record ApplicationCertificateRotation(NodeId CertificateType, ByteString PreviousCertificate);
}
