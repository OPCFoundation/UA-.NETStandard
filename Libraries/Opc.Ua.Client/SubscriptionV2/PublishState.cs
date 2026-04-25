#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using System;

    /// <summary>
    /// Flags indicating the publish state.
    /// </summary>
    [Flags]
    public enum PublishState
    {
        /// <summary>
        /// The publish state has not changed.
        /// </summary>
        None = 0,

        /// <summary>
        /// A keep alive message was received.
        /// </summary>
        KeepAlive = 1 << 1,

        /// <summary>
        /// A republish for a missing message was issued.
        /// </summary>
        Republish = 1 << 2,

        /// <summary>
        /// The publishing stopped.
        /// </summary>
        Stopped = 1 << 3,

        /// <summary>
        /// The publishing recovered.
        /// </summary>
        Recovered = 1 << 4,

        /// <summary>
        /// The subscription timed out on the
        /// server and was closed
        /// </summary>
        Timeout = 1 << 5,

        /// <summary>
        /// The Subscription was transferred
        /// to another session.
        /// </summary>
        Transferred = 1 << 6,

        /// <summary>
        /// Subscription closed on the client
        /// </summary>
        Completed = 1 << 7,
    }
}
#endif
