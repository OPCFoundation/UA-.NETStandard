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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                channelContext.ChannelThumbprint.ToArrayOrNull(),
                clientNonce.ToArray(),
                channelContext.ServerChannelCertificate.ToArrayOrNull(),
                parsedClientCertificate.RawData,
                channelContext.ClientChannelCertificate.ToArrayOrNull(),
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
                    securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None &&
                    TrySwitchEphemeralKeyPolicy(session, policyUri, out EphemeralKeyType? key))
                {
                    responseParameters.Add(new KeyValuePair
                    {
                        Key = QualifiedName.From(AdditionalParameterNames.ECDHKey),
                        Value = new ExtensionObject(key)
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
        /// Bad_SecurityPolicyRejected. Without one the Server returns a new key for
        /// the policy already in use only when the previous key was used in the
        /// request (it cannot be accepted again); otherwise it returns nothing and
        /// retains the previous key. The response carries only the parameters the
        /// Server produces; the request parameters are not echoed back.
        /// <para>
        /// A policy that is registered but cannot be served with the session's server
        /// certificate is rejected like an unknown one, before the working key is
        /// changed. When the request is rejected and the activation replaced a key the
        /// identity token used, the replacement stays installed and pending: the
        /// response carries only Bad_SecurityPolicyRejected for the ECDHKey, as 6.8.2
        /// requires, and the pending key is returned by the next ActivateSession that
        /// does not request another policy (from the Client's view the key it last
        /// received was used and not yet replaced).
        /// </para>
        /// </remarks>
        public static AdditionalParametersType ProcessActivateSessionAdditionalParameters(
            ISession session,
            AdditionalParametersType? parameters,
            ILogger? logger = null,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            var responseParameters = new List<KeyValuePair>();
            bool policyRequested = false;
            bool policyAccepted = false;
            EphemeralKeyType? acceptedKey = null;
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
                        securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None &&
                        TrySwitchEphemeralKeyPolicy(session, policyUri, out acceptedKey))
                    {
                        policyAccepted = true;
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

            if (!policyRequested || policyAccepted)
            {
                // The key the activation installed for a used key; taken here so a
                // later activation cannot return it. A rejected policy request leaves
                // it pending (see remarks).
                EphemeralKeyType? unsentKey = (session as Session)?.TakeUnsentEphemeralKey();

                EphemeralKeyType? key;
                if (policyAccepted)
                {
                    key = acceptedKey;
                }
                else if (session is Session)
                {
                    key = unsentKey;
                }
                else
                {
                    // a session implementation without key-use tracking: hand out a
                    // new key so a used one is always replaced.
                    key = session.GetNewEphemeralKey();
                }

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

        /// <summary>
        /// Switches the session's EphemeralKey to the requested policy and creates a
        /// key for it. A policy the session's server certificate cannot sign for (e.g.
        /// an ECC policy on an RSA certificate session) is detected before the working
        /// key is discarded and reported as not accepted.
        /// </summary>
        private static bool TrySwitchEphemeralKeyPolicy(
            ISession session,
            string policyUri,
            [NotNullWhen(true)] out EphemeralKeyType? key)
        {
            key = null;
            if (session is Session serverSession &&
                !serverSession.CanCreateEphemeralKey(policyUri))
            {
                return false;
            }

            try
            {
                session.SetUserTokenSecurityPolicy(policyUri);
                key = session.GetNewEphemeralKey();
            }
            catch (Exception e) when (e is not ObjectDisposedException)
            {
                // a session implementation without the pre-check: the key cannot be
                // kept, but the client is still told the policy was rejected.
                key = null;
            }
            return key != null;
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
