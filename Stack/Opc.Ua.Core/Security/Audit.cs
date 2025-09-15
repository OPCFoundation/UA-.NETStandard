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

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

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
        /// <param name="logger"></param>
        /// <param name="implementationInfo">Information about the secure channel implementation.</param>
        /// <param name="endpointUrl">The identifier assigned to the secure channel</param>
        /// <param name="secureChannelId">The identifier assigned to the secure channel</param>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="encodingSupport">The type of encoding supported by the channel.</param>
        public static void SecureChannelCreated(
            this ILogger logger,
            string implementationInfo,
            string endpointUrl,
            string secureChannelId,
            EndpointDescription endpoint,
            X509Certificate2 clientCertificate,
            X509Certificate2 serverCertificate,
            BinaryEncodingSupport encodingSupport)
        {
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

                logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "SECURE CHANNEL CREATED [{ImplementationInfo}] [ID={SecureChannelId}] Connected To: {EndpointUrl} [{SecurityMode}/{SecurityPolicyUri}/{Encoding}]",
                    implementationInfo,
                    secureChannelId,
                    endpointUrl,
                    endpoint.SecurityMode.ToString(),
                    SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri),
                    encoding);

                if (endpoint.SecurityMode != MessageSecurityMode.None)
                {
                    logger.LogCertificate(Utils.TraceMasks.Security, "Client Certificate: ", clientCertificate);
                    logger.LogCertificate(Utils.TraceMasks.Security, "Server Certificate: ", serverCertificate);
                }
            }
            else
            {
                logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "SECURE CHANNEL CREATED [{ImplementationInfo}] [ID={SecureChannelId}] Connected To: {EndpointUrl}",
                    implementationInfo,
                    secureChannelId,
                    endpointUrl);
            }
        }

        /// <summary>
        /// Called when a secure channel is renewed by the client.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="implementationInfo">Information about the secure channel implementation.</param>
        /// <param name="secureChannelId">The identifier assigned to the secure channel.</param>
        public static void SecureChannelRenewed(
            this ILogger logger,
            string implementationInfo,
            string secureChannelId)
        {
            logger.LogInformation(
                Utils.TraceMasks.Security,
                "SECURE CHANNEL RENEWED [{ImplementationInfo}] [ID={SecureChannelId}]",
                implementationInfo,
                secureChannelId);
        }
    }
}
