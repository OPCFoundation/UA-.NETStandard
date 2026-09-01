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
using Opc.Ua.Client;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// The request a session factory answers: the endpoint the compiled form
    /// targets and the security floor WoT Binding Section 5.7.1 puts on
    /// selecting an endpoint at it.
    /// </summary>
    /// <remarks>
    /// The floor is handed to the factory rather than applied behind it because
    /// only the factory calls <c>GetEndpoints</c>: it is the one place that can
    /// discard an endpoint before a channel is opened to it.
    /// <see cref="OpcUaWotEndpointSelector"/> applies the clause's rules to a
    /// response, so a factory does not have to restate them, and the executor
    /// verifies the endpoint the returned session actually reports either way.
    /// </remarks>
    public sealed class OpcUaWotSessionRequest
    {
        /// <summary>
        /// Initializes a new session request.
        /// </summary>
        /// <param name="endpointUrl">The endpoint the form targets.</param>
        /// <param name="minimumSecurity">
        /// The security floor the document states, or <c>null</c> when it
        /// constrains nothing.
        /// </param>
        /// <param name="affordanceName">The affordance the session serves.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpointUrl"/> is <c>null</c>.
        /// </exception>
        public OpcUaWotSessionRequest(
            string endpointUrl,
            WotSecurityFloor? minimumSecurity = null,
            string? affordanceName = null)
        {
            EndpointUrl = endpointUrl ?? throw new ArgumentNullException(nameof(endpointUrl));
            MinimumSecurity = minimumSecurity;
            AffordanceName = affordanceName;
        }

        /// <summary>
        /// Gets the endpoint the compiled form targets.
        /// </summary>
        public string EndpointUrl { get; }

        /// <summary>
        /// Gets the security floor the document states for an <c>auto</c>
        /// endpoint selection, or <c>null</c> when it constrains nothing.
        /// </summary>
        public WotSecurityFloor? MinimumSecurity { get; }

        /// <summary>
        /// Gets the name of the affordance the session serves, for logging and
        /// diagnostics.
        /// </summary>
        public string? AffordanceName { get; }
    }

    /// <summary>
    /// Options for the OPC UA WoT binding executor. The session factory connects a
    /// client session to the target endpoint and is injectable so callers control
    /// the application configuration, security and identity.
    /// </summary>
    public sealed class OpcUaWotBindingOptions
    {
        /// <summary>
        /// Gets or sets the factory that connects an <see cref="ISession"/> to the
        /// supplied <c>opc.tcp</c> endpoint. It is required for execution unless
        /// <see cref="ConstrainedSessionFactory"/> is configured.
        /// </summary>
        /// <remarks>
        /// The delegate learns the endpoint URL and nothing else, so it cannot
        /// honour the <c>uav:minimumSecurity</c> floor of WoT Binding
        /// Section 5.7.1 on its own. The executor still enforces the floor
        /// against the endpoint the returned session reports, and fails closed
        /// when the session is below it; a caller whose factory should apply
        /// the floor while choosing an endpoint sets
        /// <see cref="ConstrainedSessionFactory"/> instead.
        /// </remarks>
        public Func<string, CancellationToken, ValueTask<ISession>>? SessionFactory { get; set; }

        /// <summary>
        /// The delegate that connects an <see cref="ISession"/> for a request
        /// carrying the security floor of WoT Binding Section 5.7.1.
        /// </summary>
        /// <param name="request">The endpoint and floor the form states.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The connected session.</returns>
        public delegate ValueTask<ISession> ConstrainedSessionFactoryDelegate(
            OpcUaWotSessionRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Gets or sets the factory that connects an <see cref="ISession"/> for a
        /// request carrying the security floor of WoT Binding Section 5.7.1. It
        /// takes precedence over <see cref="SessionFactory"/>.
        /// </summary>
        public ConstrainedSessionFactoryDelegate? ConstrainedSessionFactory { get; set; }

        /// <summary>
        /// Gets or sets whether the executor disposes the session when the channel
        /// is disposed. Set to <c>false</c> when a shared, caller-owned session is
        /// returned by the factory.
        /// </summary>
        public bool DisposeSession { get; set; } = true;

        /// <summary>
        /// Gets or sets the sampling / publishing interval used for observe and
        /// event subscriptions. Observe and event notifications are delivered by
        /// a native OPC UA <see cref="Subscription"/> / <see cref="MonitoredItem"/>
        /// pair (Part 4 §5.13 / §5.12); this bounds how fast the server samples
        /// and publishes, not a client-side poll.
        /// </summary>
        public TimeSpan ObserveInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the bounded monitored-item queue size requested for
        /// event subscriptions, so a burst of events cannot grow the server-side
        /// queue without bound. Property observe monitored items always request
        /// a queue size of 1 (only the latest value is relevant).
        /// </summary>
        public uint EventQueueSize { get; set; } = 10;
    }
}
