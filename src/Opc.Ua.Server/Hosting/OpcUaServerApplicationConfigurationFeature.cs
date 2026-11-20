// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class OpcUaServerApplicationConfigurationFeature :
        IOpcUaApplicationConfigurationFeature
    {
        public OpcUaServerApplicationConfigurationFeature(
            IOptions<OpcUaServerOptions> options)
        {
            m_options = options.Value;
        }

        public void ApplyDefaults(OpcUaApplicationOptions options)
        {
            options.ApplicationName ??= m_options.ApplicationName;
            options.ApplicationUri ??= m_options.ApplicationUri;
            options.ProductUri ??= m_options.ProductUri;
            options.SubjectName ??= m_options.SubjectName;
            options.PkiRoot ??= m_options.PkiRoot;
            options.AutoAcceptUntrustedCertificates ??=
                m_options.AutoAcceptUntrustedCertificates;
            options.RejectSHA1SignedCertificates ??= m_options.RejectSHA1Certificates;
            options.MinimumCertificateKeySize ??= m_options.MinCertificateKeySize;
        }

        public IApplicationConfigurationBuilderSecurity Configure(
            IApplicationConfigurationBuilderTypes builder)
        {
            return Configure(builder, m_options);
        }

        internal static IApplicationConfigurationBuilderSecurity Configure(
            IApplicationConfigurationBuilderTypes builder,
            OpcUaServerOptions options)
        {
            IApplicationConfigurationBuilderTransportQuotas quotasBuilder = builder
                .SetMaxByteStringLength((int)options.MaxByteStringLength)
                .SetMaxStringLength((int)options.MaxStringLength)
                .SetMaxArrayLength((int)options.MaxArrayLength);

            if (options.MaxMessageSize is int maxMessageSize)
            {
                quotasBuilder = quotasBuilder.SetMaxMessageSize(maxMessageSize);
            }
            if (options.OperationTimeoutMs is int operationTimeout)
            {
                quotasBuilder = quotasBuilder.SetOperationTimeout(operationTimeout);
            }

            string[] urls = new string[options.EndpointUrls.Count];
            options.EndpointUrls.CopyTo(urls, 0);
            IApplicationConfigurationBuilderServerSelected serverBuilder =
                quotasBuilder.AsServer(urls);

            if (options.IncludeSignAndEncryptPolicies)
            {
                serverBuilder = serverBuilder.AddSignAndEncryptPolicies();
            }
            if (options.IncludeEccPolicies)
            {
                serverBuilder = serverBuilder.AddEccSignAndEncryptPolicies();
            }
            if (options.IncludeUnsecurePolicyNone)
            {
                serverBuilder = serverBuilder.AddUnsecurePolicyNone();
            }

            if (options.UserTokenPolicies.Count == 0)
            {
                serverBuilder = serverBuilder.AddUserTokenPolicy(UserTokenType.Anonymous);
            }
            else
            {
                foreach (OpcUaUserTokenPolicy policy in options.UserTokenPolicies)
                {
                    serverBuilder = serverBuilder.AddUserTokenPolicy(policy.TokenType);
                }
            }

            IApplicationConfigurationBuilderServerOptions optionsBuilder =
                serverBuilder.SetDiagnosticsEnabled(options.DiagnosticsEnabled);
            if (options.OperationLimits is OperationLimitsOptions operationLimits)
            {
                optionsBuilder = optionsBuilder.SetOperationLimits(
                    operationLimits.ToOperationLimits());
            }
            if (options.ReverseConnect is ServerReverseConnectOptions reverseConnect)
            {
                optionsBuilder = optionsBuilder.SetReverseConnect(
                    ToReverseConnectConfiguration(reverseConnect));
            }
            if (!string.IsNullOrEmpty(options.RegistrationEndpointUrl))
            {
                optionsBuilder = optionsBuilder.SetRegistrationEndpoint(
                    new EndpointDescription { EndpointUrl = options.RegistrationEndpointUrl });
            }

            options.ConfigureBuilder?.Invoke(serverBuilder);
            return optionsBuilder;
        }

        private static ReverseConnectServerConfiguration ToReverseConnectConfiguration(
            ServerReverseConnectOptions options)
        {
            var clients = new ReverseConnectClient[options.Clients.Count];
            for (int i = 0; i < options.Clients.Count; i++)
            {
                ServerReverseConnectClientOptions client = options.Clients[i];
                clients[i] = new ReverseConnectClient
                {
                    EndpointUrl = client.EndpointUrl,
                    Timeout = client.Timeout,
                    Enabled = client.Enabled
                };
            }
            return new ReverseConnectServerConfiguration
            {
                ConnectInterval = options.ConnectIntervalMs,
                ConnectTimeout = options.ConnectTimeoutMs,
                RejectTimeout = options.RejectTimeoutMs,
                Clients = new ArrayOf<ReverseConnectClient>(clients)
            };
        }

        private readonly OpcUaServerOptions m_options;
    }
}
