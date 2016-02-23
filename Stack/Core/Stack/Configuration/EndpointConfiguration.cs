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
using System.Collections.Generic;
using System.ServiceModel.Channels;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public partial class EndpointConfiguration
    {
        #region Constructors
        /// <summary>
        /// Creates an instance of a configuration with reasonable default values.
        /// </summary>
        public static EndpointConfiguration Create()
        {
            EndpointConfiguration configuration = new EndpointConfiguration();

            configuration.OperationTimeout      = 120000;
            configuration.UseBinaryEncoding     = true;
            configuration.MaxArrayLength        = UInt16.MaxValue;
            configuration.MaxByteStringLength   = UInt16.MaxValue*16;
            configuration.MaxMessageSize        = UInt16.MaxValue*64;
            configuration.MaxStringLength       = UInt16.MaxValue;
            configuration.MaxBufferSize         = UInt16.MaxValue;
            configuration.ChannelLifetime       = 120000;
            configuration.SecurityTokenLifetime = 3600000;

            return configuration;
        }

        /// <summary>
        /// Creates an instance of a configuration with reasonable default values.
        /// </summary>
        public static EndpointConfiguration Create(ApplicationConfiguration applicationConfiguration)
        {
            if (applicationConfiguration == null || applicationConfiguration.TransportQuotas == null)
            {
                return Create();
            }

            EndpointConfiguration configuration = new EndpointConfiguration();
            
            configuration.OperationTimeout      = applicationConfiguration.TransportQuotas.OperationTimeout;
            configuration.UseBinaryEncoding     = true;
            configuration.MaxArrayLength        = applicationConfiguration.TransportQuotas.MaxArrayLength;
            configuration.MaxByteStringLength   = applicationConfiguration.TransportQuotas.MaxByteStringLength;
            configuration.MaxMessageSize        = applicationConfiguration.TransportQuotas.MaxMessageSize;
            configuration.MaxStringLength       = applicationConfiguration.TransportQuotas.MaxStringLength;
            configuration.MaxBufferSize         = applicationConfiguration.TransportQuotas.MaxBufferSize;
            configuration.ChannelLifetime       = applicationConfiguration.TransportQuotas.ChannelLifetime;
            configuration.SecurityTokenLifetime = applicationConfiguration.TransportQuotas.SecurityTokenLifetime; 

            return configuration;
        }
        #endregion
    }
}
