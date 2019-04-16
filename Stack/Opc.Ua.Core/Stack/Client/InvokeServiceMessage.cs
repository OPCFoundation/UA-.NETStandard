/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
