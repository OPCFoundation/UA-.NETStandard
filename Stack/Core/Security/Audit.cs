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
