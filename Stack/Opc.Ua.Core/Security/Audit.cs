/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        /// <param name="logger">A contextual logger to log to</param>
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
                    logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Client Certificate: {Certificate}",
                        clientCertificate.AsLogSafeString());
                    logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Server Certificate: {Certificate}",
                        serverCertificate.AsLogSafeString());
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
        /// <param name="logger">A contextual logger to log to</param>
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
