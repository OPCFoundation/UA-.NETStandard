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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryConnectorNativeIdentityTests
    {
        [Test]
        public async Task UsernameSecretRotationAndDisableRevokeAnExistingIdentityAsync()
        {
            using var configuration = new ConfigurationManager
            {
                ["NativeGateway:Users:0:UserName"] = "registry-reader",
                ["NativeGateway:Users:0:PasswordSecret"] = "native-password",
                ["Secrets:native-password"] = "TEST_NATIVE_CREDENTIAL"
            };
            string original = Guid.NewGuid().ToString("N");
            string current = original;
            var registry = new SecretRegistry(new XRegistryEnvironmentSecretStore(
                configuration.GetSection("Secrets"), _ => current));
            var policy = new XRegistryConnectorNativeIdentity(registry, configuration);
            var token = new UserNameIdentityTokenHandler("registry-reader", Encoding.UTF8.GetBytes(original));
            IUserIdentity identity = await policy.VerifyUserAsync(token, CancellationToken.None).ConfigureAwait(false);
            Assert.That(await policy.IsCurrentAsync(identity, CancellationToken.None).ConfigureAwait(false), Is.True);
            current = Guid.NewGuid().ToString("N");
            Assert.That(await policy.IsCurrentAsync(identity, CancellationToken.None).ConfigureAwait(false), Is.False);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await policy.VerifyUserAsync(token, CancellationToken.None).ConfigureAwait(false));
            var renewedToken = new UserNameIdentityTokenHandler("registry-reader", Encoding.UTF8.GetBytes(current));
            IUserIdentity renewed =
                await policy.VerifyUserAsync(renewedToken, CancellationToken.None).ConfigureAwait(false);
            configuration["NativeGateway:Users:0:Enabled"] = "false";
            Assert.That(await policy.IsCurrentAsync(renewed, CancellationToken.None).ConfigureAwait(false), Is.False);
        }

        [TestCase(-1, false)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        public async Task IssuedTokenExpirationIsEnforcedOnEveryNativeOperationAsync(int offset, bool expected)
        {
            var now = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
            var clock = new Mock<TimeProvider>();
            clock.Setup(value => value.GetUtcNow()).Returns(now);
            using var configuration = new ConfigurationManager();
            var policy = new XRegistryConnectorNativeIdentity(Mock.Of<ISecretRegistry>(), configuration, clock.Object);
            var handler = new IssuedIdentityTokenHandler(new IssuedIdentityToken { PolicyId = Profiles.JwtUserToken });
            var identity = new JwtUserIdentity(handler,
                new Dictionary<string, object?> { ["exp"] = now.ToUnixTimeSeconds() + offset },
                [], [], "https://issuer.example", "operator");
            Assert.That(await policy.IsCurrentAsync(identity, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(expected));
        }

        [Test]
        public void ConfiguredUsernameAuthenticatorUsesTheSecretBackedVerifier()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            { DisableDefaults = true });
            builder.Configuration["NativeGateway:AllowedSubjects:0"] = "reader";
            builder.Configuration["NativeGateway:Users:0:UserName"] = "reader";
            builder.Configuration["NativeGateway:Users:0:PasswordSecret"] = "native-password";
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.OpcUaGateway,
                    ListenAddress = new Uri("opc.tcp://localhost:4841"),
                    HttpRoot = new Uri("https://registry.example/")
                });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<UserNamePasswordAuthenticator>().TokenType,
                Is.EqualTo(UserTokenType.UserName));
        }
    }
}
#endif
