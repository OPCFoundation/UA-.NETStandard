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

#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Session-less Service invocation (OPC 10000-4 §6.3) in
    /// <see cref="SessionManager.ValidateRequestAsync"/> and its wiring
    /// through <see cref="SessionlessInvocationOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionlessInvocationTests
    {
        private const string kGoodAccessToken = "good.jwt.token";

        private Mock<IServerInternal> m_serverMock = null!;
        private ITelemetryContext m_telemetry = null!;
        private ApplicationConfiguration m_config = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_serverMock.Setup(s => s.IdentityRegistry).Returns(new ServerIdentityRegistry());

            m_config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
        }

        [Test]
        public void SessionlessRequestIsUnsupportedUnlessEnabled()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config);

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, CreateHttpsChannel(), NodeId.Null).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void AnUnknownSessionTokenStaysBadSessionIdInvalid(
            [Values] bool sessionlessEnabled)
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config);
            if (sessionlessEnabled)
            {
                manager.SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = true };
            }

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, CreateHttpsChannel(), new NodeId(4711u)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [TestCase(RequestType.CreateSubscription)]
        [TestCase(RequestType.Publish)]
        [TestCase(RequestType.CreateMonitoredItems)]
        [TestCase(RequestType.RegisterNodes)]
        [TestCase(RequestType.UnregisterNodes)]
        [TestCase(RequestType.CloseSession)]
        [TestCase(RequestType.Cancel)]
        public void ServicesOutsideTheSessionlessSetStillNeedASession(RequestType requestType)
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = true }
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, CreateHttpsChannel(), NodeId.Null, requestType)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void TheServiceSetLimitAppliesBeforeAValidateSessionLessRequestHandler()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config);
            int calls = 0;
            manager.ValidateSessionLessRequest += (_, args) =>
            {
                calls++;
                args.Identity = new UserIdentity();
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(
                    manager,
                    CreateHttpsChannel(),
                    NodeId.Null,
                    RequestType.CreateSubscription).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public async Task AValidateSessionLessRequestHandlerTakesPrecedenceAsync()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };
            IUserIdentity handlerIdentity = CreateUserNameIdentity("handler-user");
            manager.ValidateSessionLessRequest += (_, args) => args.Identity = handlerIdentity;

            OperationContext context = await ValidateAsync(manager, CreateHttpsChannel(), NodeId.Null)
                .ConfigureAwait(false);

            Assert.That(context.UserIdentity, Is.SameAs(handlerIdentity));
        }

        [Test]
        public async Task SessionlessRequestWithoutIdentityIsRejectedUnlessAnonymousIsAllowedAsync()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, CreateHttpsChannel(), NodeId.Null).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));

            manager.SessionlessInvocation.AllowAnonymous = true;
            OperationContext context = await ValidateAsync(manager, CreateHttpsChannel(), NodeId.Null)
                .ConfigureAwait(false);

            Assert.That(context.UserIdentity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(context.Session, Is.Null);
        }

        [Test]
        public async Task SessionlessAccessTokenIsValidatedByTheIdentityRegistryAsync()
        {
            IUserIdentity issued = CreateUserNameIdentity("token-user");
            UseJwtAuthenticator(issued);
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };

            OperationContext context = await ValidateAsync(
                manager,
                CreateHttpsChannel(),
                new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false);
            Assert.That(context.UserIdentity, Is.SameAs(issued));

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(
                    manager,
                    CreateHttpsChannel(),
                    new NodeId("forged.jwt.token", 0)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public void AnAccessTokenNoAuthenticatorHandlesIsRejected()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(
                    manager,
                    CreateHttpsChannel(),
                    new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public void SessionlessAccessTokenRequiresAnEncryptedChannel(MessageSecurityMode securityMode)
        {
            UseJwtAuthenticator(CreateUserNameIdentity("token-user"));
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(
                    manager,
                    CreateTcpChannel(securityMode),
                    new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task SessionlessAccessTokenIsAcceptedOverSignAndEncryptAsync()
        {
            IUserIdentity issued = CreateUserNameIdentity("token-user");
            UseJwtAuthenticator(issued);
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions()
            };

            OperationContext context = await ValidateAsync(
                manager,
                CreateTcpChannel(MessageSecurityMode.SignAndEncrypt),
                new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false);

            Assert.That(context.UserIdentity, Is.SameAs(issued));
        }

        [Test]
        public void AccessTokensCanBeDisabled()
        {
            UseJwtAuthenticator(CreateUserNameIdentity("token-user"));
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions { AcceptAccessTokens = false }
            };

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ValidateAsync(
                    manager,
                    CreateHttpsChannel(),
                    new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [TestCase(RequestType.Browse, true)]
        [TestCase(RequestType.BrowseNext, true)]
        [TestCase(RequestType.TranslateBrowsePathsToNodeIds, true)]
        [TestCase(RequestType.Read, true)]
        [TestCase(RequestType.HistoryRead, true)]
        [TestCase(RequestType.Write, true)]
        [TestCase(RequestType.HistoryUpdate, true)]
        [TestCase(RequestType.Call, true)]
        [TestCase(RequestType.AddNodes, true)]
        [TestCase(RequestType.AddReferences, true)]
        [TestCase(RequestType.DeleteNodes, true)]
        [TestCase(RequestType.DeleteReferences, true)]
        [TestCase(RequestType.QueryFirst, true)]
        [TestCase(RequestType.QueryNext, true)]
        [TestCase(RequestType.RegisterNodes, false)]
        [TestCase(RequestType.UnregisterNodes, false)]
        [TestCase(RequestType.CreateSubscription, false)]
        [TestCase(RequestType.CreateMonitoredItems, false)]
        [TestCase(RequestType.Publish, false)]
        [TestCase(RequestType.ActivateSession, false)]
        [TestCase(RequestType.FindServers, false)]
        public void IsSessionlessServiceFollowsTheServiceSetsOfPartFour(RequestType requestType, bool expected)
        {
            Assert.That(SessionManager.IsSessionlessService(requestType), Is.EqualTo(expected));
        }

        [Test]
        public void ValidateSessionlessRequestRejectsMissingArguments()
        {
            using var manager = new ProbeSessionManager(m_serverMock.Object, m_config);

            Assert.That(
                async () => await manager.ProbeAsync(null!, new SessionlessInvocationOptions()).ConfigureAwait(false),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await manager.ProbeAsync(CreateHttpsChannel(), null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddSessionlessInvocationRegistersTheConfiguredOptions()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "SessionlessServer";
                    o.ApplicationUri = "urn:localhost:SessionlessServer";
                    o.ProductUri = "urn:localhost:SessionlessServer:Product";
                })
                .AddSessionlessInvocation(o => o.AllowAnonymous = true);

            using ServiceProvider provider = services.BuildServiceProvider();
            SessionlessInvocationOptions? options = provider.GetService<SessionlessInvocationOptions>();

            Assert.That(options, Is.Not.Null);
            Assert.That(options!.AcceptAccessTokens, Is.True);
            Assert.That(options.AllowAnonymous, Is.True);
        }

        [Test]
        public void AddSessionlessInvocationRejectsANullBuilder()
        {
            Assert.That(
                () => OpcUaServerBuilderExtensions.AddSessionlessInvocation(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void TheHostedServerAppliesTheOptionsToItsSessionManager()
        {
            var options = new SessionlessInvocationOptions();
            var services = new ServiceCollection();
            services.AddSingleton(options);
            using ServiceProvider provider = services.BuildServiceProvider();
            using var server = new SessionManagerProbeServer(provider, m_telemetry);

            ISessionManager manager = server.CreateManager(m_serverMock.Object, m_config);
            try
            {
                Assert.That(manager, Is.InstanceOf<SessionManager>());
                Assert.That(((SessionManager)manager).SessionlessInvocation, Is.SameAs(options));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public void TheHostedServerAppliesTheOptionsToAFactoryCreatedSessionManager()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "SessionlessServer";
                    o.ApplicationUri = "urn:localhost:SessionlessServer";
                    o.ProductUri = "urn:localhost:SessionlessServer:Product";
                })
                .AddSessionManager((_, server, configuration) => new SessionManager(server, configuration))
                .AddSessionlessInvocation();
            using ServiceProvider provider = services.BuildServiceProvider();
            using var server = new SessionManagerProbeServer(provider, m_telemetry);

            using var manager = (SessionManager)server.CreateManager(m_serverMock.Object, m_config);

            Assert.That(manager.SessionlessInvocation, Is.SameAs(provider.GetService<SessionlessInvocationOptions>()));
        }

        [Test]
        public void TheHostedServerKeepsOptionsTheSessionManagerAlreadyHas()
        {
            var configured = new SessionlessInvocationOptions { AllowAnonymous = true };
            using var manager = new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = configured
            };
            var services = new ServiceCollection();
            services.AddSingleton(new SessionlessInvocationOptions());
            services.AddSingleton<ISessionManager>(manager);
            using ServiceProvider provider = services.BuildServiceProvider();
            using var server = new SessionManagerProbeServer(provider, m_telemetry);

            Assert.That(server.CreateManager(m_serverMock.Object, m_config), Is.SameAs(manager));
            Assert.That(manager.SessionlessInvocation, Is.SameAs(configured));
        }

        private static ValueTask<OperationContext> ValidateAsync(
            SessionManager manager,
            SecureChannelContext channel,
            NodeId authenticationToken,
            RequestType requestType = RequestType.Read)
        {
            return manager.ValidateRequestAsync(
                new RequestHeader { AuthenticationToken = authenticationToken },
                channel,
                requestType,
                RequestLifetime.None);
        }

        private static SecureChannelContext CreateHttpsChannel()
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "https://localhost:4843/",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            return new SecureChannelContext("rest", endpoint, RequestEncoding.Json);
        }

        private static SecureChannelContext CreateTcpChannel(MessageSecurityMode securityMode)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityMode == MessageSecurityMode.None
                    ? SecurityPolicies.None
                    : SecurityPolicies.Basic256Sha256
            };
            return new SecureChannelContext("tcp", endpoint, RequestEncoding.Binary);
        }

        private static IUserIdentity CreateUserNameIdentity(string userName)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(userName);
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            return identity.Object;
        }

        private void UseJwtAuthenticator(IUserIdentity issued)
        {
            var authenticator = new Mock<IUserTokenAuthenticator>();
            authenticator.Setup(a => a.TokenType).Returns(UserTokenType.IssuedToken);
            authenticator.Setup(a => a.IssuedTokenProfileUri).Returns(Profiles.JwtUserToken);
            authenticator
                .Setup(a => a.AuthenticateAsync(It.IsAny<AuthenticationContext>(), It.IsAny<CancellationToken>()))
                .Returns<AuthenticationContext, CancellationToken>((ctx, _) =>
                    new ValueTask<AuthenticationResult>(
                        ctx.TokenHandler is IssuedIdentityTokenHandler jwt &&
                        Encoding.UTF8.GetString(jwt.DecryptedTokenData!) == kGoodAccessToken
                            ? AuthenticationResult.Accept(issued)
                            : AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadIdentityTokenRejected))));
            m_serverMock.Setup(s => s.IdentityRegistry).Returns(new ServerIdentityRegistry(authenticator.Object));
        }

        private sealed class ProbeSessionManager : SessionManager
        {
            public ProbeSessionManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public ValueTask<IUserIdentity> ProbeAsync(
                SecureChannelContext channel,
                SessionlessInvocationOptions options)
            {
                return ValidateSessionlessRequestAsync(NodeId.Null, channel, options, CancellationToken.None);
            }
        }

        private sealed class SessionManagerProbeServer : DependencyInjectionStandardServer
        {
            public SessionManagerProbeServer(IServiceProvider services, ITelemetryContext telemetry)
                : base(services, telemetry, TimeProvider.System)
            {
            }

            public ISessionManager CreateManager(IServerInternal server, ApplicationConfiguration configuration)
            {
                return CreateSessionManager(server, configuration);
            }
        }
    }
}
