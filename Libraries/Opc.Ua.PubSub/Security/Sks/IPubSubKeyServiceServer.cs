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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Server-side abstraction over a Security Key Service. The
    /// <see cref="SksMethodHandler"/> binds this interface to the
    /// OPC UA <c>PubSubKeyServiceType</c> Object so a Server can
    /// host the SKS for other Publishers and Subscribers.
    /// </summary>
    /// <remarks>
    /// Implements the SKS server-side surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.1">
    /// Part 14 §8.3.1 PubSubKeyServiceType</see>. The interface
    /// intentionally exposes the SecurityGroup-administration
    /// methods (<c>AddSecurityGroup</c> / <c>RemoveSecurityGroup</c>
    /// / <c>GetSecurityGroup</c>) alongside the operational
    /// <c>GetSecurityKeys</c> entry-point so that host code can
    /// administer the SKS via DI without binding to a concrete
    /// implementation.
    /// </remarks>
    public interface IPubSubKeyServiceServer
    {
        /// <summary>
        /// Snapshot of every currently-registered SecurityGroupId.
        /// </summary>
        IReadOnlyList<string> SecurityGroupIds { get; }

        /// <summary>
        /// Issues keys for the requested SecurityGroup.
        /// </summary>
        /// <param name="callerIdentity">
        /// Authenticated caller identity. Phase 8 enforces a simple
        /// non-empty contract; Phase 10 plugs Part 18 role checks.
        /// </param>
        /// <param name="request">SKS pull request arguments.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The packed key material.</returns>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the request is rejected (unknown group,
        /// missing identity, exhausted future-key budget...).
        /// </exception>
        ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            string callerIdentity,
            SksKeyRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a new SecurityGroup. Generates the initial set of
        /// keys for the group when <see cref="SksSecurityGroup.Keys"/>
        /// is empty.
        /// </summary>
        /// <param name="group">SecurityGroup configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SecurityGroupId is already registered or
        /// the policy URI is not supported.
        /// </exception>
        ValueTask AddSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a SecurityGroup from the SKS.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SecurityGroupId is not registered.
        /// </exception>
        ValueTask RemoveSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Looks up a SecurityGroup by identifier.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The configured group or <see langword="null"/> when the
        /// identifier is not registered.
        /// </returns>
        ValueTask<SksSecurityGroup?> GetSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);
    }
}
