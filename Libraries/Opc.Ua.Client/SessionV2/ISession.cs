#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client.Sessions
{
    using Opc.Ua;
    using Opc.Ua.Client.Services;
    using Opc.Ua.Client.Subscriptions;
    using System;

    /// <summary>
    /// Session interface
    /// </summary>
    public interface ISession
    {
        /// <summary>
        /// Returns the current connection state of
        /// the session.
        /// </summary>
        bool Connected { get; }

        /// <summary>
        /// The endpoint the session is connected to.
        /// </summary>
        EndpointDescription? Endpoint { get; }

        /// <summary>
        /// The current user identity of the session
        /// </summary>
        IUserIdentity Identity { get; }

        /// <summary>
        /// Cache of the server address space.
        /// </summary>
        INodeCache NodeCache { get; }

        /// <summary>
        /// Call methods
        /// </summary>
        IMethodServiceSet MethodServiceSet { get; }

        /// <summary>
        /// View services
        /// </summary>
        IViewServiceSet ViewServiceSet { get; }

        /// <summary>
        /// Attribute services
        /// </summary>
        IAttributeServiceSet AttributeServiceSet { get; }

        /// <summary>
        /// Node management services
        /// </summary>
        INodeManagementServiceSet NodeManagementServiceSet { get; }

        /// <summary>
        /// Subscriptions in the session
        /// </summary>
        ISubscriptionManager Subscriptions { get; }

        /// <summary>
        /// Encoder context providing limits and access to type
        /// namespace and server tables
        /// </summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Operation limits for this session
        /// </summary>
        Limits OperationLimits { get; }

        /// <summary>
        /// System context (legacy)
        /// </summary>
        [Obsolete("Use MessageContext instead.")]
        ISystemContext SystemContext { get; }
    }
}
#endif
