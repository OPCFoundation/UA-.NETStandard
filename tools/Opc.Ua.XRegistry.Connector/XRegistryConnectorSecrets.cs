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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Opc.Ua.XRegistry.Connector
{
    internal sealed class XRegistryEnvironmentSecretStore : ISecretStore
    {
        public XRegistryEnvironmentSecretStore(
            IConfiguration section, Func<string, string?>? readVariable = null)
        {
            ArgumentNullException.ThrowIfNull(section);
            m_readVariable = readVariable ?? Environment.GetEnvironmentVariable;
            foreach (IConfigurationSection entry in section.GetChildren())
            {
                string variable = entry.Value ??
                    throw new ArgumentException(
                        "Secrets maps logical names to environment-variable names, never plaintext values.",
                        nameof(section));
                if (string.IsNullOrWhiteSpace(variable))
                {
                    throw new ArgumentException("A secret environment-variable name cannot be empty.", nameof(section));
                }
                m_variables.Add(entry.Key, variable);
            }
        }

        public string StoreType => "Environment";

        public ISecret? TryGet(SecretIdentifier id)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (id.StoreType != StoreType ||
                !string.IsNullOrEmpty(id.StorePath) ||
                !m_variables.TryGetValue(id.Name, out string? variable))
            {
                return null;
            }
            string? value = m_readVariable(variable);
            return string.IsNullOrEmpty(value) ? null : new Secret(Encoding.UTF8.GetBytes(value));
        }

        public ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // Ownership transfers to the caller through ValueTask.
            // TODO: Remove when CA2000 models asynchronous ownership transfer.
#pragma warning disable CA2000
            return new ValueTask<ISecret?>(TryGet(id));
#pragma warning restore CA2000
        }

        public ValueTask SetAsync(SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        {
            throw new NotSupportedException("Environment secrets are provisioned outside the connector.");
        }

        public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            throw new NotSupportedException("Environment secrets are provisioned outside the connector.");
        }

        private sealed class Secret(byte[] bytes) : ISecret
        {
            public ReadOnlySpan<byte> Bytes
            {
                get
                {
                    ObjectDisposedException.ThrowIf(m_disposed, this);
                    return bytes;
                }
            }

            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(bytes);
                m_disposed = true;
            }

            private bool m_disposed;
        }

        private readonly Func<string, string?> m_readVariable;
        private readonly Dictionary<string, string> m_variables = new(StringComparer.Ordinal);
    }

    internal sealed class XRegistryBearerHandler : DelegatingHandler
    {
        public XRegistryBearerHandler(
            ISecretRegistry secrets, SecretIdentifier secretId, Uri root, HttpMessageHandler inner)
            : base(inner)
        {
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_secretId = secretId ?? throw new ArgumentNullException(nameof(secretId));
            m_root = root ?? throw new ArgumentNullException(nameof(root));
            if (!root.IsAbsoluteUri ||
                root.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(root.UserInfo) ||
                !string.IsNullOrEmpty(root.Query) ||
                !string.IsNullOrEmpty(root.Fragment))
            {
                throw new ArgumentException(
                    "Operator bearer credentials require an absolute HTTPS root.", nameof(root));
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri target = request.RequestUri ?? throw new InvalidOperationException("The request URI is missing.");
            string path = m_root.AbsolutePath.TrimEnd('/');
            if (!target.IsAbsoluteUri ||
                target.Scheme != m_root.Scheme ||
                target.Host != m_root.Host ||
                target.Port != m_root.Port ||
                !string.IsNullOrEmpty(target.UserInfo) ||
                !(target.AbsolutePath == path || target.AbsolutePath.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Credentials cannot be sent outside the configured registry root.");
            }
            using ISecret secret = await m_secrets.GetAsync(m_secretId, cancellationToken).ConfigureAwait(false) ??
                throw new UnauthorizedAccessException("The HTTP operator credential is unavailable.");
            ValidateCredential(secret.Bytes);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", Encoding.UTF8.GetString(secret.Bytes));
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateCredential(ReadOnlySpan<byte> value)
        {
            if (value.Length is 0 or > 65_536 || value[0] == '=')
            {
                throw new UnauthorizedAccessException(
                    "The HTTP operator credential is empty or exceeds its size limit.");
            }
            bool padding = false;
            foreach (byte character in value)
            {
                if (character == '=')
                {
                    padding = true;
                    continue;
                }
                if (padding ||
                    !(character is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z')
                        or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~'
                        or (byte)'+' or (byte)'/'))
                {
                    throw new UnauthorizedAccessException("The HTTP operator credential has invalid header material.");
                }
            }
        }

        private readonly ISecretRegistry m_secrets;
        private readonly SecretIdentifier m_secretId;
        private readonly Uri m_root;
    }
}
