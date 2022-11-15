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

using System.Text;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security
{
    /// <summary>
    /// A class which is used to report events which have security implications.
    /// </summary>
    public static class Audit
    {
        /// <summary>
        /// Called when a secure channel is created by the client.
        /// </summary>
        /// <param name="implementationInfo">Information about the secure channel implementation.</param>
        /// <param name="endpointUrl">The identifier assigned to the secure channel</param>
        /// <param name="secureChannelId">The identifier assigned to the secure channel</param>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="encodingSupport">The type of encoding supported by the channel.</param>
        public static void SecureChannelCreated(
            string implementationInfo,
            string endpointUrl,
            string secureChannelId,
            EndpointDescription endpoint,
            X509Certificate2 clientCertificate,
            X509Certificate2 serverCertificate,
            BinaryEncodingSupport encodingSupport)
        {
            // do nothing if security turned off.
            if ((Utils.TraceMask & Utils.TraceMasks.Security) == 0)
            {
                return;
            }

            if (endpoint != null)
            {
                string encoding;
                if (encodingSupport == BinaryEncodingSupport.Required)
                {
                    encoding = "Binary";
                }
                else if (encodingSupport == BinaryEncodingSupport.None)
                {
                    encoding = "Xml";
                }
                else
                {
                    encoding = "BinaryOrXml";
                }

                Utils.LogInfo("SECURE CHANNEL CREATED [{0}] [ID={1}] Connected To: {2} [{3}/{4}/{5}]",
                    implementationInfo, secureChannelId, endpointUrl,
                    endpoint.SecurityMode.ToString(), SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri), encoding);

                if (endpoint.SecurityMode != MessageSecurityMode.None)
                {
                    Utils.LogCertificate("Client Certificate: ", clientCertificate);
                    Utils.LogCertificate("Server Certificate: ", serverCertificate);
                }
            }
            else
            {
                Utils.LogInfo("SECURE CHANNEL CREATED [{0}] [ID={1}] Connected To: {2}", implementationInfo, secureChannelId, endpointUrl);
            }
        }


        /// <summary>
        /// Called when a secure channel is renewed by the client.
        /// </summary>
        /// <param name="implementationInfo">Information about the secure channel implementation.</param>
        /// <param name="secureChannelId">The identifier assigned to the secure channel.</param>
        public static void SecureChannelRenewed(
            string implementationInfo,
            string secureChannelId)
        {
            // do nothing if security turned off.
            if ((Utils.TraceMask & Utils.TraceMasks.Security) == 0)
            {
                return;
            }

            Utils.LogInfo("SECURE CHANNEL RENEWED [{0}] [ID={1}]", implementationInfo, secureChannelId);
        }
    }
}
