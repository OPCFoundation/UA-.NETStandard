// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua.Client.Sessions;
    using Polly;
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Session options builder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SessionOptionsBuilderBase<T> : ISessionOptionsBuilder<T>,
        IOptionsBuilder<T>
        where T : SessionOptions, new()
    {
        /// <inheritdoc/>
        public T Options { get; set; } = new();

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> WithName(
            string sessionName)
        {
            Options = Options with { SessionName = sessionName };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> WithTimeout
            (TimeSpan sessionTimeout)
        {
            Options = Options with { SessionTimeout = sessionTimeout };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> WithPreferredLocales(
            IReadOnlyList<string> preferredLocales)
        {
            Options = Options with { PreferredLocales = preferredLocales };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> WithKeepAliveInterval(
            TimeSpan keepAliveInterval)
        {
            Options = Options with { KeepAliveInterval = keepAliveInterval };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> CheckDomain(
            bool checkDomain)
        {
            Options = Options with { CheckDomain = checkDomain };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> EnableComplexTypePreloading(
            bool enableComplexTypePreloading)
        {
            Options = Options with { EnableComplexTypePreloading = enableComplexTypePreloading };
            return this;
        }

        /// <inheritdoc/>
        public ISessionOptionsBuilder<T> DisableDataTypeDictionary(
            bool disableDataTypeDictionary = true)
        {
            Options = Options with { DisableDataTypeDictionary = disableDataTypeDictionary };
            return this;
        }
    }

    /// <summary>
    /// Build session create options
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SessionCreateOptionsBuilder<T> : SessionOptionsBuilderBase<T>,
        ISessionCreateOptionsBuilder<T>
        where T : SessionCreateOptions, new()
    {
        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithAvailableEndpoints(
            EndpointDescriptionCollection availableEndpoints)
        {
            Options = Options with { AvailableEndpoints = availableEndpoints };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithDiscoveryProfileUris(
            StringCollection discoveryProfileUris)
        {
            Options = Options with { DiscoveryProfileUris = discoveryProfileUris };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithChannel(
            ITransportChannel channel)
        {
            Options = Options with { Channel = channel };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithConnection(
            ITransportWaitingConnection connection)
        {
            Options = Options with { Connection = connection };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithUser(
            IUserIdentity identity)
        {
            Options = Options with { Identity = identity };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithClientCertificate(
            X509Certificate2 clientCertificate)
        {
            Options = Options with { ClientCertificate = clientCertificate };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithReconnectStrategy(
            ResiliencePipeline reconnectStrategy)
        {
            Options = Options with { ReconnectStrategy = reconnectStrategy };
            return this;
        }

        /// <inheritdoc/>
        public ISessionCreateOptionsBuilder<T> WithReconnectStrategy(
            Action<ResiliencePipelineBuilder> reconnectStrategy)
        {
            var builder = new ResiliencePipelineBuilder();
            reconnectStrategy(builder);
            Options = Options with { ReconnectStrategy = builder.Build() };
            return this;
        }
    }
}
