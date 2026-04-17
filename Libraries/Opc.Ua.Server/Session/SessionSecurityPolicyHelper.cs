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

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Encapsulates session service security-policy specific processing.
    /// </summary>
    internal static class SessionSecurityPolicyHelper
    {
        /// <summary>
        /// Creates the signature returned by CreateSession.
        /// </summary>
        public static SignatureData CreateServerSignature(
            OperationContext context,
            Certificate instanceCertificate,
            Certificate parsedClientCertificate,
            ByteString clientNonce,
            ByteString serverNonce)
        {
            if (parsedClientCertificate == null || clientNonce.IsEmpty)
            {
                return null;
            }

            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(context.SecurityPolicyUri);

            byte[] dataToSign = securityPolicy.GetServerSignatureData(
                context.ChannelContext.ChannelThumbprint,
                clientNonce.ToArray(),
                context.ChannelContext.ServerChannelCertificate,
                parsedClientCertificate.RawData,
                context.ChannelContext.ClientChannelCertificate,
                serverNonce.ToArray());

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
            if (parameters == null)
            {
                return null;
            }

            var responseParameters = new List<KeyValuePair>();
            foreach (KeyValuePair parameter in parameters.Parameters)
            {
                if (parameter.Key != AdditionalParameterNames.ECDHPolicyUri ||
                    !parameter.Value.TryGet(out string policyUri))
                {
                    responseParameters.Add(parameter);
                    continue;
                }

                logger.LogDebug(
                    "Received request for new EphmeralKey using {SecurityPolicyUri}.",
                    policyUri);

                SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(policyUri);

                if (securityPolicy != null &&
                    securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                {
                    session.SetUserTokenSecurityPolicy(policyUri);
                    EphemeralKeyType key = session.GetNewEphemeralKey();
                    responseParameters.Add(new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = new ExtensionObject(key)
                    });
                    continue;
                }

                logger.LogWarning(
                    "Rejecting request for new EphemeralKey using {SecurityPolicyUri}.",
                    policyUri);

                responseParameters.Add(new KeyValuePair
                {
                    Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                    Value = StatusCodes.BadSecurityPolicyRejected
                });
            }
            return new AdditionalParametersType
            {
                Parameters = responseParameters
            };
        }

        /// <summary>
        /// Processes additional request parameters during ActivateSession.
        /// </summary>
        public static AdditionalParametersType ProcessActivateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters)
        {
            EphemeralKeyType key = session.GetNewEphemeralKey();

            if (key == null)
            {
                return parameters;
            }

            return new AdditionalParametersType
            {
                Parameters = parameters.Parameters.AddItem(new KeyValuePair
                {
                    Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                    Value = new ExtensionObject(key)
                })
            };
        }
    }
}
