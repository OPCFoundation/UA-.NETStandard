// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
