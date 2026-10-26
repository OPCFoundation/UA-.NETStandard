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
using System.Collections;
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
using Opc.Ua.Gds.Server.AliasNames;
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
    public sealed partial class GdsServerHostedServiceTests
    {
        /// <summary>
        /// Both teardown paths cancel and join an admitted source read before disposing its shared resources.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AliasSourceReadsDrainBeforeManagerResourcesAreReleasedAsync(bool deleteAddressSpace)
        {
            var reader = new BlockingAliasSourceReader();
            using var certificateGroup = new StubCertificateGroup();
            var database = new StubApplicationsDatabase
            {
                Application = new ApplicationRecordDataType
                {
                    ApplicationUri = "urn:tests:alias-source-drain",
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = ["opc.tcp://localhost:4840"],
                    ServerCapabilities = ["ALIAS"]
                }
            };
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry));
            ApplicationsNodeManager? manager = null;
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                manager = new ApplicationsNodeManager(
                    server.CurrentInstance, fixture.Config, database,
                    new StubCertificateRequest(), certificateGroup)
                {
                    AliasNameAggregationEnabled = true,
                    AliasNameSourceReader = reader
                };
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                await manager.OnServerReadyAsync().ConfigureAwait(false);
                var applicationId = new NodeId(9001, 2);
                Task<bool> read = manager.RefreshAliasNameSourceAsync(applicationId).AsTask();
                await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task cleanup = deleteAddressSpace
                    ? manager.DeleteAddressSpaceAsync().AsTask()
                    : manager.DisposeAsync().AsTask();
                await reader.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(cleanup.IsCompleted, Is.False, "Cancellation alone does not establish source quiescence.");
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await manager.RefreshAliasNameSourceAsync(applicationId).ConfigureAwait(false));
                Task? concurrentDisposal = deleteAddressSpace ? null : manager.DisposeAsync().AsTask();
                if (concurrentDisposal is not null)
                {
                    Assert.That(concurrentDisposal.IsCompleted, Is.False);
                }
                reader.Release.TrySetResult(true);
                Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.False);
                await cleanup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (concurrentDisposal is not null)
                {
                    await concurrentDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            finally
            {
                reader.Release.TrySetResult(true);
                if (manager is not null)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

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
            using var certificateGroup = new StubCertificateGroup();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            services.AddSingleton<IApplicationsDatabase>(new StubApplicationsDatabase());
            services.AddSingleton<ICertificateRequest>(new StubCertificateRequest());
            services.AddSingleton<ICertificateGroup>(certificateGroup);
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
            int initialPreparationCount = 0;
            bool publishedDuringPreparation = false;
            certificateGroup.OnInitialize = () =>
            {
                initialPreparationCount++;
                publishedDuringPreparation |= GetServer(hostedService).CurrentInstance.NodeManager
                    .AsyncNodeManagers.Any(static manager => manager is ApplicationsNodeManager);
            };

            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

                AuthenticationResult result = await WaitForAuthenticationAsync(hostedService)
                    .ConfigureAwait(false);

                Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
                Assert.That(result.Identity, Is.SameAs(authenticator.Identity));
                Assert.That(authenticator.CallCount, Is.EqualTo(1));
                Assert.That(initialPreparationCount, Is.GreaterThan(0));
                Assert.That(publishedDuringPreparation, Is.False);
                Assert.That(
                    GetServer(hostedService).NodeManagerLifecycle.Registrations.ToArray(),
                    Has.Some.Matches<NodeManagerRegistration>(registration =>
                        registration is { NodeManager: ApplicationsNodeManager, Generation: 1 }));

                // OPC 10000-12 §7.8.3.3: without configured groups the hosted GDS
                // still serves the mandatory DefaultApplicationGroup.
                Assert.That(certificateGroup.Configuration.Id, Is.EqualTo("Default"));
                Assert.That(
                    certificateGroup.Configuration.BaseStorePath,
                    Does.StartWith(pkiRoot));
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

                AuthenticationResult result = await WaitForAugmentedAuthenticationAsync(
                    hostedService,
                    augmenter).ConfigureAwait(false);

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

        [Test]
        public async Task AddDefaultIdentityAuthenticatorsOptionsCanDisableSelfAdminProviderAsync()
        {
            await AssertSelfAdminProviderNotRegisteredAsync(builder =>
                builder.AddDefaultIdentityAuthenticators(
                    options => options.EnableGdsApplicationSelfAdminProvider = false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task DisableGdsApplicationSelfAdminProviderDisablesSelfAdminProviderAsync()
        {
            await AssertSelfAdminProviderNotRegisteredAsync(builder =>
                builder.DisableGdsApplicationSelfAdminProvider())
                .ConfigureAwait(false);
        }

        private static async Task AssertSelfAdminProviderNotRegisteredAsync(
            Func<IGdsServerBuilder, IGdsServerBuilder> configure)
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(GdsServerHostedServiceTests),
                Guid.NewGuid().ToString("N"));
            string pkiRoot = Path.Combine(testRoot, "pki");
            Directory.CreateDirectory(testRoot);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            services.AddSingleton<IApplicationsDatabase>(new StubApplicationsDatabase());
            services.AddSingleton<ICertificateRequest>(new StubCertificateRequest());
            services.AddSingleton<ICertificateGroup>(new StubCertificateGroup());
            services.AddSingleton<IUserDatabase>(new StubUserDatabase());

            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options =>
                {
                    options.ApplicationName = "GdsSelfAdminOptOutTest";
                    options.ApplicationUri = "urn:localhost:gds-self-admin-opt-out-test";
                    options.ProductUri = "urn:localhost:gds-self-admin-opt-out-test:product";
                    options.PkiRoot = pkiRoot;
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeUnsecurePolicyNone = true;
                    options.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                        "/GdsSelfAdminOptOutTest");
                });
            configure(builder);

            using ServiceProvider provider = services.BuildServiceProvider();
            var hostedService = (GdsServerHostedService)provider.GetServices<IHostedService>().Single();

            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

                IServerIdentityRegistry registry = await WaitForAnonymousAuthenticationAsync(hostedService)
                    .ConfigureAwait(false);

                Assert.That(GetRegisteredAugmenterCount(registry), Is.Zero);
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

        private static async Task<IServerIdentityRegistry> WaitForAnonymousAuthenticationAsync(
            GdsServerHostedService hostedService)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                Task executeTask = hostedService.ExecuteTask!;
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
                            .AuthenticateAsync(CreateAnonymousAuthenticationContext(currentInstance.MessageContext))
                            .ConfigureAwait(false);
                        if (result.Outcome == AuthenticationOutcome.Accepted)
                        {
                            return currentInstance.IdentityRegistry;
                        }
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadServerHalted)
                    {
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for the GDS hosted service to register default identity authenticators.");
            return new ServerIdentityRegistry();
        }

        private static AuthenticationContext CreateAnonymousAuthenticationContext(
            IServiceMessageContext messageContext)
        {
            var endpointDescription = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            return new AuthenticationContext(
                new AnonymousIdentityTokenHandler(),
                new UserTokenPolicy(UserTokenType.Anonymous),
                endpointDescription,
                messageContext);
        }

        private static int GetRegisteredAugmenterCount(IServerIdentityRegistry registry)
        {
            Assert.That(registry, Is.TypeOf<ServerIdentityRegistry>());
            FieldInfo field = typeof(ServerIdentityRegistry).GetField(
                "m_augmenters",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            var augmenters = (ICollection)field.GetValue(registry)!;
            return augmenters!.Count;
        }

        private static async Task<AuthenticationResult> WaitForAuthenticationAsync(
            GdsServerHostedService hostedService)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                Task executeTask = hostedService.ExecuteTask!;
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

        private static async Task<AuthenticationResult> WaitForAugmentedAuthenticationAsync(
            GdsServerHostedService hostedService,
            IIdentityAugmenter augmenter)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                Task executeTask = hostedService.ExecuteTask!;
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

                        // The hosted service starts listening first and only then
                        // registers authenticators and augmenters, in two separate
                        // steps. An accepted authentication therefore does not on
                        // its own prove the augmenter is in place: a call landing
                        // between the two steps returns the un-augmented identity.
                        // Waiting for this augmenter keeps the single asserted
                        // call below deterministic.
                        if (IsAugmenterRegistered(currentInstance.IdentityRegistry, augmenter))
                        {
                            return await currentInstance.IdentityRegistry
                                .AuthenticateAsync(
                                    CreateAuthenticationContext(currentInstance.MessageContext))
                                .ConfigureAwait(false);
                        }
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadServerHalted)
                    {
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for the GDS hosted service to register the identity augmenter.");
            return AuthenticationResult.NotHandled;
        }

        private static bool IsAugmenterRegistered(
            IServerIdentityRegistry registry,
            IIdentityAugmenter augmenter)
        {
            Assert.That(registry, Is.TypeOf<ServerIdentityRegistry>());
            FieldInfo field = typeof(ServerIdentityRegistry).GetField(
                "m_augmenters",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            var augmenters = (IEnumerable)field.GetValue(registry)!;

            try
            {
                foreach (object registered in augmenters!)
                {
                    if (ReferenceEquals(registered, augmenter))
                    {
                        return true;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // The registry adds augmenters under its own lock, which this
                // reflection based probe cannot take. A concurrent add
                // invalidates the enumerator; the caller retries on the next
                // poll.
            }

            return false;
        }

        private static AuthenticationContext CreateAuthenticationContext(
            IServiceMessageContext messageContext)
        {
            var handler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                [0x01]);
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
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            return (StandardServer)field.GetValue(hostedService)!;
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

            public IUserIdentity InputIdentity { get; private set; } = null!;

            public ValueTask<AuthenticationResult> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                InputIdentity = identity;
                return new ValueTask<AuthenticationResult>(AuthenticationResult.Accept(Identity));
            }
        }

        /// <summary>
        /// Observes cancellation but defers completion until the test permits teardown to finish.
        /// </summary>
        private sealed class BlockingAliasSourceReader : IAliasNameSourceReader
        {
            /// <summary>
            /// Signals that the source reader owns the supplied cancellation token.
            /// </summary>
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Signals that teardown has requested cancellation.
            /// </summary>
            public TaskCompletionSource<bool> Canceled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Allows the source reader to finish after the drain has been observed.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public async ValueTask<AliasNameSourceSnapshot> ReadAsync(
                ApplicationRecordDataType application,
                CancellationToken ct = default)
            {
                using CancellationTokenRegistration registration = ct.Register(() => Canceled.TrySetResult(true));
                Started.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return new AliasNameSourceSnapshot(application.ApplicationUri!, [], []);
            }
        }

        private sealed class StubApplicationsDatabase : IApplicationsDatabase
        {
            /// <summary>
            /// Supplies one direct-read application without populating the periodic discovery query.
            /// </summary>
            public ApplicationRecordDataType? Application { get; set; }

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
                return Application!;
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
                trustListId = null!;
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
            /// <summary>
            /// Observes model preparation without depending on private lifecycle implementation types.
            /// </summary>
            public Action? OnInitialize { get; set; }

            public void Dispose()
            {
                // Nothing owned by the stub.
            }

            public NodeId Id { get; set; } = NodeId.Null;

            public ArrayOf<NodeId> CertificateTypes { get; set; } = [];

            public ConcurrentDictionary<NodeId, Certificate?> Certificates { get; } = new();

            public CertificateGroupConfiguration Configuration { get; private set; } = new();

            public CertificateStoreIdentifier AuthoritiesStore { get; } = new();

            public CertificateStoreIdentifier IssuerCertificatesStore { get; } = null!;

            public TrustListState DefaultTrustList { get; set; } = null!;

            public bool UpdateRequired { get; set; }

            public ICertificateGroup Create(
                string authoritiesStorePath,
                CertificateGroupConfiguration certificateGroupConfiguration,
                string? issuerCertificatesStorePath)
            {
                Configuration = certificateGroupConfiguration;
                return this;
            }

            public Task InitAsync(CancellationToken ct = default)
            {
                OnInitialize?.Invoke();
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

            public Task VerifySigningRequestAsync(
                ApplicationRecordDataType application,
                NodeId certificateType,
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
            /// <inheritdoc/>
            public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
            {
                return true;
            }

            /// <inheritdoc/>
            public bool DeleteUser(string userName)
            {
                return false;
            }

            /// <inheritdoc/>
            public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
            {
                return false;
            }

            /// <inheritdoc/>
            public ICollection<Role> GetUserRoles(string userName)
            {
                return Array.Empty<Role>();
            }

            /// <inheritdoc/>
            public IReadOnlyList<UserManagementDataType> GetUsers()
            {
                return [];
            }

            /// <inheritdoc/>
            public bool ChangePassword(
                string userName,
                ReadOnlySpan<byte> oldPassword,
                ReadOnlySpan<byte> newPassword)
            {
                return false;
            }

            /// <inheritdoc/>
            public bool CreateUser(
                string userName,
                ReadOnlySpan<byte> password,
                ArrayOf<Role> roles,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return true;
            }

            /// <inheritdoc/>
            public bool ResetPassword(
                string userName,
                ReadOnlySpan<byte> newPassword,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return false;
            }

            /// <inheritdoc/>
            public bool UpdateUserMetadata(
                string userName,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return false;
            }
        }
    }
}
