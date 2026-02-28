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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Encapsulates session service security-policy specific processing.
    /// </summary>
    internal static class SessionSecurityPolicyHelper
    {
        /// <summary>
        /// Decodes additional request parameters from an additional header.
        /// </summary>
        public static AdditionalParametersType DecodeAdditionalParameters(
            ExtensionObject additionalHeader)
        {
            return ExtensionObject.ToEncodeable(additionalHeader) as AdditionalParametersType;
        }

        /// <summary>
        /// Creates the signature returned by CreateSession.
        /// </summary>
        public static SignatureData CreateServerSignature(
            OperationContext context,
            X509Certificate2 instanceCertificate,
            X509Certificate2 parsedClientCertificate,
            byte[] clientNonce,
            byte[] serverNonce)
        {
            if (parsedClientCertificate == null || clientNonce == null)
            {
                return null;
            }

            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(context.SecurityPolicyUri);

            byte[] dataToSign = securityPolicy.GetServerSignatureData(
                context.ChannelContext.ChannelThumbprint,
                clientNonce,
                context.ChannelContext.ServerChannelCertificate,
                parsedClientCertificate.RawData,
                context.ChannelContext.ClientChannelCertificate,
                serverNonce);

            return SecurityPolicies.CreateSignatureData(
                context.SecurityPolicyUri,
                instanceCertificate,
                dataToSign);
        }

        /// <summary>
        /// Processes additional request parameters during CreateSession.
        /// </summary>
        public static AdditionalParametersType ProcessCreateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters,
            ILogger logger)
        {
            AdditionalParametersType response = null;

            if (parameters != null && parameters.Parameters != null)
            {
                response = new AdditionalParametersType();

                foreach (KeyValuePair ii in parameters.Parameters)
                {
                    if (ii.Key == AdditionalParameterNames.ECDHPolicyUri)
                    {
                        string policyUri = ii.Value.ToString();
                        logger.LogWarning("Received request for new EphmeralKey using {SecurityPolicyUri}.", policyUri);

                        SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(policyUri);

                        if (securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                        {
                            session.SetUserTokenSecurityPolicy(policyUri);
                            EphemeralKeyType key = session.GetNewEphemeralKey();
                            response.Parameters.Add(
                                new KeyValuePair
                                {
                                    Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                                    Value = new ExtensionObject(key)
                                });

                            logger.LogWarning("Returning new EphemeralKey: {PublicKey} bytes.", key.PublicKey?.Length ?? 0);
                        }
                        else
                        {
                            response.Parameters.Add(
                                new KeyValuePair
                                {
                                    Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                                    Value = StatusCodes.BadSecurityPolicyRejected
                                });

                            logger.LogWarning("Rejecting request for new EphemeralKey using {SecurityPolicyUri}.", policyUri);
                        }
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// Processes additional request parameters during ActivateSession.
        /// </summary>
        public static AdditionalParametersType ProcessActivateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters,
            ILogger logger)
        {
            AdditionalParametersType response = null;
            EphemeralKeyType key = session.GetNewEphemeralKey();

            if (key != null)
            {
                response = new AdditionalParametersType();
                response.Parameters.Add(
                    new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = new ExtensionObject(key)
                    });

                logger.LogWarning("Returning new EphemeralKey: {PublicKey} bytes.", key.PublicKey?.Length ?? 0);
            }

            return response;
        }
    }
}
