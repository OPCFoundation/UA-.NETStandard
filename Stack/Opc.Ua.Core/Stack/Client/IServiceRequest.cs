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

namespace Opc.Ua
{   
    /// <summary>
	/// An interface to a service request.
	/// </summary>
    public interface IServiceRequest : IEncodeable
    {
        /// <summary>
        /// The header for the request.
        /// </summary>
        /// <value>The request header.</value>
        RequestHeader RequestHeader { get; set; }
    }
    
    /// <summary>
	/// An interface to a service response.
	/// </summary>
    public interface IServiceResponse : IEncodeable 
    {
        /// <summary>
        /// The header for the response.
        /// </summary>
        /// <value>The response header.</value>
        ResponseHeader ResponseHeader { get; }
    }
    
    /// <summary>
	/// An interface to a service message.
	/// </summary>
    public interface IServiceMessage
    {
        /// <summary>
        /// Returns the request contained in the message.
        /// </summary>
        /// <returns></returns>
        IServiceRequest GetRequest();
    
        /// <summary>
        /// Creates an instance of a response message.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns></returns>
        object CreateResponse(IServiceResponse response);
    }
}
