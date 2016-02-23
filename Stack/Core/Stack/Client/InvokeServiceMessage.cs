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

using System.ServiceModel;

namespace Opc.Ua
{
    /// <summary>
    /// The message contract for the InvokeService service.
    /// </summary>
    [MessageContract(IsWrapped=false)]
    public class InvokeServiceMessage
    {    
        /// <summary>
        /// The body of the message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1051:DoNotDeclareVisibleInstanceFields")]
        [MessageBodyMember(Namespace=Namespaces.OpcUaXsd, Order=0)]
        public byte[] InvokeServiceRequest;
        
        /// <summary>
        /// Initializes an empty message.
        /// </summary>    
        public InvokeServiceMessage()
        {
        }
            
        /// <summary>
        /// Initializes the message with the body.
        /// </summary>
        /// <param name="InvokeServiceRequest">The invoke service request.</param>
        public InvokeServiceMessage(byte[] InvokeServiceRequest)
        {
            this.InvokeServiceRequest = InvokeServiceRequest;
        }
    }

    /// <summary>
    /// The message contract for the InvokeService service response.
    /// </summary>
    [MessageContract(IsWrapped=false)]
    public class InvokeServiceResponseMessage
    {    
        /// <summary>
        /// The body of the message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1051:DoNotDeclareVisibleInstanceFields")]
        [MessageBodyMember(Namespace=Namespaces.OpcUaXsd, Order=0)]
        public byte[] InvokeServiceResponse;
        
        /// <summary>
        /// Initializes an empty message.
        /// </summary>
        public InvokeServiceResponseMessage()
        {
        }
            
        /// <summary>
        /// Initializes the message with the body.
        /// </summary>
        /// <param name="InvokeServiceResponse">The invoke service response.</param>
        public InvokeServiceResponseMessage(byte[] InvokeServiceResponse)
        {
            this.InvokeServiceResponse = InvokeServiceResponse;
        }
    }
}
