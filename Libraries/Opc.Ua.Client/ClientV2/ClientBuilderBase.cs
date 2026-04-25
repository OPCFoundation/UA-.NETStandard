#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Configuration;
    using Polly;
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Builds clients that are then used to connect sessions to a server. Theses
    /// sessions can then be used to create subscriptions and monitored items or
    /// send service requests and receive responses.
    /// </summary>
    /// <typeparam name="TPooledSessionOptions"></typeparam>
    /// <typeparam name="TSessionOptions"></typeparam>
    /// <typeparam name="TSessionCreateOptions"></typeparam>
    /// <typeparam name="TClientOptions"></typeparam>
    /// <param name="services"></param>
    public abstract class ClientBuilderBase<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions>(
            IServiceCollection? services = null) :
        IClientBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>,
        IClientOptionsBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>,
        IApplicationNameBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>,
        IApplicationUriBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>,
        IProductBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>,
        IOptionsBuilder<TClientOptions>
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <inheritdoc/>
        public IServiceCollection Services { get; } = services
            ?? new ServiceCollection();

        /// <inheritdoc/>
        public TClientOptions Options { get; set; } = new();

        /// <inheritdoc/>
        public IApplicationNameBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            NewClientServer()
        {
            _applicationType = ApplicationType.ClientAndServer;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationNameBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            NewClient()
        {
            _applicationType = ApplicationType.Client;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationUriBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithName(string applicationName)
        {
            _applicationName = applicationName;
            return this;
        }

        /// <inheritdoc/>
        public IProductBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithUri(string applicationUri)
        {
            _applicationUri = applicationUri;
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithProductUri(string productUri)
        {
            _productUri = productUri;
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithReverseConnectPort(int reverseConnectPort)
        {
            Options = Options with { ReverseConnectPort = reverseConnectPort };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithMaxPooledSessions(int maxPooledSessions)
        {
            Options = Options with { MaxPooledSessions = maxPooledSessions };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithLingerTimeout(TimeSpan lingerTimeout)
        {
            Options = Options with { LingerTimeout = lingerTimeout };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithStackLogging(LogLevel maxLevel)
        {
            Options = Options with { StackLoggingLevel = maxLevel };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithConnectStrategy(ResiliencePipeline connectStrategy)
        {
            Options = Options with { RetryStrategy = connectStrategy };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithConnectStrategy(Action<ResiliencePipelineBuilder> connectStrategy)
        {
            var builder = new ResiliencePipelineBuilder();
            connectStrategy(builder);
            Options = Options with { RetryStrategy = builder.Build() };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithSecurityOption(Action<ISecurityOptionsBuilder> configure)
        {
            var builder = new SecurityOptionsBuilder(Options.Security);
            configure(builder);
            Options = Options with { Security = builder.Options };
            return this;
        }

        /// <inheritdoc/>
        public IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithTransportOption(Action<ITransportQuotaOptionsBuilder> configure)
        {
            var builder = new TransportQuotaOptionsBuilder(Options.Quotas);
            configure(builder);
            Options = Options with { Quotas = builder.Options };
            return this;
        }

        /// <summary>
        /// Security options builder
        /// </summary>
        /// <param name="options"></param>
        internal sealed class SecurityOptionsBuilder(SecurityOptions options) :
            IOptionsBuilder<SecurityOptions>, ISecurityOptionsBuilder
        {
            /// <inheritdoc/>
            public SecurityOptions Options { get; set; } = options;

            /// <inheritdoc/>
            public ISecurityOptionsBuilder SetPkiRootPath(string pkiRootPath)
            {
                Options = Options with { PkiRootPath = pkiRootPath };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder SetApplicationCertificateSubjectName(string subjectName)
            {
                Options = Options with { ApplicationCertificateSubjectName = subjectName };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder SetApplicationCertificatePassword(string password)
            {
                Options = Options with { ApplicationCertificatePassword = password };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder UpdateApplicationFromExistingCert(bool update)
            {
                Options = Options with { UpdateApplicationFromExistingCert = update };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder AddAppCertToTrustedStore(bool add)
            {
                Options = Options with { AddAppCertToTrustedStore = add };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder SetHostName(string? hostName)
            {
                Options = Options with { HostName = hostName };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder AutoAcceptUntrustedCertificates(bool autoAccept)
            {
                Options = Options with { AutoAcceptUntrustedCertificates = autoAccept };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder SetMinimumCertificateKeySize(ushort keySize)
            {
                Options = Options with { MinimumCertificateKeySize = keySize };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder RejectSha1SignedCertificates(bool reject)
            {
                Options = Options with { RejectSha1SignedCertificates = reject };
                return this;
            }

            /// <inheritdoc/>
            public ISecurityOptionsBuilder RejectUnknownRevocationStatus(bool reject)
            {
                Options = Options with { RejectUnknownRevocationStatus = reject };
                return this;
            }
        }

        /// <summary>
        /// Transport options builder
        /// </summary>
        /// <param name="options"></param>
        internal sealed class TransportQuotaOptionsBuilder(TransportQuotaOptions options) :
            IOptionsBuilder<TransportQuotaOptions>, ITransportQuotaOptionsBuilder
        {
            /// <inheritdoc/>
            public TransportQuotaOptions Options { get; set; } = options;

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetMaxArrayLength(int maxArrayLength)
            {
                Options = Options with { MaxArrayLength = maxArrayLength };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetMaxByteStringLength(int maxByteStringLength)
            {
                Options = Options with { MaxByteStringLength = maxByteStringLength };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetMaxMessageSize(int maxMessageSize)
            {
                Options = Options with { MaxMessageSize = maxMessageSize };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetMaxStringLength(int maxStringLength)
            {
                Options = Options with { MaxStringLength = maxStringLength };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetMaxBufferSize(int maxBufferSize)
            {
                Options = Options with { MaxBufferSize = maxBufferSize };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetOperationTimeout(TimeSpan operationTimeout)
            {
                Options = Options with { OperationTimeout = operationTimeout };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetChannelLifetime(TimeSpan channelLifetime)
            {
                Options = Options with { ChannelLifetime = channelLifetime };
                return this;
            }

            /// <inheritdoc/>
            public ITransportQuotaOptionsBuilder SetSecurityTokenLifetime(TimeSpan securityTokenLifetime)
            {
                Options = Options with { SecurityTokenLifetime = securityTokenLifetime };
                return this;
            }
        }

        /// <inheritdoc/>
        private sealed record class Observability(ILoggerFactory LoggerFactory,
             ActivitySource ActivitySource) : ITelemetryContext
        {
            /// <inheritdoc/>
            public Meter CreateMeter()
            {
                return new Meter(nameof(Observability));
            }
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions>
            Build()
        {
            // Resolve missing services from DI
            var provider = Services.BuildServiceProvider();

            var options = provider.GetService<IPostConfigureOptions<ClientOptions>>();
            options?.PostConfigure(null, Options);

            var telemetry = provider.GetService<ITelemetryContext>();
            if (telemetry == null)
            {
                var loggerFactory = provider.GetService<ILoggerFactory>();
                if (loggerFactory == null)
                {
                    Services.AddLogging(builder => builder.AddConsole());
                    provider = Services.BuildServiceProvider();
                    loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                }

                var activitySource = provider.GetService<ActivitySource>();

                telemetry = new Observability(loggerFactory,
                     activitySource ?? new ActivitySource(nameof(Observability)));
            }

            return Build(provider, new ApplicationInstance((Opc.Ua.ITelemetryContext?)null)
            {
                ApplicationType = _applicationType,
                ApplicationName = _applicationName
            }, _applicationUri!, _productUri!, Options, telemetry);
        }

        /// <summary>
        /// Create session builder
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="application"></param>
        /// <param name="applicationUri"></param>
        /// <param name="productUri"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        protected abstract ISessionBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions>
            Build(ServiceProvider provider, ApplicationInstance application, string applicationUri,
                string productUri, TClientOptions options, ITelemetryContext telemetry);

        private ApplicationType _applicationType;
        private string? _applicationUri;
        private string? _productUri;
        private string? _applicationName;
    }
}
#endif
