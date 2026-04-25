// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using Opc.Ua;
    using Opc.Ua.Client.Nodes;
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
