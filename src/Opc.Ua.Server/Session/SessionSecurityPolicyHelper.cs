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
        public static SignatureData? CreateServerSignature(
            OperationContext context,
            Certificate instanceCertificate,
            Certificate? parsedClientCertificate,
            ByteString clientNonce,
            ByteString serverNonce,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            if (parsedClientCertificate == null || clientNonce.IsEmpty)
            {
                return null;
            }

            ISecurityPolicyRegistry policies = securityPolicies ?? SecurityPolicies.Default;
            SecurityPolicyInfo? securityPolicy = policies.GetInfo(context.SecurityPolicyUri);

            // CreateServerSignature is invoked only when a secure channel is bound.
            SecureChannelContext channelContext = context.ChannelContext!;

            byte[] dataToSign = securityPolicy!.GetServerSignatureData(
                channelContext.ChannelThumbprint,
                clientNonce.ToArray(),
                channelContext.ServerChannelCertificate,
                parsedClientCertificate.RawData,
                channelContext.ClientChannelCertificate,
                serverNonce.ToArray());

            return policies.CreateSignatureData(
                context.SecurityPolicyUri,
                instanceCertificate,
                dataToSign);
        }

        /// <summary>
        /// Processes additional request parameters during CreateSession.
        /// </summary>
        public static AdditionalParametersType? ProcessCreateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters,
            ILogger logger,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            if (parameters == null)
            {
                return null;
            }

            var responseParameters = new List<KeyValuePair>();
            foreach (KeyValuePair parameter in parameters.Parameters)
            {
                if (parameter.Key != AdditionalParameterNames.ECDHPolicyUri ||
                    !parameter.Value.TryGetValue(out string policyUri))
                {
                    responseParameters.Add(parameter);
                    continue;
                }

                logger.ReceivedRequestForNewEphmeralKeyUsingSecurityPolicyUri(policyUri);

                SecurityPolicyInfo? securityPolicy = (securityPolicies ?? SecurityPolicies.Default)
                    .GetInfo(policyUri);

                if (securityPolicy != null &&
                    securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                {
                    session.SetUserTokenSecurityPolicy(policyUri);
                    EphemeralKeyType? key = session.GetNewEphemeralKey();
                    responseParameters.Add(new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = new ExtensionObject(key!)
                    });
                    continue;
                }

                logger.RejectingRequestForNewEphemeralKeyUsingSecurityPolicyUri(policyUri);

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
        /// <remarks>
        /// OPC 10000-6 6.8.2: a valid ECDHPolicyUri in the request selects the
        /// policy of the returned EphemeralKey, an unsupported one is answered with
        /// Bad_SecurityPolicyRejected; without one the Server returns a new key for
        /// the policy already in use, since the previous key cannot be accepted
        /// again. The response carries only the parameters the Server produces; the
        /// request parameters are not echoed back.
        /// </remarks>
        public static AdditionalParametersType ProcessActivateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType? parameters,
            ILogger? logger = null,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            var responseParameters = new List<KeyValuePair>();
            bool policyRequested = false;
            if (parameters != null)
            {
                foreach (KeyValuePair parameter in parameters.Parameters)
                {
                    if (parameter.Key != AdditionalParameterNames.ECDHPolicyUri ||
                        !parameter.Value.TryGetValue(out string policyUri) ||
                        policyRequested)
                    {
                        continue;
                    }

                    policyRequested = true;
                    logger?.ReceivedRequestForNewEphmeralKeyUsingSecurityPolicyUri(policyUri);

                    SecurityPolicyInfo? securityPolicy = (securityPolicies ?? SecurityPolicies.Default)
                        .GetInfo(policyUri);

                    if (securityPolicy != null &&
                        securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                    {
                        session.SetUserTokenSecurityPolicy(policyUri);
                        continue;
                    }

                    logger?.RejectingRequestForNewEphemeralKeyUsingSecurityPolicyUri(policyUri);
                    responseParameters.Add(new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = StatusCodes.BadSecurityPolicyRejected
                    });
                }
            }

            if (responseParameters.Count == 0)
            {
                EphemeralKeyType? key = session.GetNewEphemeralKey();
                if (key != null)
                {
                    responseParameters.Add(new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = new ExtensionObject(key)
                    });
                }
            }

            return new AdditionalParametersType
            {
                Parameters = responseParameters
            };
        }
    }

    /// <summary>
    /// Source-generated log messages for SessionSecurityPolicyHelper.
    /// </summary>
    internal static partial class SessionSecurityPolicyHelperLog
    {
        [LoggerMessage(EventId = ServerEventIds.SessionSecurityPolicyHelper + 0, Level = LogLevel.Debug,
            Message = "Received request for new EphmeralKey using {SecurityPolicyUri}.")]
        public static partial void ReceivedRequestForNewEphmeralKeyUsingSecurityPolicyUri(
            this ILogger logger,
            string securityPolicyUri);

        [LoggerMessage(EventId = ServerEventIds.SessionSecurityPolicyHelper + 1, Level = LogLevel.Warning,
            Message = "Rejecting request for new EphemeralKey using {SecurityPolicyUri}.")]
        public static partial void RejectingRequestForNewEphemeralKeyUsingSecurityPolicyUri(
            this ILogger logger,
            string securityPolicyUri);
    }

}
