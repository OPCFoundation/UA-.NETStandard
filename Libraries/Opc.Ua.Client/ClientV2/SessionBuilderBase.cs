#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua.Client.Sessions;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Session builder base. Can be extended to support extra configurability.
    /// </summary>
    /// <typeparam name="TPooledSessionOptions"></typeparam>
    /// <typeparam name="TSessionOptions"></typeparam>
    /// <typeparam name="TSessionCreateOptions"></typeparam>
    /// <typeparam name="TOptionsBuilder"></typeparam>
    /// <param name="application"></param>
    /// <param name="pooledSessionBuilder"></param>
    internal class SessionBuilderBase<TPooledSessionOptions, TSessionOptions,
        TSessionCreateOptions, TOptionsBuilder>(SessionManagerBase application,
        IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions> pooledSessionBuilder) :
        ISessionBuilder<TPooledSessionOptions, TSessionOptions, TSessionCreateOptions>,
        IOptionsBuilder<EndpointDescription>
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TOptionsBuilder : ISessionCreateOptionsBuilder<TSessionCreateOptions>, new()
    {
        /// <inheritdoc/>
        public IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions> FromPool { get; }
            = pooledSessionBuilder;

        /// <inheritdoc/>
        public IPkiManagement PkiManagement
            => application;

        /// <inheritdoc/>
        public IDiscovery Discovery
            => application;

        /// <inheritdoc/>
        public EndpointDescription Options
            => ((IOptionsBuilder<TPooledSessionOptions>)FromPool).Options.Endpoint;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            ConnectTo(string endpointUrl)
        {
            Options.EndpointUrl = endpointUrl;
            return this;
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            WithSecurityMode(MessageSecurityMode securityMode)
        {
            Options.SecurityMode = securityMode;
            return this;
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            WithSecurityPolicy(string securityPolicyUri)
        {
            Options.SecurityPolicyUri = securityPolicyUri;
            return this;
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            WithServerCertificate(byte[] serverCertificate)
        {
            Options.ServerCertificate = (ByteString)serverCertificate;
            return this;
        }

        /// <inheritdoc/>
        public ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            WithTransportProfileUri(string transportProfileUri)
        {
            Options.TransportProfileUri = transportProfileUri;
            return this;
        }

        /// <inheritdoc/>
        public IUnpooledSessionBuilder<TSessionCreateOptions> WithOption(
            Action<ISessionCreateOptionsBuilder<TSessionCreateOptions>> configure)
        {
            configure(_optionsBuilder);
            return this;
        }

        /// <inheritdoc/>
        public IUnpooledSessionBuilder<TSessionCreateOptions> UseReverseConnect(
            bool useReverseConnect = true)
        {
            _useReverseConnect = useReverseConnect;
            return this;
        }

        /// <inheritdoc/>
        public ValueTask<ISessionHandle> CreateAsync(CancellationToken ct = default)
        {
            return application.ConnectAsync(Options,
                ((IOptionsBuilder<TSessionCreateOptions>)_optionsBuilder).Options,
                _useReverseConnect, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> TestAsync(CancellationToken ct = default)
        {
            return application.TestAsync(Options,
                // ((IOptionsBuilder<TSessionCreateOptions>)_sessionCreateOptionsBuilder).Options,
                _useReverseConnect, ct);
        }

        /// <summary>
        /// Called when disposed
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            application.Dispose();
        }

        private readonly TOptionsBuilder _optionsBuilder = new();
        private bool _useReverseConnect;
    }

    /// <summary>
    /// Builder
    /// </summary>
    /// <typeparam name="TPooledSessionOptions"></typeparam>
    /// <typeparam name="TSessionOptions"></typeparam>
    /// <typeparam name="TBuilder"></typeparam>
    /// <param name="application"></param>
    internal class PooledSessionBuilderBase<TPooledSessionOptions,
        TSessionOptions, TBuilder>(ISessionManager application) :
        IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions>,
        IOptionsBuilder<TPooledSessionOptions>
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TBuilder : ISessionOptionsBuilder<TSessionOptions>, new()
    {
        /// <inheritdoc/>
        public TPooledSessionOptions Options { get; set; } = new();

        /// <inheritdoc/>
        public ValueTask<ISessionHandle> CreateAsync(CancellationToken ct = default)
        {
            Debug.Assert(Options.Endpoint.EndpointUrl != null);
            return application.GetOrConnectAsync(Options, ct);
        }

        /// <inheritdoc/>
        public IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions> WithUser(
            IUserIdentity user)
        {
            Options = Options with { User = user };
            return this;
        }

        /// <inheritdoc/>
        public IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions>
            WithOption(Action<ISessionOptionsBuilder<TSessionOptions>> configure)
        {
            configure(_optionsBuilder);
            Options = Options with
            {
                SessionOptions = ((IOptionsBuilder<TSessionOptions>)_optionsBuilder).Options
            };
            return this;
        }

        /// <inheritdoc/>
        public IPooledSessionBuilder<TPooledSessionOptions, TSessionOptions>
            UseReverseConnect(bool useReverseConnect)
        {
            Options = Options with
            {
                UseReverseConnect = useReverseConnect
            };
            return this;
        }

        private readonly TBuilder _optionsBuilder = new();
    }
}
#endif
