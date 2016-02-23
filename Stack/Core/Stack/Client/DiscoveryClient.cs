/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.ServiceModel;
using System.ServiceModel.Channels;

using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// An object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryClient
    {
        #region Constructors
        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <returns></returns>
        public static DiscoveryClient Create(Uri discoveryUrl)
        {
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            ITransportChannel channel = DiscoveryChannel.Create(discoveryUrl, configuration, new ServiceMessageContext());
            return new DiscoveryClient(channel);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        public static DiscoveryClient Create(Uri discoveryUrl, EndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                configuration = EndpointConfiguration.Create();
            }

            ITransportChannel channel = DiscoveryChannel.Create(discoveryUrl, configuration, new ServiceMessageContext());
            return new DiscoveryClient(channel);
        }

        #endregion
        
        #region Public Methods
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        /// <returns></returns>
        public virtual EndpointDescriptionCollection GetEndpoints(StringCollection profileUris)
        {
            EndpointDescriptionCollection endpoints = null;

            GetEndpoints(
                null,
                this.Endpoint.EndpointUrl,
                null,
                profileUris,
                out endpoints);

            return endpoints;
        }

        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        /// <returns></returns>
        public virtual ApplicationDescriptionCollection FindServers(StringCollection serverUris)
        {
            ApplicationDescriptionCollection servers = null;

            FindServers(
                null,
                this.Endpoint.EndpointUrl,
                null,
                serverUris,
                out servers);

            return servers;
        }
        #endregion  
    }
    
    /// <summary>
    /// A channel object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryChannel
    {
        #region Constructors
        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="discoveryUrl">The discovery url.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ServiceMessageContext messageContext)
        {
            // create a dummy description.
            EndpointDescription endpoint = new EndpointDescription();

            endpoint.EndpointUrl = discoveryUrl.ToString();
            endpoint.SecurityMode = MessageSecurityMode.None;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            ITransportChannel channel = CreateUaBinaryChannel(
                null,
                endpoint,
                endpointConfiguration,
                (System.Security.Cryptography.X509Certificates.X509Certificate2)null,
                messageContext);

            return channel;
        }

        #endregion
    } 
}
