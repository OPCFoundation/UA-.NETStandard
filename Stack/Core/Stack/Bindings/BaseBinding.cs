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
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The binding for the UA native stack
    /// </summary>
    public abstract class BaseBinding : Binding
    {
        #region Constructors
        /// <summary>
        /// Initializes the binding.
        /// </summary>
        protected BaseBinding(
            NamespaceTable        namespaceUris,
            EncodeableFactory     factory,
            EndpointConfiguration configuration)
        {
            m_messageContext = new ServiceMessageContext();
            
            m_messageContext.MaxStringLength     = configuration.MaxStringLength;
            m_messageContext.MaxByteStringLength = configuration.MaxByteStringLength;
            m_messageContext.MaxArrayLength      = configuration.MaxArrayLength;
            m_messageContext.MaxMessageSize      = configuration.MaxMessageSize;
            m_messageContext.Factory             = factory;
            m_messageContext.NamespaceUris       = namespaceUris;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The message context to use with the binding.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get { return m_messageContext; }
            set { m_messageContext = value; }
        }
        #endregion

        #region Private Fields
        private ServiceMessageContext m_messageContext;
        #endregion
    }
}
