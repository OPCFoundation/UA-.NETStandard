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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Identity, user-token policy and startup-task behaviour of the hosted GDS
    /// (issues #4584, #4585, #4586).
    /// </summary>
    public sealed partial class GdsServerHostedServiceTests
    {
        private const string kAdminUser = "appadmin";
        private static readonly string[] s_expectedStartupOrder = ["pre", "shared", "callback"];
        private static readonly string[] s_expectedGdsOnly = ["gds"];

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task UserNameLoginKeepsUserDatabaseRolesAsync(bool registerUserManagement)
        {
            var userDatabase = new LinqUserDatabase();
            userDatabase.CreateUser(
                kAdminUser,
                "password1234"u8,
                [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin]);

            await using GdsTestHost host = await StartGdsAsync(
                nameof(UserNameLoginKeepsUserDatabaseRolesAsync),
                services =>
                {
                    services.AddSingleton<IUserDatabase>(userDatabase);
                    if (registerUserManagement)
                    {
                        services.AddSingleton<IUserManagement>(new UserManagement(userDatabase));
                    }
                },
                builder => builder.AddDefaultIdentityAuthenticators(_ => { })).ConfigureAwait(false);

            AuthenticationResult result = await host.Server.IdentityRegistry
                .AuthenticateAsync(CreateUserNameAuthenticationContext(
                    host.Server.MessageContext,
                    kAdminUser,
                    "password1234"))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            NamespaceTable namespaces = host.Server.NamespaceUris;
            Assert.That(
                result.Identity.GrantedRoleIds.ToArray(),
                Does.Contain(ExpandedNodeId.ToNodeId(ObjectIds.WellKnownRole_DiscoveryAdmin, namespaces)));
            Assert.That(
                result.Identity.GrantedRoleIds.ToArray(),
                Does.Contain(ExpandedNodeId.ToNodeId(ObjectIds.WellKnownRole_CertificateAuthorityAdmin, namespaces)));

            // neither generic authenticator replaces the GDS's own ones.
            string[] authenticatorTypes = GetRegisteredAuthenticators(host.Server.IdentityRegistry)
                .Select(a => a.GetType().Name)
                .ToArray();
            Assert.That(authenticatorTypes, Does.Not.Contain(nameof(UserNamePasswordAuthenticator)));
            Assert.That(authenticatorTypes, Does.Not.Contain(nameof(X509Authenticator)));
            Assert.That(authenticatorTypes, Does.Contain("GlobalDiscoverySampleX509Authenticator"));
        }

        [Test]
        public async Task UserNameLoginKeepsUserDatabaseRolesWithCoHostedRegularServerAsync()
        {
            var userDatabase = new LinqUserDatabase();
            userDatabase.CreateUser(
                kAdminUser,
                "password1234"u8,
                [Role.AuthenticatedUser, GdsRole.DiscoveryAdmin]);

            // AddServer registers its own default authenticator set, including the
            // generic UserName and X.509 authenticators, which the GDS also consumes.
            await using GdsTestHost host = await StartGdsAsync(
                nameof(UserNameLoginKeepsUserDatabaseRolesWithCoHostedRegularServerAsync),
                services =>
                {
                    services.AddSingleton<IUserDatabase>(userDatabase);
                    services.AddSingleton<IUserManagement>(new UserManagement(userDatabase));
                    services.AddOpcUa().AddServer(options => options.ApplicationName = "RegularServer");
                },
                _ => { }).ConfigureAwait(false);

            AuthenticationResult result = await host.Server.IdentityRegistry
                .AuthenticateAsync(CreateUserNameAuthenticationContext(
                    host.Server.MessageContext,
                    kAdminUser,
                    "password1234"))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(
                result.Identity!.GrantedRoleIds.ToArray(),
                Does.Contain(ExpandedNodeId.ToNodeId(
                    ObjectIds.WellKnownRole_DiscoveryAdmin,
                    host.Server.NamespaceUris)));
            string[] authenticatorTypes = GetRegisteredAuthenticators(host.Server.IdentityRegistry)
                .Select(a => a.GetType().Name)
                .ToArray();
            Assert.That(authenticatorTypes, Does.Not.Contain(nameof(UserNamePasswordAuthenticator)));
            Assert.That(authenticatorTypes, Does.Not.Contain(nameof(X509Authenticator)));
        }

        [Test]
        public async Task UserNameLoginRejectsWrongPasswordAsync()
        {
            var userDatabase = new LinqUserDatabase();
            userDatabase.CreateUser(kAdminUser, "password1234"u8, [Role.AuthenticatedUser]);

            await using GdsTestHost host = await StartGdsAsync(
                nameof(UserNameLoginRejectsWrongPasswordAsync),
                services =>
                {
                    services.AddSingleton<IUserDatabase>(userDatabase);
                    services.AddSingleton<IUserManagement>(new UserManagement(userDatabase));
                },
                builder => builder.AddDefaultIdentityAuthenticators(_ => { })).ConfigureAwait(false);

            AuthenticationResult result = await host.Server.IdentityRegistry
                .AuthenticateAsync(CreateUserNameAuthenticationContext(
                    host.Server.MessageContext,
                    kAdminUser,
                    "wrong"))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
        }

        [Test]
        public async Task AddIdentityAuthenticatorReplacesGdsUserNameAuthenticatorAsync()
        {
            var authenticator = new StubUserNameAuthenticator();

            await using GdsTestHost host = await StartGdsAsync(
                nameof(AddIdentityAuthenticatorReplacesGdsUserNameAuthenticatorAsync),
                services => services.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                builder =>
                {
                    builder
                        .AddDefaultIdentityAuthenticators(_ => { })
                        .AddIdentityAuthenticator<StubUserNameAuthenticator>();

                    // after AddIdentityAuthenticator, so this instance is the one resolved.
                    builder.Services.AddSingleton(authenticator);
                }).ConfigureAwait(false);

            AuthenticationResult result = await host.Server.IdentityRegistry
                .AuthenticateAsync(CreateUserNameAuthenticationContext(
                    host.Server.MessageContext,
                    "anyone",
                    "secret"))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(authenticator.Identity));
        }

        [Test]
        public async Task DisabledAnonymousIsRejectedAndNotAdvertisedAsync()
        {
            await using GdsTestHost host = await StartGdsAsync(
                nameof(DisabledAnonymousIsRejectedAndNotAdvertisedAsync),
                services => services.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                builder => builder.AddDefaultIdentityAuthenticators(
                    options => options.EnableAnonymous = false)).ConfigureAwait(false);

            AuthenticationResult result = await host.Server.IdentityRegistry
                .AuthenticateAsync(CreateAnonymousAuthenticationContext(host.Server.MessageContext))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(
                GetAdvertisedTokenTypes(host.HostedService),
                Is.EqualTo(new[] { UserTokenType.UserName }));
        }

        [Test]
        public async Task DefaultUserTokenPoliciesAdvertiseAnonymousAndUserNameAsync()
        {
            await using GdsTestHost host = await StartGdsAsync(
                nameof(DefaultUserTokenPoliciesAdvertiseAnonymousAndUserNameAsync),
                services => services.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                _ => { }).ConfigureAwait(false);

            Assert.That(
                GetAdvertisedTokenTypes(host.HostedService),
                Is.EqualTo(new[] { UserTokenType.Anonymous, UserTokenType.UserName }));
        }

        [Test]
        public async Task ConfiguredUserTokenPoliciesAreAdvertisedAsync()
        {
            await using GdsTestHost host = await StartGdsAsync(
                nameof(ConfiguredUserTokenPoliciesAreAdvertisedAsync),
                services => services.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                _ => { },
                options =>
                {
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Certificate });
                }).ConfigureAwait(false);

            Assert.That(
                GetAdvertisedTokenTypes(host.HostedService),
                Is.EqualTo(new[] { UserTokenType.UserName, UserTokenType.Certificate }));
        }

        [Test]
        public void GetUserTokenTypesHonoursOptions()
        {
            var options = new GdsServerOptions();
            Assert.That(
                GdsServerHostedService.GetUserTokenTypes(options, null),
                Is.EqualTo(new[] { UserTokenType.Anonymous, UserTokenType.UserName }));
            Assert.That(
                GdsServerHostedService.GetUserTokenTypes(
                    options,
                    new GdsDefaultIdentityAuthenticatorOptions { EnableUserNamePassword = false }),
                Is.EqualTo(new[] { UserTokenType.Anonymous }));
            Assert.That(
                GdsServerHostedService.GetUserTokenTypes(
                    options,
                    new GdsDefaultIdentityAuthenticatorOptions
                    {
                        EnableAnonymous = false,
                        EnableUserNamePassword = false
                    }),
                Is.EqualTo(new[] { UserTokenType.Certificate }));
            Assert.Throws<InvalidOperationException>(() => GdsServerHostedService.GetUserTokenTypes(
                options,
                new GdsDefaultIdentityAuthenticatorOptions
                {
                    EnableAnonymous = false,
                    EnableUserNamePassword = false,
                    EnableX509 = false
                }));

            options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
            options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
            Assert.That(
                GdsServerHostedService.GetUserTokenTypes(
                    options,
                    new GdsDefaultIdentityAuthenticatorOptions { EnableUserNamePassword = false }),
                Is.EqualTo(new[] { UserTokenType.UserName }));
        }

        [Test]
        public async Task ConfigureRolesReachesHostedGdsAsync()
        {
            await using GdsTestHost host = await StartGdsAsync(
                nameof(ConfigureRolesReachesHostedGdsAsync),
                services => services.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                builder => builder.ConfigureRoles(new ConfigurationBuilder().Build())).ConfigureAwait(false);

            IRoleManager configured = host.Provider.GetRequiredService<IRoleManager>();
            Assert.That(host.Server.RoleManager, Is.SameAs(configured));
        }

        [Test]
        public async Task StartupTasksRunInOrderAfterGdsStartedAsync()
        {
            var calls = new ConcurrentQueue<string>();
            var preStartupTask = new RecordingPreStartupTask(calls);

            await using GdsTestHost host = await StartGdsAsync(
                nameof(StartupTasksRunInOrderAfterGdsStartedAsync),
                services =>
                {
                    services.AddSingleton<IUserDatabase>(new StubUserDatabase());
                    services.AddSingleton(preStartupTask);
                    services.AddSingleton<IServerStartupTask>(new RecordingStartupTask(calls, "shared"));
                },
                builder => builder
                    .AddPreStartupTask<RecordingPreStartupTask>()
                    .AddPreStartupTask<RecordingPreStartupTask>()
                    .AddStartupTask((_, server, _) =>
                    {
                        calls.Enqueue(server.MessageContext != null ? "callback" : "no context");
                        return default;
                    })).ConfigureAwait(false);

            Assert.That(
                calls.ToArray(),
                Is.EqualTo(s_expectedStartupOrder));
        }

        [Test]
        public async Task SharedStartupTasksDoNotRunWhenRegularServerIsRegisteredAsync()
        {
            var calls = new ConcurrentQueue<string>();

            await using GdsTestHost host = await StartGdsAsync(
                nameof(SharedStartupTasksDoNotRunWhenRegularServerIsRegisteredAsync),
                services =>
                {
                    services.AddSingleton<IUserDatabase>(new StubUserDatabase());
                    services.AddSingleton<IServerStartupTask>(new RecordingStartupTask(calls, "shared"));
                    services.AddOpcUa().AddServer(options => options.ApplicationName = "RegularServer");
                },
                builder => builder.AddStartupTask((_, _, _) =>
                {
                    calls.Enqueue("gds");
                    return default;
                })).ConfigureAwait(false);

            Assert.That(calls.ToArray(), Is.EqualTo(s_expectedGdsOnly));
        }

        [Test]
        public async Task FailingStartupTaskFailsGdsStartupAsync()
        {
            string testRoot = CreateTestRoot();
            ServiceCollection services = CreateGdsServices(
                nameof(FailingStartupTaskFailsGdsStartupAsync),
                testRoot,
                s => s.AddSingleton<IUserDatabase>(new StubUserDatabase()),
                builder => builder.AddStartupTask((_, _, _) =>
                    throw new InvalidOperationException("startup task failed")),
                null!);

            using ServiceProvider provider = services.BuildServiceProvider();
            GdsServerHostedService hostedService = provider.GetServices<IHostedService>()
                .OfType<GdsServerHostedService>()
                .Single();
            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Task executeTask = hostedService.ExecuteTask!;
                Task completed = await Task.WhenAny(executeTask!, Task.Delay(TimeSpan.FromSeconds(60)))
                    .ConfigureAwait(false);

                Assert.That(completed, Is.SameAs(executeTask));
                InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await executeTask.ConfigureAwait(false));
                Assert.That(ex.Message, Is.EqualTo("startup task failed"));
            }
            finally
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await hostedService.StopAsync(cts.Token).ConfigureAwait(false);
                DeleteTestRoot(testRoot);
            }
        }

        private static async Task<GdsTestHost> StartGdsAsync(
            string name,
            Action<IServiceCollection> configureServices,
            Action<IGdsServerBuilder> configureGds,
            Action<GdsServerOptions>? configureOptions = null)
        {
            string testRoot = CreateTestRoot();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServiceCollection services = CreateGdsServices(
                name,
                testRoot,
                configureServices,
                builder =>
                {
                    configureGds(builder);

                    // registered last, so the other startup tasks have run once it signals.
                    builder.AddStartupTask((_, _, _) =>
                    {
                        started.TrySetResult(true);
                        return default;
                    });
                },
                configureOptions!);

            ServiceProvider provider = services.BuildServiceProvider();
            GdsServerHostedService hostedService = provider.GetServices<IHostedService>()
                .OfType<GdsServerHostedService>()
                .Single();
            var host = new GdsTestHost(provider, hostedService, testRoot);
            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Task completed = await Task.WhenAny(
                    started.Task,
                    hostedService.ExecuteTask!,
                    Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                if (completed == hostedService.ExecuteTask)
                {
                    await hostedService.ExecuteTask.ConfigureAwait(false);
                }
                Assert.That(started.Task.IsCompleted, Is.True, "The GDS did not start in time.");
                return host;
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static ServiceCollection CreateGdsServices(
            string name,
            string testRoot,
            Action<IServiceCollection> configureServices,
            Action<IGdsServerBuilder> configureGds,
            Action<GdsServerOptions> configureOptions)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            services.AddSingleton<IApplicationsDatabase>(new StubApplicationsDatabase());
            services.AddSingleton<ICertificateRequest>(new StubCertificateRequest());
            services.AddSingleton<ICertificateGroup>(new StubCertificateGroup());
            configureServices(services);

            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options =>
                {
                    options.ApplicationName = name;
                    options.ApplicationUri = "urn:localhost:" + name;
                    options.ProductUri = "urn:localhost:" + name + ":product";
                    options.PkiRoot = Path.Combine(testRoot, "pki");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeUnsecurePolicyNone = true;
                    options.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                        "/" + name);
                    configureOptions?.Invoke(options);
                });
            configureGds(builder);
            return services;
        }

        private static string CreateTestRoot()
        {
            string testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(GdsServerHostedServiceTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            return testRoot;
        }

        private static void DeleteTestRoot(string testRoot)
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        private static AuthenticationContext CreateUserNameAuthenticationContext(
            IServiceMessageContext messageContext,
            string userName,
            string password)
        {
            var endpointDescription = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            return new AuthenticationContext(
                new UserNameIdentityTokenHandler(userName, System.Text.Encoding.UTF8.GetBytes(password)),
                new UserTokenPolicy(UserTokenType.UserName),
                endpointDescription,
                messageContext);
        }

        private static List<IUserTokenAuthenticator> GetRegisteredAuthenticators(IServerIdentityRegistry registry)
        {
            Assert.That(registry, Is.TypeOf<ServerIdentityRegistry>());
            FieldInfo field = typeof(ServerIdentityRegistry).GetField(
                "m_order",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            return ((IEnumerable)field.GetValue(registry)!).Cast<IUserTokenAuthenticator>().ToList();
        }

        private static UserTokenType[] GetAdvertisedTokenTypes(GdsServerHostedService hostedService)
        {
            FieldInfo field = typeof(GdsServerHostedService).GetField(
                "m_application",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            var application = (IApplicationInstance)field.GetValue(hostedService)!;
            ArrayOf<UserTokenPolicy> policies =
                application!.ApplicationConfiguration!.ServerConfiguration!.UserTokenPolicies;
            var tokenTypes = new UserTokenType[policies.Count];
            for (int i = 0; i < tokenTypes.Length; i++)
            {
                tokenTypes[i] = policies[i].TokenType;
            }
            return tokenTypes;
        }

        private sealed class GdsTestHost : IAsyncDisposable
        {
            private readonly string m_testRoot;

            public GdsTestHost(
                ServiceProvider provider,
                GdsServerHostedService hostedService,
                string testRoot)
            {
                Provider = provider;
                HostedService = hostedService;
                m_testRoot = testRoot;
            }

            public ServiceProvider Provider { get; }

            public GdsServerHostedService HostedService { get; }

            public IServerInternal Server => GetServer(HostedService).CurrentInstance;

            public async ValueTask DisposeAsync()
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await HostedService.StopAsync(cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    await Provider.DisposeAsync().ConfigureAwait(false);
                    DeleteTestRoot(m_testRoot);
                }
            }
        }

        private sealed class StubUserNameAuthenticator : IUserTokenAuthenticator
        {
            public IUserIdentity Identity { get; } = new UserIdentity();

            public UserTokenType TokenType => UserTokenType.UserName;

            public string IssuedTokenProfileUri => null!;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.Accept(Identity));
            }
        }

        private sealed class RecordingPreStartupTask : IServerPreStartupTask
        {
            private readonly ConcurrentQueue<string> m_calls;

            public RecordingPreStartupTask(ConcurrentQueue<string> calls)
            {
                m_calls = calls;
            }

            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                m_calls.Enqueue("pre");
                return default;
            }
        }

        private sealed class RecordingStartupTask : IServerStartupTask
        {
            private readonly ConcurrentQueue<string> m_calls;
            private readonly string m_name;

            public RecordingStartupTask(ConcurrentQueue<string> calls, string name)
            {
                m_calls = calls;
                m_name = name;
            }

            public ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                m_calls.Enqueue(m_name);
                return default;
            }
        }
    }
}
