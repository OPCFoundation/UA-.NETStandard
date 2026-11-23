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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// The hosted server must fail closed: an anonymous session cannot change
    /// nodes that carry no role permissions, and a configuration value that
    /// cannot be parsed must not silently become the permissive default.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AnonymousAccessHostingTests
    {
        [Test]
        public void AnonymousAccessDefaultsToReadOnly()
        {
            Assert.That(new OpcUaServerOptions().AnonymousAccess, Is.EqualTo(AnonymousAccess.ReadOnly));
        }

        [Test]
        public void AnonymousReadOnlyPermissionsGrantNoMutationToAnonymous()
        {
            RolePermissionType anonymous = ServerDefaultRolePermissions.AnonymousReadOnly
                .ToArray()!
                .Single(p => p.RoleId == ObjectIds.WellKnownRole_Anonymous);
            RolePermissionType authenticated = ServerDefaultRolePermissions.AnonymousReadOnly
                .ToArray()!
                .Single(p => p.RoleId == ObjectIds.WellKnownRole_AuthenticatedUser);

            Assert.Multiple(() =>
            {
                Assert.That(
                    (PermissionType)anonymous.Permissions,
                    Is.EqualTo(ServerDefaultRolePermissions.ReadOnly));
                Assert.That(
                    (PermissionType)anonymous.Permissions &
                        (PermissionType.Write | PermissionType.Call | PermissionType.AddNode),
                    Is.EqualTo(PermissionType.None));
                Assert.That(
                    (PermissionType)authenticated.Permissions,
                    Is.EqualTo(ServerDefaultRolePermissions.All));
                Assert.That(ServerDefaultRolePermissions.Unrestricted.IsEmpty, Is.True);
            });
        }

        [Test]
        public void HostedServiceAppliesAnonymousAccessOption()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(isServer: true);
            var readOnly = new StandardServer(telemetry);
            var unrestricted = new StandardServer(telemetry);
            var custom = new StandardServer(telemetry)
            {
                NamespaceDefaultRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Operator,
                        Permissions = (uint)ServerDefaultRolePermissions.All
                    }
                ]
            };

            OpcUaServerHostedService.ApplyAnonymousAccess(readOnly, new OpcUaServerOptions());
            OpcUaServerHostedService.ApplyAnonymousAccess(
                unrestricted,
                new OpcUaServerOptions { AnonymousAccess = AnonymousAccess.Unrestricted });
            OpcUaServerHostedService.ApplyAnonymousAccess(custom, new OpcUaServerOptions());

            Assert.Multiple(() =>
            {
                Assert.That(readOnly.NamespaceDefaultRolePermissions.Count, Is.EqualTo(8));
                Assert.That(unrestricted.NamespaceDefaultRolePermissions.IsEmpty, Is.True);
                // a default the server factory supplied is kept.
                Assert.That(custom.NamespaceDefaultRolePermissions.Count, Is.EqualTo(1));
            });
        }

        [TestCase("OpcUa:Server:UserTokenPolicies:0", "UserName", UserTokenType.UserName)]
        [TestCase("OpcUa:Server:UserTokenPolicies:0", "certificate", UserTokenType.Certificate)]
        [TestCase("OpcUa:Server:UserTokenPolicies:0:TokenType", "IssuedToken", UserTokenType.IssuedToken)]
        [TestCase("OpcUa:Server:UserTokenPolicies:0:TokenType", "Anonymous", UserTokenType.Anonymous)]
        public void UserTokenPolicyBindsScalarAndObjectForm(string key, string value, UserTokenType expected)
        {
            OpcUaServerOptions options = Bind(new Dictionary<string, string?> { [key] = value });

            Assert.That(options.UserTokenPolicies, Has.Count.EqualTo(1));
            Assert.That(options.UserTokenPolicies[0].TokenType, Is.EqualTo(expected));
        }

        [TestCase("OpcUa:Server:UserTokenPolicies:0", "X509")]
        [TestCase("OpcUa:Server:UserTokenPolicies:0:TokenType", "X509")]
        [TestCase("OpcUa:Server:UserTokenPolicies:0:TokenType", "42")]
        [TestCase("OpcUa:Server:UserTokenPolicies:0:Type", "UserName")]
        [TestCase("OpcUa:Server:Identity:Defaults:EnableAnonymous", "0")]
        [TestCase("OpcUa:Server:Identity:Defaults:ClockSkewTolerance", "soon")]
        [TestCase("OpcUa:Server:IncludeUnsecurePolicyNone", "no")]
        [TestCase("OpcUa:Server:AutoAcceptUntrustedCertificates", "off")]
        [TestCase("OpcUa:Server:RejectSHA1Certificates", "1")]
        [TestCase("OpcUa:Server:MinCertificateKeySize", "2048 bits")]
        [TestCase("OpcUa:Server:MaxMessageSize", "4MB")]
        [TestCase("OpcUa:Server:MaxArrayLength", "-1")]
        [TestCase("OpcUa:Server:AnonymousAccess", "ReadWrite")]
        public void InvalidOptionValueFailsInsteadOfKeepingTheDefault(string key, string value)
        {
            ArgumentException? exception = Assert.Throws<ArgumentException>(
                () => Bind(new Dictionary<string, string?> { [key] = value }));

            Assert.That(exception!.Message, Does.Contain("invalid value"));
        }

        [Test]
        public void AnonymousAccessBindsFromConfiguration()
        {
            OpcUaServerOptions options = Bind(new Dictionary<string, string?>
            {
                ["OpcUa:Server:AnonymousAccess"] = "unrestricted"
            });

            Assert.That(options.AnonymousAccess, Is.EqualTo(AnonymousAccess.Unrestricted));
        }

        [TestCase(AnonymousAccess.ReadOnly)]
        [TestCase(AnonymousAccess.Unrestricted)]
        public async Task AnonymousSessionMutatesUnprotectedNodesOnlyWhenUnrestrictedAsync(
            AnonymousAccess anonymousAccess)
        {
            string root = Path.Combine(Path.GetTempPath(), "opcanon", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(isServer: true);
            var client = new ClientFixture(telemetry);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await client.LoadClientConfigurationAsync(Path.Combine(root, "client")).ConfigureAwait(false);
                string serverUrl = "opc.tcp://localhost:" +
                    ServerFixtureUtils.GetNextFreeIPPort().ToString(CultureInfo.InvariantCulture) + "/AnonymousAccess";

                NodeId folderId = default;
                NodeId setpointId = default;
                NodeId actuateId = default;
                int actuateCalls = 0;
                var users = new LinqUserDatabase();
                users.CreateUser("operator", "secret"u8, [Role.AuthenticatedUser]);

                var started = new TaskCompletionSource<IServerContext>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(telemetry);
                services.AddSingleton<IUserDatabase>(users);
                services.TryAddSingleton<IUserManagement, UserManagement.UserManagement>();
                services.AddOpcUa()
                    .AddServer(options =>
                    {
                        options.ApplicationName = "AnonymousAccessServer";
                        options.ApplicationUri = "urn:localhost:AnonymousAccessServer";
                        options.ProductUri = "urn:localhost:AnonymousAccessServer:Product";
                        options.PkiRoot = Path.Combine(root, "server");
                        options.EndpointUrls.Add(serverUrl);
                        options.IncludeSignAndEncryptPolicies = false;
                        options.IncludeUnsecurePolicyNone = true;
                        options.AnonymousAccess = anonymousAccess;
                        options.UserTokenPolicies.Add(
                            new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
                        options.UserTokenPolicies.Add(
                            new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                    })
                    .AddDefaultIdentityAuthenticators(options =>
                    {
                        options.EnableX509 = false;
                        options.EnableJwt = false;
                    })
                    .AddNodeManager("urn:test:anonymous-access", nodes =>
                    {
                        FolderState devices = nodes.AddFolder("Devices").Node;
                        folderId = devices.NodeId;
                        BaseVariableState setpoint = nodes
                            .AddVariable<int>("Setpoint", devices.NodeId)
                            .Writable()
                            .AsVariable<int>().Node;
                        setpoint.Value = 1;
                        setpoint.StatusCode = StatusCodes.Good;
                        setpoint.Timestamp = DateTime.UtcNow;
                        setpointId = setpoint.NodeId;
                        actuateId = nodes
                            .AddMethod("Actuate", devices.NodeId)
                            .OnCall((_, _, _, _, _) =>
                            {
                                Interlocked.Increment(ref actuateCalls);
                                return ServiceResult.Good;
                            })
                            .Node.NodeId;
                    })
                    .AddStartupTask((_, context, _) =>
                    {
                        started.TrySetResult(context);
                        return default;
                    });

                ServiceProvider provider = services.BuildServiceProvider();
                var hosted = (BackgroundService)provider.GetServices<IHostedService>().Single();
                try
                {
                    await hosted.StartAsync(cancellation.Token).ConfigureAwait(false);
                    Task completed = await Task.WhenAny(
                        started.Task,
                        hosted.ExecuteTask!,
                        Task.Delay(Timeout.Infinite, cancellation.Token)).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (completed == hosted.ExecuteTask)
                    {
                        await hosted.ExecuteTask.ConfigureAwait(false);
                        Assert.Fail("The hosted server stopped before completing startup.");
                    }
                    await started.Task.ConfigureAwait(false);

                    EndpointDescription description = (await CoreClientUtils.SelectEndpointAsync(
                        client.Config, serverUrl, false, 10000, telemetry, cancellation.Token)
                        .ConfigureAwait(false))!;
                    var endpoint = new ConfiguredEndpoint(
                        null, description, EndpointConfiguration.Create(client.Config));

                    bool expectAllowed = anonymousAccess == AnonymousAccess.Unrestricted;
                    StatusCode expected = expectAllowed ? StatusCodes.Good : StatusCodes.BadUserAccessDenied;

                    using (Client.ISession anonymous = await client.SessionFactory.CreateAsync(
                        client.Config, endpoint, false, false, "anonymous", 30000,
                        new UserIdentity(), default, cancellation.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            DataValue before = await anonymous.ReadValueAsync(
                                setpointId, cancellation.Token).ConfigureAwait(false);
                            StatusCode write = await WriteAsync(
                                anonymous, setpointId, 42, cancellation.Token).ConfigureAwait(false);
                            StatusCode call = await CallAsync(
                                anonymous, folderId, actuateId, cancellation.Token).ConfigureAwait(false);

                            Assert.Multiple(() =>
                            {
                                // reading stays possible either way.
                                Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
                                Assert.That(write, Is.EqualTo(expected));
                                Assert.That(call, Is.EqualTo(expected));
                                Assert.That(Volatile.Read(ref actuateCalls), Is.EqualTo(expectAllowed ? 1 : 0));
                            });
                        }
                        finally
                        {
                            await anonymous.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }

                    // an authenticated user is not restricted by the anonymous default.
                    using Client.ISession user = await client.SessionFactory.CreateAsync(
                        client.Config, endpoint, false, false, "operator", 30000,
                        new UserIdentity("operator", "secret"u8), default, cancellation.Token)
                        .ConfigureAwait(false);
                    try
                    {
                        int callsBefore = Volatile.Read(ref actuateCalls);
                        StatusCode write = await WriteAsync(
                            user, setpointId, 7, cancellation.Token).ConfigureAwait(false);
                        StatusCode call = await CallAsync(
                            user, folderId, actuateId, cancellation.Token).ConfigureAwait(false);

                        Assert.Multiple(() =>
                        {
                            Assert.That(write, Is.EqualTo(StatusCodes.Good));
                            Assert.That(call, Is.EqualTo(StatusCodes.Good));
                            Assert.That(Volatile.Read(ref actuateCalls), Is.EqualTo(callsBefore + 1));
                        });
                    }
                    finally
                    {
                        await user.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    cancellation.Cancel();
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try
                    {
                        await hosted.StopAsync(shutdown.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await provider.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                client.Dispose();
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                    // best effort cleanup of the temporary certificate stores.
                }
            }
        }

        private static OpcUaServerOptions Bind(IDictionary<string, string?> values)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
        }

        private static async Task<StatusCode> WriteAsync(
            Client.ISession session,
            NodeId nodeId,
            int value,
            CancellationToken cancellationToken)
        {
            WriteResponse response = await session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new()
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(value))
                    }
                },
                cancellationToken).ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<StatusCode> CallAsync(
            Client.ISession session,
            NodeId objectId,
            NodeId methodId,
            CancellationToken cancellationToken)
        {
            CallResponse response = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() { ObjectId = objectId, MethodId = methodId }
                },
                cancellationToken).ConfigureAwait(false);
            return response.Results[0].StatusCode;
        }
    }
}
