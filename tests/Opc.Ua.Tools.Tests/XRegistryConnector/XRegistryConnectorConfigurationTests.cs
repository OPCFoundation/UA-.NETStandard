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

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Hosting;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Connector;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryConnectorConfigurationTests
    {
        [TestCase(null, null, 30000L, 5000L)]
        [TestCase("00:00:02", "00:00:00.125", 2000L, 125L)]
        [TestCase("00:00:00.001", "49.17:02:47.294", 1L, 4294967294L)]
        public void NativeTimeoutConfigurationReachesTheInjectedOptionsWithoutStartingAHost(
            string? prepare, string? cleanup, long prepareMilliseconds, long cleanupMilliseconds)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration["NativeGateway:PreparedOperationTimeout"] = prepare;
            builder.Configuration["NativeGateway:CleanupTimeout"] = cleanup;
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings { Command = XRegistryConnectorCommand.Inspect });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            XRegistryBridgeNativeOptions options = provider.GetRequiredService<XRegistryBridgeNativeOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(
                    options.PreparedOperationTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(prepareMilliseconds)));
                Assert.That(options.CleanupTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(cleanupMilliseconds)));
                Assert.That(options.RequireEncryptedWrites, Is.True);
            });
        }

        [Test]
        public void InvalidNativeTimeoutConfigurationFailsBeforeHostCreation(
            [Values("NativeGateway:PreparedOperationTimeout", "NativeGateway:CleanupTimeout")] string name,
            [Values("invalid", "00:00:00", "-00:00:01", "49.17:02:47.295")] string value)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration[name] = value;
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                    new XRegistryConnectorSettings { Command = XRegistryConnectorCommand.Inspect }));
            Assert.That(exception.Message, Does.Contain(name).And.Contain("positive duration"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativePoliciesAndApplicationSecurityReachRuntimeOptionsWithoutOpeningStores(bool issued)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            string pki = Path.Combine(Path.GetTempPath(), "xregistry-options-" + Guid.NewGuid().ToString("N"));
            builder.Configuration["PkiRoot"] = pki;
            builder.Configuration["ApplicationUri"] = "urn:xregistry:test-application";
            builder.Configuration["NativeGateway:AllowedSubjects:0"] = "reader";
            builder.Configuration["NativeGateway:Users:0:UserName"] = "reader";
            builder.Configuration["NativeGateway:Users:0:PasswordSecret"] = "password-reference";
            if (issued)
            {
                builder.Configuration["NativeGateway:Issuers:0:IssuerUri"] = "https://issuer.example/";
                builder.Configuration["NativeGateway:Issuers:0:Audience"] = "registry";
                builder.Configuration["NativeGateway:Issuers:0:JwksUri"] = "https://issuer.example/keys";
            }
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.OpcUaGateway,
                    ListenAddress = new Uri("opc.tcp://localhost:4841"),
                    HttpRoot = new Uri("https://registry.example/")
                });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            OpcUaApplicationOptions application = provider.GetRequiredService<OpcUaApplicationOptions>();
            OpcUaServerOptions server = provider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            UserTokenType[] tokens = [.. provider.GetServices<OpcUaServerIdentityAuthenticatorRegistration>()
                .Where(registration => !registration.IsFallback)
                .SelectMany(
                    registration => registration.CreateAuthenticators(provider, Mock.Of<ICertificateValidatorEx>()))
                .Select(authenticator => authenticator.TokenType)];
            Assert.Multiple(() =>
            {
                Assert.That(application.ApplicationUri, Is.EqualTo("urn:xregistry:test-application"));
                Assert.That(application.PkiRoot, Is.EqualTo(pki));
                Assert.That(application.RejectSHA1SignedCertificates, Is.True);
                Assert.That(application.AutoAcceptUntrustedCertificates, Is.False);
                Assert.That(application.MinimumCertificateKeySize, Is.EqualTo(2048));
                Assert.That(server.EndpointUrls, Does.Contain("opc.tcp://localhost:4841/"));
                Assert.That(server.IncludeUnsecurePolicyNone, Is.False);
                Assert.That(
                    server.UserTokenPolicies.Any(policy => policy.TokenType == UserTokenType.UserName), Is.True);
                Assert.That(server.UserTokenPolicies.Any(policy => policy.TokenType == UserTokenType.IssuedToken),
                    Is.EqualTo(issued));
                Assert.That(tokens, Does.Not.Contain(UserTokenType.Anonymous));
                Assert.That(tokens, Does.Contain(UserTokenType.UserName));
                Assert.That(tokens, Does.Contain(UserTokenType.Certificate));
                Assert.That(tokens.Contains(UserTokenType.IssuedToken), Is.EqualTo(issued));
                Assert.That(Directory.Exists(pki), Is.False);
            });
        }

        [TestCase(UserTokenType.UserName, "reader", true)]
        [TestCase(UserTokenType.UserName, "unlisted", false)]
        [TestCase(UserTokenType.Anonymous, "reader", false)]
        [TestCase(UserTokenType.Certificate, "reader", false)]
        public async Task RegisteredNativeAuthorizationRechecksCredentialsAndRejectsMissingCertificateDataAsync(
            UserTokenType type, string subject, bool allowed)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration["NativeGateway:AllowedSubjects:0"] = "reader";
            builder.Configuration["NativeGateway:Users:0:UserName"] = "reader";
            builder.Configuration["NativeGateway:Users:0:PasswordSecret"] = "native-password";
            builder.Configuration["Secrets:native-password"] = "TEST_NATIVE_PASSWORD";
            string password = Guid.NewGuid().ToString("N");
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings { Command = XRegistryConnectorCommand.Inspect });
            builder.Services.AddSingleton<ISecretRegistry>(new SecretRegistry(new XRegistryEnvironmentSecretStore(
                builder.Configuration.GetSection("Secrets"), _ => password)));
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            XRegistryBridgeNativeOptions options = provider.GetRequiredService<XRegistryBridgeNativeOptions>();
            IUserIdentity identity = type == UserTokenType.UserName
                ? new UserIdentity(new UserNameIdentityTokenHandler(subject, Encoding.UTF8.GetBytes(password)))
                : Mock.Of<IUserIdentity>(value => value.TokenType == type && value.DisplayName == subject);
            var context = new Mock<ISessionSystemContext>();
            context.SetupGet(value => value.UserIdentity).Returns(identity);
            context.SetupGet(value => value.SessionId).Returns(new NodeId(42u));
            Assert.That(await options.AuthorizeCallerAsync!(context.Object, true, CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(allowed));
            if (allowed)
            {
                Assert.That(options.ContextFactory!(context.Object).Subject, Is.EqualTo("operator:default"));
                Assert.That(options.ContextFactory(context.Object).SessionId, Is.EqualTo("i=42"));
                builder.Configuration["NativeGateway:Users:0:Enabled"] = "false";
                Assert.That(await options.AuthorizeCallerAsync(context.Object, true, CancellationToken.None)
                    .ConfigureAwait(false), Is.False);
            }
        }

        [TestCase("Http:IsQualifiedBinding", "invalid")]
        [TestCase("Http:MaximumBodyBytes", "0")]
        [TestCase("Http:MaximumBodyBytes", "invalid")]
        public void InvalidHttpProfileBoundsRejectConfigurationBeforeAnyRequest(string field, string value)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration["Profiles:default:" + field] = value;
            Assert.Throws<ArgumentException>(() => XRegistryConnectorHost.Configure(
                builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.Inspect,
                    HttpRoot = new Uri("https://registry.example/")
                }));
        }

        [Test]
        public void MappingAndAliasProfilesReachRuntimeOptionsWithoutOpeningAConnection()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration["Profiles:default:Http:ShortLinkPrefix"] = "/_s";
            builder.Configuration["NativeGateway:MaxMappedProperties"] = "31";
            const string prefix = "NativeGateway:AttributeMappings:0:";
            builder.Configuration[prefix + "ModelPath"] = "/schemagroups";
            builder.Configuration[prefix + "Scope"] = "Group";
            builder.Configuration[prefix + "AttributePath:0"] = "score";
            builder.Configuration[prefix + "BrowsePath:0:NamespaceUri"] = "urn:registry:properties";
            builder.Configuration[prefix + "BrowsePath:0:Name"] = "Score";
            builder.Configuration[prefix + "NativeType"] = "Int16";
            builder.Configuration[prefix + "Writable"] = "true";
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.Inspect,
                    HttpRoot = new Uri("https://registry.example/")
                });
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            XRegistryBridgeNativeOptions options = provider.GetRequiredService<XRegistryBridgeNativeOptions>();
            Assert.Multiple(() =>
            {
                Assert.That(options.MaxMappedProperties, Is.EqualTo(31));
                Assert.That(options.AttributeMappings.Count, Is.EqualTo(1));
                Assert.That(options.AttributeMappings[0].AttributePath[0], Is.EqualTo("score"));
                Assert.That(options.AttributeMappings[0].BrowsePath[0].NamespaceUri,
                    Is.EqualTo("urn:registry:properties"));
                Assert.That(options.AttributeMappings[0].NativeType, Is.EqualTo(BuiltInType.Int16));
                Assert.That(options.AttributeMappings[0].Writable, Is.True);
            });
        }

        [TestCase("Scope", "invalid")]
        [TestCase("ModelPath", "/types/too/deep")]
        [TestCase("NativeType", "ExtensionObject")]
        [TestCase("BrowsePath:0:NamespaceUri", "relative")]
        [TestCase("AttributePath:0", "epoch")]
        public void InvalidNativeMappingProfilesRejectBeforeHosting(string field, string value)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            const string prefix = "NativeGateway:AttributeMappings:0:";
            builder.Configuration[prefix + "ModelPath"] = "/groups";
            builder.Configuration[prefix + "Scope"] = "Group";
            builder.Configuration[prefix + "AttributePath:0"] = "score";
            builder.Configuration[prefix + "BrowsePath:0:NamespaceUri"] = "urn:registry:properties";
            builder.Configuration[prefix + "BrowsePath:0:Name"] = "Score";
            builder.Configuration[prefix + field] = value;
            Assert.Throws<ArgumentException>(() => XRegistryConnectorHost.Configure(
                builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings { Command = XRegistryConnectorCommand.Inspect }));
        }

        [Test]
        public async Task ConfiguredHttpAliasPrefixReachesEndpointDescriptionAsync()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration["Profiles:default:Http:ShortLinkPrefix"] = "/tiny";
            XRegistryConnectorHost.Configure(builder.Services, builder.Configuration, builder.Logging,
                new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.Inspect,
                    HttpRoot = new Uri("https://registry.example/")
                });
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
                {
                    Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                    string json = request.RequestUri!.AbsolutePath switch
                    {
                        "/" => """{"registryid":"configured","specversion":"1.0-rc4","epoch":0}""",
                        "/model" => """{"groups":{}}""",
                        "/capabilities" => """{"specversions":["1.0-rc4"],"flags":["doc"],"available":{}}""",
                        _ => throw new AssertionException("Unexpected inspection request: " + request.RequestUri)
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                });
            using var client = new HttpClient(handler.Object);
            var clients = new Mock<IHttpClientFactory>();
            clients.Setup(value => value.CreateClient("xregistry-operator")).Returns(client);
            builder.Services.AddSingleton(clients.Object);
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            XRegistryEndpointDescription description = await provider.GetRequiredService<XRegistryHttpEndpoint>()
                .InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.RegistryId, Is.EqualTo("configured"));
                Assert.That(description.ShortLinkPrefix, Is.EqualTo("/tiny"));
                Assert.That(description.SupportsOperationReplay, Is.False);
            });
            clients.Verify(value => value.CreateClient("xregistry-operator"), Times.Once);
            handler.Protected().Verify("SendAsync", Times.Exactly(3),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }
    }
}
#endif
