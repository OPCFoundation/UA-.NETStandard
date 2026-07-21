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
using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;

namespace Opc.Ua.WotCon.Binding.Mqtt
{
    /// <summary>
    /// Builds the transport-security-aware MQTT client options for a compiled WoT
    /// form. The <c>mqtts</c> scheme always enables TLS (defaulting to port 8883)
    /// and applies the trust anchors and client certificate resolved through the
    /// credential provider; the <c>mqtt</c> scheme stays explicit plaintext
    /// (port 1883). The builder fails closed: a declared security scheme that
    /// resolves to no credential, or username / password material that would be
    /// sent over a plaintext connection, throws instead of downgrading silently.
    /// </summary>
    internal static class MqttWotConnection
    {
        internal const int DefaultTlsPort = 8883;
        internal const int DefaultPlaintextPort = 1883;

        /// <summary>The result of preparing an MQTT connection for a compiled form.</summary>
        internal sealed class MqttWotConnectPlan
        {
            public MqttWotConnectPlan(MqttClientOptions options, string host, int port, bool useTls, bool hasCredentials)
            {
                Options = options;
                Host = host;
                Port = port;
                UseTls = useTls;
                HasCredentials = hasCredentials;
            }

            /// <summary>Gets the built MQTT client options.</summary>
            public MqttClientOptions Options { get; }

            /// <summary>Gets the resolved broker host.</summary>
            public string Host { get; }

            /// <summary>Gets the resolved broker port.</summary>
            public int Port { get; }

            /// <summary>Gets whether TLS is enabled for the connection.</summary>
            public bool UseTls { get; }

            /// <summary>Gets whether username / password credentials were applied.</summary>
            public bool HasCredentials { get; }
        }

        /// <summary>
        /// Resolves credentials / trust through the provider and builds the MQTT
        /// client options for the supplied compiled form, enforcing the transport
        /// security rules described on the type.
        /// </summary>
        public static async ValueTask<MqttWotConnectPlan> PrepareAsync(
            WotCompiledForm form,
            WotExecutorContext context,
            MqttWotBindingOptions options,
            string clientId,
            CancellationToken cancellationToken)
        {
            bool useTls = string.Equals(form.Endpoint.Scheme, "mqtts", StringComparison.OrdinalIgnoreCase);
            string host = string.IsNullOrEmpty(form.Endpoint.Host) ? "127.0.0.1" : form.Endpoint.Host!;
            int port = form.Endpoint.Port > 0
                ? form.Endpoint.Port
                : (useTls ? DefaultTlsPort : DefaultPlaintextPort);

            WotCredential? credential = await ResolveRequiredCredentialAsync(form, context, cancellationToken)
                .ConfigureAwait(false);

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId(clientId);

            string? username = null;
            byte[] password = Array.Empty<byte>();
            if (credential is not null)
            {
                if (credential.Properties.TryGetValue("username", out string? user))
                {
                    username = user;
                }
                if (credential.Properties.TryGetValue("password", out string? pass) && pass is not null)
                {
                    password = Encoding.UTF8.GetBytes(pass);
                }
            }

            bool hasCredentials = !string.IsNullOrEmpty(username);
            if (hasCredentials && !useTls && !options.AllowCredentialsOverPlaintext)
            {
                throw new InvalidOperationException(
                    "MQTT username / password credentials require TLS. Use an mqtts:// href, or set " +
                    "MqttWotBindingOptions.AllowCredentialsOverPlaintext for explicitly accepted plaintext deployments.");
            }
            if (hasCredentials)
            {
                builder = builder.WithCredentials(username, password);
            }

            if (useTls)
            {
                X509Certificate2? clientCertificate = credential?.ClientCertificate;
                ImmutableArray<X509Certificate2> trust = credential is null
                    ? ImmutableArray<X509Certificate2>.Empty
                    : credential.TrustedCertificates;
                bool validate = options.ValidateServerCertificate;
                builder = builder.WithTlsOptions(tls =>
                {
                    tls.UseTls().WithAllowUntrustedCertificates(!validate);
                    if (clientCertificate is not null)
                    {
                        tls.WithClientCertificates(new X509Certificate2Collection { clientCertificate });
                    }
                    if (!trust.IsDefaultOrEmpty)
                    {
                        var chain = new X509Certificate2Collection();
                        foreach (X509Certificate2 anchor in trust)
                        {
                            chain.Add(anchor);
                        }
                        tls.WithTrustChain(chain);
                    }
                });
            }

            return new MqttWotConnectPlan(builder.Build(), host, port, useTls, hasCredentials);
        }

        private static async ValueTask<WotCredential?> ResolveRequiredCredentialAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken)
        {
            if (form.Security.IsDefaultOrEmpty)
            {
                return null;
            }
            foreach (WotCredentialReference reference in form.Security)
            {
                if (reference.Scheme == WotSecurityScheme.NoSecurity)
                {
                    continue;
                }
                WotCredential? credential = await context.Credentials
                    .ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
                if (credential is null)
                {
                    // Fail closed: a form that declares a security scheme must have
                    // its credential resolved or the connection is refused rather
                    // than silently opened without the required authentication.
                    throw new InvalidOperationException(
                        $"The MQTT binding requires a credential for security scheme '{reference.SchemeName}' " +
                        "but the credential provider resolved none; refusing to connect.");
                }
                return credential;
            }
            return null;
        }
    }
}
