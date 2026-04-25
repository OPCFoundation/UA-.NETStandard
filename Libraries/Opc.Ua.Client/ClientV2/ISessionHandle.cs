#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua.Client.Sessions;
    using System;

    /// <summary>
    /// A reference to a session with a particular lifetime
    /// Basis of the fluent api surface the client exposes
    /// </summary>
    public interface ISessionHandle : IAsyncDisposable
    {
        /// <summary>
        /// The session
        /// </summary>
        Sessions.ISession Session { get; }
    }
}
#endif
