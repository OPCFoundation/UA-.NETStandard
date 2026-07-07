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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Operational client for one Security Key Service endpoint.
    /// Wraps the OPC UA <c>GetSecurityKeys</c> method call and
    /// surfaces availability transitions so that subscribers can
    /// drive the security subsystem's PubSubState transitions
    /// without leaking transport details.
    /// </summary>
    /// <remarks>
    /// Implements the SKS pull profile defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. A single instance
    /// services every SecurityGroup hosted by the same SKS
    /// endpoint; each request carries the SecurityGroupId. The
    /// per-group cache and rotation logic live in
    /// <see cref="PullSecurityKeyProvider"/>, which composes this
    /// abstraction.
    /// </remarks>
    public interface ISecurityKeyService
    {
        /// <summary>
        /// Raised whenever the underlying SKS connectivity changes.
        /// </summary>
        event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;

        /// <summary>
        /// Issues a <c>GetSecurityKeys</c> call for the supplied
        /// <paramref name="request"/>.
        /// </summary>
        /// <param name="request">SKS pull request arguments.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The packed key material from the SKS.</returns>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SKS returns a Bad status, the call cannot
        /// be issued (no session), or the response is malformed.
        /// </exception>
        ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            SksKeyRequest request,
            CancellationToken cancellationToken = default);
    }
}
