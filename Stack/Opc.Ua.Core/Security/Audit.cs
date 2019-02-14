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

            StringBuilder buffer = new StringBuilder();

            buffer.Append("SECURE CHANNEL CREATED");
            buffer.Append(" [");
            buffer.Append(implementationInfo);
            buffer.Append("]");
            buffer.Append(" [ID=");
            buffer.Append(secureChannelId);
            buffer.Append("]");
            buffer.Append(" Connected To: ");
            buffer.Append(endpointUrl);

            if (endpoint != null)
            {
                buffer.Append(" [");
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", endpoint.SecurityMode);
                buffer.Append("/");
                buffer.Append(SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri));
                buffer.Append("/");

                if (encodingSupport == BinaryEncodingSupport.Required)
                {
                    buffer.Append("Binary");
                }
                else if (encodingSupport == BinaryEncodingSupport.None)
                {
                    buffer.Append("Xml");
                }
                else
                {
                    buffer.Append("BinaryOrXml");
                }

                buffer.Append("]");

                if (endpoint.SecurityMode != MessageSecurityMode.None)
                {
                    if (clientCertificate != null)
                    {
                        buffer.Append(" Client Certificate: [");
                        buffer.Append(clientCertificate.Subject);
                        buffer.Append("] [");
                        buffer.Append(clientCertificate.Thumbprint);
                        buffer.Append("]");
                    }

                    if (serverCertificate != null)
                    {
                        buffer.Append(" Server Certificate: [");
                        buffer.Append(serverCertificate.Subject);
                        buffer.Append("] [");
                        buffer.Append(serverCertificate.Thumbprint);
                        buffer.Append("]");
                    }
                }
            }

            Utils.Trace(Utils.TraceMasks.Security, buffer.ToString());
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

            StringBuilder buffer = new StringBuilder();

            buffer.Append("SECURE CHANNEL RENEWED");
            buffer.Append(" [");
            buffer.Append(implementationInfo);
            buffer.Append("]");
            buffer.Append(" [ID=");
            buffer.Append(secureChannelId);
            buffer.Append("]");

            Utils.Trace(Utils.TraceMasks.Security, buffer.ToString());
        }
    }
}
