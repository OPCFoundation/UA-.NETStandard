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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using Ua = Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises device ownership through real encrypted channels and the standard generated push client.
    /// The platform installer is isolated; these tests do not establish device certificate activation.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    [NonParallelizable]
    public sealed class ProvisionableDevicePushChannelTests
    {
        /// <summary>
        /// Standard role checks precede ownership, distinct channel certificates cannot share a pending claim,
        /// and physical security reset releases pending registrar ownership.
        /// </summary>
        [Test]
        public async Task EncryptedPushPinsActualRegistrarUntilPhysicalSecurityReset()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "obch", Guid.NewGuid().ToString("N")[..8]);
            string applicationUri = "urn:localhost:onboarding:channel-test";
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            var url = new Uri($"opc.tcp://localhost:{port}/onboarding-channel-test");
            string password = Guid.NewGuid().ToString("N");
            var users = new LinqUserDatabase();
            Assert.That(users.CreateUser("registrar", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser, Role.SecurityAdmin]), Is.True);
            Assert.That(users.CreateUser("observer", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser]), Is.True);
            var installer = new Mock<IProvisionableDeviceSecurityInstaller>(MockBehavior.Strict);
            installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ISecretStore>(new InMemorySecretStore());
            services.AddSingleton<ICertificateManager>(provider =>
                new CertificateManager(provider.GetRequiredService<ITelemetryContext>()));
            services.AddSingleton<IProvisionableDeviceSecurityInstaller>(installer.Object);
            services.AddSingleton<IUserDatabase>(users);
            services.AddSingleton<IUserManagement>(provider =>
                new UserManagement(provider.GetRequiredService<IUserDatabase>()));
            services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "OnboardingChannelTest";
                options.ApplicationUri = applicationUri;
                options.ProductUri = "urn:opcfoundation:test:onboarding";
                options.PkiRoot = Path.Combine(root, "server");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeSignAndEncryptPolicies = true;
                options.IncludeUnsecurePolicyNone = false;
                options.IncludeEccPolicies = false;
                options.EndpointUrls.Add(url.AbsoluteUri);
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
            })
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                })
                .AddProvisionableDeviceSecurity("channel-test", applicationUri);

            ServiceProvider provider = services.BuildServiceProvider();
            IHostedService? hosted = null;
            try
            {
                hosted = provider.GetServices<IHostedService>()
                    .Single(static service => service is OpcUaServerHostedService);
                await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
                using var firstClient = new ClientFixture(telemetry: telemetry);
                using var secondClient = new ClientFixture(telemetry: telemetry);
                await firstClient.LoadClientConfigurationAsync(Path.Combine(root, "first"), "FirstRegistrar")
                    .ConfigureAwait(false);
                await secondClient.LoadClientConfigurationAsync(Path.Combine(root, "second"), "SecondRegistrar")
                    .ConfigureAwait(false);
                ProvisionableDeviceSecurity security = provider.GetRequiredService<ProvisionableDeviceSecurity>();
                using (ISession observer = await firstClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("observer", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false))
                {
                    ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await CreateSigningRequestAsync(observer, telemetry).ConfigureAwait(false));
                    Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                        Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                    await observer.CloseAsync(5000, true).ConfigureAwait(false);
                }
                using ISession first = await firstClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                using ISession second = await secondClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                ByteString firstRequest = await CreateSigningRequestAsync(first, telemetry).ConfigureAwait(false);
                Assert.That(firstRequest.IsEmpty, Is.False);
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
                ServiceResultException? otherRegistrar = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await CreateSigningRequestAsync(second, telemetry).ConfigureAwait(false));
                Assert.That(otherRegistrar!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                await security.ResetToServerDefaultsAsync().ConfigureAwait(false);
                ByteString secondRequest = await CreateSigningRequestAsync(second, telemetry).ConfigureAwait(false);

                Assert.That(secondRequest.IsEmpty, Is.False);
                Assert.That(secondRequest, Is.Not.EqualTo(firstRequest));
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
                installer.Verify(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
                installer.VerifyNoOtherCalls();
                await first.CloseAsync(5000, true).ConfigureAwait(false);
                await second.CloseAsync(5000, true).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (hosted != null)
                    {
                        await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }
        }

        /// <summary>
        /// Uses the generated method proxy and real server-held pending signing key.
        /// </summary>
        private static async ValueTask<ByteString> CreateSigningRequestAsync(
            ISession session, ITelemetryContext telemetry)
        {
            var client = new ServerConfigurationTypeClient(session, Ua.ObjectIds.ServerConfiguration, telemetry);
            return await client.CreateSigningRequestAsync(
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                string.Empty, true, Nonce.CreateRandomNonceData(32).ToByteString(), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
