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
