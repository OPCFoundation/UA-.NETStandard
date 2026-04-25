// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua.Client.Sessions;
    using Microsoft.Extensions.Logging;
    using Polly;
    using System;

    /// <inheritdoc/>
    public interface IClientBuilder<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions> :
        IDependencyInjectionBuilder
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <summary>
        /// Set whether the client is a client
        /// and server
        /// </summary>
        /// <returns></returns>
        IApplicationNameBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            NewClientServer();

        /// <summary>
        /// Set whether the client is a client
        /// </summary>
        /// <returns></returns>
        IApplicationNameBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            NewClient();
    }

    /// <inheritdoc/>
    public interface IApplicationNameBuilder<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions> :
        IDependencyInjectionBuilder
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <summary>
        /// Set application name
        /// </summary>
        /// <param name="applicationName"></param>
        /// <returns></returns>
        IApplicationUriBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithName(string applicationName);
    }

    /// <inheritdoc/>
    public interface IApplicationUriBuilder<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions> :
        IDependencyInjectionBuilder
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <summary>
        /// Set application uri
        /// </summary>
        /// <param name="applicationUri"></param>
        /// <returns></returns>
        IProductBuilder<TPooledSessionOptions, TSessionOptions,
            TSessionCreateOptions, TClientOptions>
            WithUri(string applicationUri);
    }

    /// <inheritdoc/>
    public interface IProductBuilder<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions> :
        IDependencyInjectionBuilder
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <summary>
        /// Set product uri
        /// </summary>
        /// <param name="productUri"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithProductUri(string productUri);
    }

    /// <inheritdoc/>
    public interface IClientOptionsBuilder<TPooledSessionOptions,
        TSessionOptions, TSessionCreateOptions, TClientOptions> :
        IDependencyInjectionBuilder
        where TPooledSessionOptions : PooledSessionOptions, new()
        where TSessionOptions : Sessions.SessionOptions, new()
        where TSessionCreateOptions : SessionCreateOptions, new()
        where TClientOptions : ClientOptions, new()
    {
        /// <summary>
        /// Use the connection strategy to connect to the server.
        /// </summary>
        /// <param name="connectStrategy"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithConnectStrategy(ResiliencePipeline connectStrategy);

        /// <summary>
        /// Build reconnect strategy
        /// </summary>
        /// <param name="connectStrategy"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithConnectStrategy(Action<ResiliencePipelineBuilder> connectStrategy);

        /// <summary>
        /// Use this timeout for the session pools
        /// </summary>
        /// <param name="lingerTimeout"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithLingerTimeout(TimeSpan lingerTimeout);

        /// <summary>
        /// Use the max pooled sessions specified
        /// </summary>
        /// <param name="maxPooledSessions"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithMaxPooledSessions(int maxPooledSessions);

        /// <summary>
        /// Use the reverse connect port
        /// </summary>
        /// <param name="reverseConnectPort"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithReverseConnectPort(int reverseConnectPort);

        /// <summary>
        /// Update security options
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithSecurityOption(Action<ISecurityOptionsBuilder> configure);

        /// <summary>
        /// Update transport quota options
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithTransportOption(Action<ITransportQuotaOptionsBuilder> configure);

        /// <summary>
        /// Enable and optionally set stack logging level
        /// </summary>
        /// <param name="maxLevel"></param>
        /// <returns></returns>
        IClientOptionsBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions, TClientOptions>
            WithStackLogging(LogLevel maxLevel = LogLevel.Error);

        /// <summary>
        /// Build session builder
        /// </summary>
        /// <returns></returns>
        ISessionBuilder<TPooledSessionOptions,
            TSessionOptions, TSessionCreateOptions>
            Build();
    }

    /// <summary>
    /// Security options builder
    /// </summary>
    public interface ISecurityOptionsBuilder
    {
        /// <summary>
        /// Add application certificate to trusted store
        /// </summary>
        /// <param name="add"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder AddAppCertToTrustedStore(
            bool add = true);

        /// <summary>
        /// Set the password for the application certificate
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder SetApplicationCertificatePassword(
            string password);

        /// <summary>
        /// Set the subject name of the application certificate
        /// </summary>
        /// <param name="subjectName"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder SetApplicationCertificateSubjectName(
            string subjectName);

        /// <summary>
        /// Set whether to auto accept untrusted certificates
        /// </summary>
        /// <param name="autoAccept"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder AutoAcceptUntrustedCertificates(
            bool autoAccept = true);

        /// <summary>
        /// Set the host name to use
        /// </summary>
        /// <param name="hostName"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder SetHostName(string hostName);

        /// <summary>
        /// Set the minimum key size
        /// </summary>
        /// <param name="keySize"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder SetMinimumCertificateKeySize(
            ushort keySize);

        /// <summary>
        /// Set the pki root path
        /// </summary>
        /// <param name="pkiRootPath"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder SetPkiRootPath(
            string pkiRootPath);

        /// <summary>
        /// Reject sha1 signed certificates
        /// </summary>
        /// <param name="reject"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder RejectSha1SignedCertificates(
            bool reject = true);

        /// <summary>
        /// Reject unknown revocation status
        /// </summary>
        /// <param name="reject"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder RejectUnknownRevocationStatus(
            bool reject = true);

        /// <summary>
        /// Update application information from existing certificate
        /// </summary>
        /// <param name="update"></param>
        /// <returns></returns>
        ISecurityOptionsBuilder UpdateApplicationFromExistingCert(
            bool update = true);
    }

    /// <summary>
    /// Configure the transport quota options
    /// </summary>
    public interface ITransportQuotaOptionsBuilder
    {
        /// <summary>
        /// Set channel lifetime
        /// </summary>
        /// <param name="channelLifetime"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetChannelLifetime(
            TimeSpan channelLifetime);

        /// <summary>
        /// Set operation timeout
        /// </summary>
        /// <param name="operationTimeout"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetOperationTimeout(
            TimeSpan operationTimeout);

        /// <summary>
        /// Set security token lifetime
        /// </summary>
        /// <param name="securityTokenLifetime"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetSecurityTokenLifetime(
            TimeSpan securityTokenLifetime);

        /// <summary>
        /// Set max buffer size
        /// </summary>
        /// <param name="maxBufferSize"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetMaxBufferSize(
            int maxBufferSize);

        /// <summary>
        /// Set max message size
        /// </summary>
        /// <param name="maxMessageSize"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetMaxMessageSize(
            int maxMessageSize);

        /// <summary>
        /// Set the max array length used
        /// </summary>
        /// <param name="maxArrayLength"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetMaxArrayLength(
            int maxArrayLength);

        /// <summary>
        /// Set max byte string length
        /// </summary>
        /// <param name="maxByteStringLength"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetMaxByteStringLength(
            int maxByteStringLength);

        /// <summary>
        /// Set max string length
        /// </summary>
        /// <param name="maxStringLength"></param>
        /// <returns></returns>
        ITransportQuotaOptionsBuilder SetMaxStringLength(
            int maxStringLength);
    }
}
