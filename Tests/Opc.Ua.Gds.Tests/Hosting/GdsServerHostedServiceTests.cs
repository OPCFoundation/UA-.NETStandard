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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    [TestFixture]
    [Category("Hosting")]
    [Category("GdsHosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class GdsServerHostedServiceTests
    {
        [Test]
        public async Task AddIdentityAuthenticatorRegistersWithRunningGdsIdentityRegistry()
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(GdsServerHostedServiceTests),
                Guid.NewGuid().ToString("N"));
            string pkiRoot = Path.Combine(testRoot, "pki");
            Directory.CreateDirectory(testRoot);

            var services = new ServiceCollection();
            var authenticator = new StubAuthenticator();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            services.AddSingleton<IApplicationsDatabase>(new StubApplicationsDatabase());
            services.AddSingleton<ICertificateRequest>(new StubCertificateRequest());
            services.AddSingleton<ICertificateGroup>(new StubCertificateGroup());
            services.AddSingleton<IUserDatabase>(new StubUserDatabase());

            services.AddOpcUa()
                .AddGdsServer(options =>
                {
                    options.ApplicationName = "GdsIdentityRegistryTest";
                    options.ApplicationUri = "urn:localhost:gds-identity-registry-test";
                    options.ProductUri = "urn:localhost:gds-identity-registry-test:product";
                    options.PkiRoot = pkiRoot;
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeUnsecurePolicyNone = true;
                    options.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                        "/GdsIdentityRegistryTest");
                })
                .AddIdentityAuthenticator<StubAuthenticator>();
            services.AddSingleton(authenticator);

            using ServiceProvider provider = services.BuildServiceProvider();
            var hostedService = (GdsServerHostedService)provider.GetServices<IHostedService>().Single();

            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

                AuthenticationResult result = await WaitForAuthenticationAsync(hostedService)
                    .ConfigureAwait(false);

                Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
                Assert.That(result.Identity, Is.SameAs(authenticator.Identity));
                Assert.That(authenticator.CallCount, Is.EqualTo(1));
            }
            finally
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await hostedService.StopAsync(cts.Token).ConfigureAwait(false);

                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
        }

        [Test]
        public async Task AddIdentityAugmenterRegistersWithRunningGdsIdentityRegistry()
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(GdsServerHostedServiceTests),
                Guid.NewGuid().ToString("N"));
            string pkiRoot = Path.Combine(testRoot, "pki");
            Directory.CreateDirectory(testRoot);

            var services = new ServiceCollection();
            var authenticator = new StubAuthenticator();
            var augmenter = new StubAugmenter();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            services.AddSingleton<IApplicationsDatabase>(new StubApplicationsDatabase());
            services.AddSingleton<ICertificateRequest>(new StubCertificateRequest());
            services.AddSingleton<ICertificateGroup>(new StubCertificateGroup());
            services.AddSingleton<IUserDatabase>(new StubUserDatabase());

            services.AddOpcUa()
                .AddGdsServer(options =>
                {
                    options.ApplicationName = "GdsIdentityAugmenterRegistryTest";
                    options.ApplicationUri = "urn:localhost:gds-identity-augmenter-registry-test";
                    options.ProductUri = "urn:localhost:gds-identity-augmenter-registry-test:product";
                    options.PkiRoot = pkiRoot;
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeUnsecurePolicyNone = true;
                    options.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                        "/GdsIdentityAugmenterRegistryTest");
                })
                .AddIdentityAuthenticator<StubAuthenticator>()
                .AddIdentityAugmenter<StubAugmenter>();
            services.AddSingleton(authenticator);
            services.AddSingleton(augmenter);

            using ServiceProvider provider = services.BuildServiceProvider();
            var hostedService = (GdsServerHostedService)provider.GetServices<IHostedService>().Single();

            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

                AuthenticationResult result = await WaitForAuthenticationAsync(hostedService)
                    .ConfigureAwait(false);

                Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
                Assert.That(result.Identity, Is.SameAs(augmenter.Identity));
                Assert.That(authenticator.CallCount, Is.EqualTo(1));
                Assert.That(augmenter.CallCount, Is.EqualTo(1));
                Assert.That(augmenter.InputIdentity, Is.SameAs(authenticator.Identity));
            }
            finally
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await hostedService.StopAsync(cts.Token).ConfigureAwait(false);

                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
        }

        private static async Task<AuthenticationResult> WaitForAuthenticationAsync(
            GdsServerHostedService hostedService)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                Task executeTask = hostedService.ExecuteTask;
                if (executeTask != null && executeTask.IsCompleted)
                {
                    await executeTask.ConfigureAwait(false);
                }

                StandardServer server = GetServer(hostedService);
                if (server != null)
                {
                    try
                    {
                        IServerInternal currentInstance = server.CurrentInstance;
                        AuthenticationResult result = await currentInstance.IdentityRegistry
                            .AuthenticateAsync(CreateAuthenticationContext(currentInstance.MessageContext))
                            .ConfigureAwait(false);
                        if (result.Outcome == AuthenticationOutcome.Accepted)
                        {
                            return result;
                        }
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadServerHalted)
                    {
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for the GDS hosted service to register identity authenticators.");
            return AuthenticationResult.NotHandled;
        }

        private static AuthenticationContext CreateAuthenticationContext(
            IServiceMessageContext messageContext)
        {
            var handler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                new byte[] { 0x01 });
            var userTokenPolicy = new UserTokenPolicy(UserTokenType.IssuedToken);
            var endpointDescription = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            return new AuthenticationContext(
                handler,
                userTokenPolicy,
                endpointDescription,
                messageContext);
        }

        private static StandardServer GetServer(GdsServerHostedService hostedService)
        {
            FieldInfo field = typeof(GdsServerHostedService).GetField(
                "m_server",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (StandardServer)field.GetValue(hostedService);
        }

        private static int GetAvailablePort()
        {
            return ServerFixtureUtils.GetNextFreeIPPort();
        }

        private sealed class StubAuthenticator : IUserTokenAuthenticator
        {
            public IUserIdentity Identity { get; } = new UserIdentity();

            public int CallCount { get; private set; }

            public UserTokenType TokenType => UserTokenType.IssuedToken;

            public string IssuedTokenProfileUri => Profiles.JwtUserToken;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<AuthenticationResult>(AuthenticationResult.Accept(Identity));
            }
        }

        private sealed class StubAugmenter : IIdentityAugmenter
        {
            public IUserIdentity Identity { get; } = new UserIdentity();

            public int CallCount { get; private set; }

            public IUserIdentity InputIdentity { get; private set; }

            public ValueTask<IUserIdentity> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                InputIdentity = identity;
                return new ValueTask<IUserIdentity>(Identity);
            }
        }

        private sealed class StubApplicationsDatabase : IApplicationsDatabase
        {
            public ushort NamespaceIndex { get; set; }

            public void Initialize()
            {
            }

            public NodeId RegisterApplication(ApplicationRecordDataType application)
            {
                throw new NotSupportedException();
            }

            public NodeId UpdateApplication(ApplicationRecordDataType application)
            {
                throw new NotSupportedException();
            }

            public void UnregisterApplication(NodeId applicationId)
            {
                throw new NotSupportedException();
            }

            public ApplicationRecordDataType GetApplication(NodeId applicationId)
            {
                return null;
            }

            public ApplicationRecordDataType[] FindApplications(string applicationUri)
            {
                return [];
            }

            public ServerOnNetwork[] QueryServers(
                uint startingRecordId,
                uint maxRecordsToReturn,
                string applicationName,
                string applicationUri,
                string productUri,
                ArrayOf<string> serverCapabilities,
                out DateTimeUtc lastCounterResetTime)
            {
                lastCounterResetTime = DateTimeUtc.MinValue;
                return [];
            }

            public bool SetApplicationCertificate(
                NodeId applicationId,
                string certificateTypeId,
                ByteString certificate)
            {
                throw new NotSupportedException();
            }

            public bool GetApplicationCertificate(
                NodeId applicationId,
                string certificateTypeId,
                out ByteString certificate)
            {
                certificate = ByteString.Empty;
                return false;
            }

            public bool SetApplicationTrustLists(
                NodeId applicationId,
                string certificateTypeId,
                string trustListId)
            {
                throw new NotSupportedException();
            }

            public bool GetApplicationTrustLists(
                NodeId applicationId,
                string certificateTypeId,
                out string trustListId)
            {
                trustListId = null;
                return false;
            }

            public ApplicationDescription[] QueryApplications(
                uint startingRecordId,
                uint maxRecordsToReturn,
                string applicationName,
                string applicationUri,
                uint applicationType,
                string productUri,
                ArrayOf<string> serverCapabilities,
                out DateTimeUtc lastCounterResetTime,
                out uint nextRecordId)
            {
                lastCounterResetTime = DateTimeUtc.MinValue;
                nextRecordId = 0;
                return [];
            }
        }

        private sealed class StubCertificateRequest : ICertificateRequest
        {
            public ushort NamespaceIndex { get; set; }

            public void Initialize()
            {
            }

            public NodeId StartSigningRequest(
                NodeId applicationId,
                string certificateGroupId,
                string certificateTypeId,
                ByteString certificateRequest,
                string authorityId)
            {
                throw new NotSupportedException();
            }

            public NodeId StartNewKeyPairRequest(
                NodeId applicationId,
                string certificateGroupId,
                string certificateTypeId,
                string subjectName,
                ArrayOf<string> domainNames,
                string privateKeyFormat,
                ReadOnlySpan<char> privateKeyPassword,
                string authorityId)
            {
                throw new NotSupportedException();
            }

            public void ApproveRequest(NodeId requestId, bool isRejected)
            {
                throw new NotSupportedException();
            }

            public void AcceptRequest(NodeId requestId, ByteString certificate)
            {
                throw new NotSupportedException();
            }

            public CertificateRequestState FinishRequest(
                NodeId applicationId,
                NodeId requestId,
                out string certificateGroupId,
                out string certificateTypeId,
                out ByteString signedCertificate,
                out ByteString privateKey)
            {
                throw new NotSupportedException();
            }

            public CertificateRequestState ReadRequest(
                NodeId applicationId,
                NodeId requestId,
                out string certificateGroupId,
                out string certificateTypeId,
                out ByteString certificateRequest,
                out string subjectName,
                out string[] domainNames,
                out string privateKeyFormat,
                out ReadOnlySpan<char> privateKeyPassword)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubCertificateGroup : ICertificateGroup
        {
            public NodeId Id { get; set; } = NodeId.Null;

            public ArrayOf<NodeId> CertificateTypes { get; set; } = [];

            public ConcurrentDictionary<NodeId, Certificate> Certificates { get; } = new();

            public CertificateGroupConfiguration Configuration { get; } = new();

            public CertificateStoreIdentifier AuthoritiesStore { get; } = new();

            public CertificateStoreIdentifier IssuerCertificatesStore { get; }

            public TrustListState DefaultTrustList { get; set; }

            public bool UpdateRequired { get; set; }

            public ICertificateGroup Create(
                string authoritiesStorePath,
                CertificateGroupConfiguration certificateGroupConfiguration,
                string issuerCertificatesStorePath)
            {
                return this;
            }

            public Task InitAsync(CancellationToken ct = default)
            {
                return Task.CompletedTask;
            }

            public Task<Certificate> CreateCACertificateAsync(
                string subjectName,
                NodeId certificateType,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<X509CRL> RevokeCertificateAsync(
                Certificate certificate,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task VerifySigningRequestAsync(
                ApplicationRecordDataType application,
                ByteString certificateRequest,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<Certificate> SigningRequestAsync(
                ApplicationRecordDataType application,
                NodeId certificateType,
                string[] domainNames,
                ByteString certificateRequest,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<X509Certificate2KeyPair> NewKeyPairRequestAsync(
                ApplicationRecordDataType application,
                NodeId certificateType,
                string subjectName,
                string[] domainNames,
                string privateKeyFormat,
                char[] privateKeyPassword,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubUserDatabase : IUserDatabase
        {
            public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
            {
                return true;
            }

            public bool DeleteUser(string userName)
            {
                return false;
            }

            public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
            {
                return false;
            }

            public ICollection<Role> GetUserRoles(string userName)
            {
                return Array.Empty<Role>();
            }

            public bool ChangePassword(
                string userName,
                ReadOnlySpan<byte> oldPassword,
                ReadOnlySpan<byte> newPassword)
            {
                return false;
            }
        }
    }
}
