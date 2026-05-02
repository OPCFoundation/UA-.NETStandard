/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for creating a <see cref="ManagedSession"/>. Provided as a
    /// record so it can be used with the .NET options pattern
    /// (<see cref="Microsoft.Extensions.Options.IOptions{T}"/>) and bound
    /// from configuration.
    /// </summary>
    public sealed record ManagedSessionOptions
    {
        /// <summary>
        /// The configured endpoint to connect to. Required.
        /// </summary>
        public ConfiguredEndpoint? Endpoint { get; init; }

        /// <summary>
        /// Optional user identity. If null, the session uses anonymous.
        /// </summary>
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Session display name.
        /// </summary>
        public string SessionName { get; init; } = "ManagedSession";

        /// <summary>
        /// Requested session timeout. Defaults to 60 seconds.
        /// </summary>
        public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Preferred locale identifiers (e.g. "en-US"). May be null.
        /// </summary>
        public IReadOnlyList<string>? PreferredLocales { get; init; }

        /// <summary>
        /// Whether to validate the domain in the server certificate.
        /// </summary>
        public bool CheckDomain { get; init; }

        /// <summary>
        /// Reconnect policy configuration.
        /// </summary>
        public ReconnectPolicyOptions ReconnectPolicy { get; init; } = new();

        /// <summary>
        /// When set, server-side redundancy failover is enabled using a
        /// default <see cref="DefaultServerRedundancyHandler"/>. To use a
        /// custom handler, pass it explicitly to
        /// <see cref="ManagedSession.CreateAsync(ApplicationConfiguration, ConfiguredEndpoint, ISessionFactory, IUserIdentity?, IReconnectPolicy?, IServerRedundancyHandler?, ITelemetryContext?, string, uint, ArrayOf{string}, bool, ISubscriptionEngineFactory?, System.Threading.CancellationToken)"/>.
        /// </summary>
        public bool EnableServerRedundancy { get; init; }

        /// <summary>
        /// Optional subscription engine factory. When null, defaults to the
        /// V2 engine (<see cref="DefaultSubscriptionEngineFactory"/>) so
        /// <see cref="ManagedSession.SubscriptionManager"/> is available.
        /// </summary>
        public ISubscriptionEngineFactory? SubscriptionEngineFactory { get; init; }
    }
}
