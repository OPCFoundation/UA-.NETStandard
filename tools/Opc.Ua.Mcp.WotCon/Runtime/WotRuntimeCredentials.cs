/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Resolves only the selected form's authorized references; resolved material is never retained by MCP.
    /// Credential-bearing executor channels are operation-scoped because existing executors cache credentials.
    /// </summary>
    internal sealed class WotRuntimeCredentials : IWotCredentialProvider
    {
        /// <summary>
        /// Validates one form's exact security contract without choosing an alternate scheme.
        /// </summary>
        internal WotRuntimeCredentials(
            WotCompiledForm form, WotRuntimeTarget target, ArrayOf<WotRuntimeCredentialInput> inputs,
            McpSecretReferences? secrets, IMcpOperationPolicy policy)
        {
            m_form = form;
            m_target = target;
            m_secrets = secrets;
            m_capability = WotRuntimeTarget.Capability(form.OpToken);
            if (inputs.Count > 1 || form.Security.Length > 1)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Live execution requires one explicit security scheme, not alternatives or combinations.");
            }
            WotCredentialReference? security = form.Security.IsEmpty ? null : form.Security[0];
            if (form.Binding.Id == "opc.opcua")
            {
                if (!inputs.IsEmpty ||
                    (security is not null &&
                        security.Scheme is not (WotSecurityScheme.NoSecurity or WotSecurityScheme.Auto)))
                {
                    throw new ArgumentException("OPC UA uses only the explicitly selected session's identity.");
                }
                Dependencies = [];
                return;
            }
            if (security is null || security.Scheme == WotSecurityScheme.NoSecurity)
            {
                if (!inputs.IsEmpty)
                {
                    throw new ArgumentException("A no-security form cannot accept credential references.");
                }
                Dependencies = [];
                return;
            }
            if (inputs.Count != 1 ||
                inputs[0] is not { } input ||
                !string.Equals(input.SchemeName, security.SchemeName, StringComparison.Ordinal))
            {
                throw new ArgumentException("The selected security definition requires its exact secret reference.");
            }
            if (secrets is null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The host has not registered its secret-reference gateway.");
            }
            if (target.Endpoint.Scheme is not ("https" or "mqtts"))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeRejected,
                    "Live WoT credential references require HTTPS or MQTT over TLS.");
            }
            if ((form.Binding.Id == "w3c.mqtt" && security.Scheme != WotSecurityScheme.Basic) ||
                (form.Binding.Id == "w3c.http" &&
                    security.Scheme is not
                        (WotSecurityScheme.Basic or WotSecurityScheme.Bearer or
                        WotSecurityScheme.OAuth2 or WotSecurityScheme.ApiKey)))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "This executor cannot apply the authored credential scheme through the secret gateway.");
            }
            if (security.In is not (null or "" or "header"))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Live HTTP credentials are restricted to headers, never URL query parameters.");
            }
            if (security.Scheme == WotSecurityScheme.ApiKey)
            {
                ValidateApiKeyHeader(security.ParameterName);
            }
            if (security.Scheme == WotSecurityScheme.Basic != (input.UserNameReference is not null))
            {
                throw new ArgumentException("Only Basic/MQTT credentials require a separate username reference.");
            }
            m_input = input;
            var dependencies = new List<McpSecretDependency>();
            AddReference(input.SecretReference, dependencies, policy);
            if (input.UserNameReference is { } username)
            {
                AddReference(username, dependencies, policy);
            }
            Dependencies = [.. dependencies];
        }

        /// <summary>
        /// Gets issued-reference dependencies whose revocation and expiry close the owning runtime.
        /// </summary>
        internal ArrayOf<McpSecretDependency> Dependencies { get; }

        /// <summary>
        /// Gets whether the channel must be disposed after each operation to prevent cached secret reuse.
        /// </summary>
        internal bool HasCredentials => m_input is not null;

        /// <summary>
        /// Gets whether the host supplied an alias without asynchronous invalidation metadata.
        /// </summary>
        internal bool HasAlias => m_input is not null &&
            (m_input.SecretReference.StartsWith("alias:", StringComparison.Ordinal) ||
                m_input.UserNameReference?.StartsWith("alias:", StringComparison.Ordinal) == true);

        /// <inheritdoc/>
        public async ValueTask<WotCredential?> ResolveAsync(
            WotCredentialReference reference, CancellationToken cancellationToken = default)
        {
            if (reference.Scheme == WotSecurityScheme.NoSecurity || m_form.Binding.Id == "opc.opcua")
            {
                return null;
            }
            if (m_input is null ||
                m_secrets is null ||
                m_form.Security.Length != 1 ||
                !ReferenceEquals(reference, m_form.Security[0]))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "Credential resolution is not authorized for this form.");
            }
            using ISecret secret = await m_secrets.ResolveAsync(
                m_input.SecretReference, m_target.CredentialResource, m_capability, cancellationToken)
                .ConfigureAwait(false);
            if (secret.Bytes.Length is 0 or > 8192)
            {
                throw new ArgumentException("The credential size is invalid for the selected transport.");
            }
            var headers = ImmutableDictionary<string, string>.Empty;
            if (reference.Scheme == WotSecurityScheme.Basic)
            {
                using ISecret username = await m_secrets.ResolveAsync(
                    m_input.UserNameReference!, m_target.CredentialResource, m_capability, cancellationToken)
                    .ConfigureAwait(false);
                if (username.Bytes.Length is 0 or > 1024 || username.Bytes.Contains((byte)':'))
                {
                    throw new ArgumentException("The username is invalid for the selected credential scheme.");
                }
                if (m_form.Binding.Id == "w3c.mqtt")
                {
                    return new WotCredential(reference.Scheme, properties:
                        ImmutableDictionary<string, string>.Empty
                            .Add("username", Decode(username))
                            .Add("password", Decode(secret)));
                }
                byte[] combined = new byte[username.Bytes.Length + 1 + secret.Bytes.Length];
                try
                {
                    username.Bytes.CopyTo(combined);
                    combined[username.Bytes.Length] = (byte)':';
                    secret.Bytes.CopyTo(combined.AsSpan(username.Bytes.Length + 1));
                    headers = headers.Add("Authorization", "Basic " + Convert.ToBase64String(combined));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(combined);
                }
            }
            else
            {
                string value = Decode(secret);
                if (value.Contains('\r', StringComparison.Ordinal) ||
                    value.Contains('\n', StringComparison.Ordinal))
                {
                    throw new ArgumentException("A credential cannot contain HTTP header delimiters.");
                }
                headers = reference.Scheme == WotSecurityScheme.ApiKey
                    ? headers.Add(reference.ParameterName!, value)
                    : headers.Add("Authorization", "Bearer " + value);
            }
            return new WotCredential(reference.Scheme, headers: headers);
        }

        /// <summary>
        /// Validates references without interpreting caller strings as store identifiers.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private void AddReference(
            string reference, List<McpSecretDependency> dependencies, IMcpOperationPolicy policy)
        {
            WotRuntimeJson.Text(reference, 256);
            if (reference.StartsWith("issued:", StringComparison.Ordinal))
            {
                dependencies.Add(m_secrets!.GetDependency(reference, m_target.CredentialResource, m_capability));
            }
            else if (reference.StartsWith("alias:", StringComparison.Ordinal))
            {
                _ = policy.RequireSecret(m_capability, reference["alias:".Length..]);
            }
            else
            {
                throw new ArgumentException("Credentials must use authorized alias: or issued: references.");
            }
        }

        /// <summary>
        /// Decodes a short-lived secret view using strict UTF-8 without exposing decoder input in errors.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static string Decode(ISecret secret)
        {
            try
            {
                return s_utf8.GetString(secret.Bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new ArgumentException("The credential is not valid UTF-8 for this transport.");
            }
        }

        /// <summary>
        /// Permits custom API-key headers but never transport, routing, cookie or proxy overrides.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateApiKeyHeader(string? header)
        {
            if (string.IsNullOrWhiteSpace(header) ||
                header.Length > 128 ||
                s_forbiddenHeaders.Contains(header))
            {
                throw new ArgumentException("The API-key header cannot override transport or ambient identity.");
            }
            foreach (char character in header)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    throw new ArgumentException("The API-key header name is invalid.");
                }
            }
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);

        private static readonly HashSet<string> s_forbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Cookie", "Set-Cookie", "Proxy-Authorization", "Proxy-Authenticate", "Connection",
            "Content-Length", "Transfer-Encoding", "Upgrade", "Forwarded", "X-Forwarded-Host"
        };

        private readonly WotCompiledForm m_form;
        private readonly WotRuntimeTarget m_target;
        private readonly McpSecretReferences? m_secrets;
        private readonly McpCapability m_capability;
        private readonly WotRuntimeCredentialInput? m_input;
    }
}
