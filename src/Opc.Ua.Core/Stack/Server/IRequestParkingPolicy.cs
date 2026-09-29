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

namespace Opc.Ua
{
    /// <summary>
    /// Opts custom service requests into cooperative parking through <see cref="RequestLifetime.ParkSink"/>.
    /// Built-in Publish requests remain eligible independently of this policy.
    /// </summary>
    /// <remarks>
    /// The endpoint evaluates this policy before queue admission. Implementations must be thread-safe,
    /// bounded, and free of authentication or session-activity side effects; selection does not authorize
    /// the request. When resource isolation is enabled, selected requests reserve parked-request capacity
    /// before queuing, even if their handler never parks. Retained-request cost and parked capacity remain
    /// charged until actual completion. The server's DecoupleHeldPublishRequests switch disables worker
    /// decoupling for both Publish and custom requests.
    /// </remarks>
    public interface IRequestParkingPolicy
    {
        /// <summary>
        /// Returns whether the request's handler supports the cooperative parking contract.
        /// Returning true supplies a sink; it does not itself release an execution worker.
        /// </summary>
        bool CanPark(IServiceRequest request);
    }

    /// <summary>
    /// Optional capability for servers that supply custom request parking without changing <see cref="IServerBase"/>.
    /// </summary>
    public interface IRequestParkingPolicySource
    {
        /// <summary>
        /// Gets the optional policy that selects custom requests for cooperative parking.
        /// </summary>
        IRequestParkingPolicy? RequestParkingPolicy { get; }
    }

    /// <summary>
    /// Selects custom requests for cooperative parking using a host-supplied predicate.
    /// </summary>
    public sealed class DelegateRequestParkingPolicy : IRequestParkingPolicy
    {
        /// <summary>
        /// Creates a policy whose predicate follows the admission-time contract of <see cref="IRequestParkingPolicy"/>.
        /// </summary>
        public DelegateRequestParkingPolicy(Func<IServiceRequest, bool> canPark)
        {
            m_canPark = canPark ?? throw new ArgumentNullException(nameof(canPark));
        }

        /// <inheritdoc/>
        public bool CanPark(IServiceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            return m_canPark(request);
        }

        private readonly Func<IServiceRequest, bool> m_canPark;
    }
}
