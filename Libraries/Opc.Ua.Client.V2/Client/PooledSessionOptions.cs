// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua;
    using Opc.Ua.Client.Sessions;

    /// <summary>
    /// Pooled session options
    /// </summary>
    public record class PooledSessionOptions
    {
        /// <summary>
        /// Endpoint to use to connect to the server with.
        /// The endpoint contains the security settings to
        /// use when connecting. The client will try to
        /// match the settings to a supported endpoint returned
        /// by the discovery sequence.
        /// </summary>
        public EndpointDescription Endpoint { get; } = new();

        /// <summary>
        /// The user identity to use when connecting the server.
        /// The default is anonymous user. If elevation is
        /// needed, a new connection should be created or the
        /// session should be created directly.
        /// </summary>
        public IUserIdentity? User { get; init; }

        /// <summary>
        /// Session options
        /// </summary>
        public SessionOptions? SessionOptions { get; init; }

        /// <summary>
        /// Use reverse connect
        /// </summary>
        public bool UseReverseConnect { get; init; }
    }
}
