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
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public static class EndpointDescriptionExtensions
    {
        extension(EndpointDescription endpointDescription)
        {
            /// <summary>
            /// The encodings supported by the configuration.
            /// </summary>
            public BinaryEncodingSupport EncodingSupport
            {
                get
                {
                    if (!string.IsNullOrEmpty(endpointDescription.EndpointUrl) &&
                        endpointDescription.EndpointUrl.StartsWith(
                            Utils.UriSchemeOpcTcp,
                            StringComparison.Ordinal))
                    {
                        return BinaryEncodingSupport.Required;
                    }

                    endpointDescription.TransportProfileUri =
                        Profiles.NormalizeUri(endpointDescription.TransportProfileUri);
                    return endpointDescription.TransportProfileUri == Profiles.HttpsBinaryTransport ?
                        BinaryEncodingSupport.Required : BinaryEncodingSupport.None;
                }
            }

            /// <summary>
            /// Finds the user token policy with the specified id and securtyPolicyUri
            /// </summary>
            public UserTokenPolicy FindUserTokenPolicy(string policyId)
            {
                // The specified security policies take precedence
                foreach (UserTokenPolicy policy in endpointDescription.UserIdentityTokens)
                {
                    if (policy.PolicyId == policyId)
                    {
                        if (policy.SecurityPolicyUri == tokenSecurityPolicyUri)
                        {
                            return policy;
                        }
                    }
                }
                return null;
            }

            /// <summary>
            /// Finds a token policy that matches the user identity specified.
            /// </summary>
            public UserTokenPolicy FindUserTokenPolicy(
                UserTokenType tokenType,
                XmlQualifiedName issuedTokenType,
                string preferredSecurityPolicyUri,
                string[] fallbackSecurityPolicyUris)
            {
                // Use the namespace uri for the issued token type.
                string issuedTokenTypeDef = issuedTokenType?.Namespace;

                // Iterate twice: first for exact matches, then for relaxed matches.
                foreach (bool exactMatch in new[] { true, false })
                {
                    // Check preferred policy.
                    UserTokenPolicy match = FindUserTokenPolicy(
                        tokenType,
                        issuedTokenTypeDef,
                        preferredSecurityPolicyUri,
                        exactMatch);

                    if (match != null)
                    {
                        return match;
                    }

                    // Check fallback policies.
                    if (fallbackSecurityPolicyUris != null)
                    {
                        foreach (string policy in fallbackSecurityPolicyUris)
                        {
                            match = FindUserTokenPolicy(
                                tokenType,
                                issuedTokenTypeDef,
                                policy,
                                exactMatch);

                            if (match != null)
                            {
                                return match;
                            }
                        }
                    }
                }

                return null;
            }

            /// <summary>
            /// Finds a token policy that matches the user identity specified.
            /// </summary>
            public UserTokenPolicy FindUserTokenPolicy(
                UserTokenType tokenType,
                XmlQualifiedName issuedTokenType,
                string tokenSecurityPolicyUri)
            {
                if (issuedTokenType == null)
                {
                    return endpointDescription.FindUserTokenPolicy(
                        tokenType,
                        (string)null,
                        tokenSecurityPolicyUri);
                }

                return endpointDescription.FindUserTokenPolicy(
                    tokenType,
                    issuedTokenType.Namespace,
                    tokenSecurityPolicyUri);
            }

            /// <summary>
            /// Finds a token policy that matches the user identity specified.
            /// </summary>
            public UserTokenPolicy FindUserTokenPolicy(
                UserTokenType tokenType,
                string issuedTokenType,
                string tokenSecurityPolicyUri)
            {
                // construct issuer type.
                string issuedTokenTypeText = issuedTokenType;

                UserTokenPolicy sameEncryptionAlgorithm = null;
                UserTokenPolicy unspecifiedSecPolicy = null;
                // The specified security policies take precedence
                foreach (UserTokenPolicy policy in endpointDescription.UserIdentityTokens)
                {
                    if ((policy.TokenType == tokenType) &&
                        (issuedTokenTypeText == policy.IssuedTokenType))
                    {
                        if ((policy.SecurityPolicyUri == tokenSecurityPolicyUri) ||
                            (tokenType == UserTokenType.Anonymous))
                        {
                            return policy;
                        }
                        else if ((
                                policy.SecurityPolicyUri != null &&
                                tokenSecurityPolicyUri != null &&
                                EccUtils.IsEccPolicy(policy.SecurityPolicyUri) &&
                                EccUtils.IsEccPolicy(tokenSecurityPolicyUri)
                            ) ||
                            (
                                !EccUtils.IsEccPolicy(policy.SecurityPolicyUri) &&
                                !EccUtils.IsEccPolicy(tokenSecurityPolicyUri)))
                        {
                            sameEncryptionAlgorithm ??= policy;
                        }
                        else if (policy.SecurityPolicyUri == null)
                        {
                            if (sameEncryptionAlgorithm == null)
                            {
                                unspecifiedSecPolicy = policy;
                            }
                        }
                    }
                }
                // The first token with the same encryption algorithm (RSA/ECC) follows
                if (sameEncryptionAlgorithm != null)
                {
                    return sameEncryptionAlgorithm;
                }
                // The first token with unspecified security policy follows / no policy
                return unspecifiedSecPolicy;
            }
        }
    }
}
