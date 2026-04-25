#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using Polly;
    using System;

    /// <summary>
    /// Client options
    /// </summary>
    public record class ClientOptions
    {
        /// <summary>
        /// Max number of sessions that can be held through the
        /// connection pool before a session is disposed.
        /// </summary>
        public int MaxPooledSessions { get; init; }
            = 100;

        /// <summary>
        /// The length of time a session should linger in the session
        /// pool without being used before it is disposed.
        /// </summary>
        public TimeSpan LingerTimeout { get; init; }
            = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Connect policy to use. The resiliency pipelines will
        /// be used to reconnect the session when the connection
        /// is determined lost. Use rate limiting to limit the
        /// number of reconnects across the entire client.
        /// </summary>
        public ResiliencePipeline? RetryStrategy { get; init; }

        /// <summary>
        /// Transport quotas
        /// </summary>
        public TransportQuotaOptions Quotas { get; init; }
            = new TransportQuotaOptions();

        /// <summary>
        /// Transport quotas
        /// </summary>
        public SecurityOptions Security { get; init; }
            = new SecurityOptions();

        /// <summary>
        /// Reverse connect port to use. The default reverse connect
        /// port is 4840. If the reverse connect port is set to null,
        /// Reverse connect will not be used.
        /// </summary>
        public int? ReverseConnectPort { get; init; }

        /// <summary>
        /// Eanble opc ua stack logging
        /// </summary>
        public LogLevel? StackLoggingLevel { get; init; }
    }
}
#endif
