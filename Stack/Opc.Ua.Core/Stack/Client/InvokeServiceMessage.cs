/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

namespace Opc.Ua
{
    /// <summary>
    /// The message contract for the InvokeService service.
    /// </summary>
    public class InvokeServiceMessage
    {
        /// <summary>
        /// The body of the message.
        /// </summary>
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
        /// <param name="invokeServiceRequest">The invoke service request.</param>
        public InvokeServiceMessage(byte[] invokeServiceRequest)
        {
            InvokeServiceRequest = invokeServiceRequest;
        }
    }

    /// <summary>
    /// The message contract for the InvokeService service response.
    /// </summary>
    public class InvokeServiceResponseMessage
    {
        /// <summary>
        /// The body of the message.
        /// </summary>
        public byte[] InvokeServiceResponse { get; set; }

        /// <summary>
        /// Initializes an empty message.
        /// </summary>
        public InvokeServiceResponseMessage()
        {
        }

        /// <summary>
        /// Initializes the message with the body.
        /// </summary>
        /// <param name="invokeServiceResponse">The invoke service response.</param>
        public InvokeServiceResponseMessage(byte[] invokeServiceResponse)
        {
            InvokeServiceResponse = invokeServiceResponse;
        }
    }
}
