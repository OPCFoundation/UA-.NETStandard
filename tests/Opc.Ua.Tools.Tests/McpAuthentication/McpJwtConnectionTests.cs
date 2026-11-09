#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.McpAuthentication
{
    [TestFixture]
    public sealed class McpJwtConnectionTests
    {
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.Full)]
        public async Task ConnectionProfilesRegisterJwtExactlyOnce(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaCoreTools(new McpToolProfileSet(profile)).WithOpcUaConnectionTools();
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            foreach (string name in new[] { "Connect", "ConnectCertificate", "ConnectJwt" })
            {
                Assert.That(names.Count(candidate => candidate == name), Is.EqualTo(1), name);
            }
        }

        [Test]
        public async Task RealCatalogueHasExactJwtSchemaAndPreservesLegacyContracts()
        {
            await using JwtConnectionFixture fixture = new();
            McpServerTool[] tools = [.. fixture.Services.GetServices<McpServerTool>()];
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.Unique);
            AssertSchema(tools, "ConnectJwt",
                ["endpointUrl", "jwt", "securityMode", "securityPolicy", "name"], ["endpointUrl", "jwt"]);
            AssertSchema(tools, "Connect",
                ["endpointUrl", "securityMode", "securityPolicy", "authType", "username", "password",
                    "autoAcceptCerts", "name"], ["endpointUrl"]);
            AssertSchema(tools, "ConnectCertificate",
                ["endpointUrl", "certificateIdentity", "securityMode", "securityPolicy", "name"],
                ["endpointUrl", "certificateIdentity"]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RealBinderDispatchesJwtUsingDedicatedConnectorField(bool explicitOptions)
        {
            await using JwtConnectionFixture fixture = new();
            var arguments = new Dictionary<string, object?>
            {
                ["endpointUrl"] = JwtTestData.Url,
                ["jwt"] = JwtTestData.Token
            };
            if (explicitOptions)
            {
                arguments["securityMode"] = "Sign";
                arguments["securityPolicy"] = "Basic256Sha256";
                arguments["name"] = "Jwt-A";
            }
            CallToolResult result = await fixture.InvokeAsync(arguments).ConfigureAwait(false);
            string text = ((TextContentBlock)result.Content[0]).Text;
            string name = explicitOptions ? "Jwt-A" : "jwt-test.invalid-4840";
            Assert.That(text, Does.Contain("Connected to").And.Contain(name).And.Contain("Auth=Jwt"));
            Assert.That(text, Does.Not.Contain(JwtTestData.Token));
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(1));
            Assert.That(fixture.Connector.LegacyCalls, Is.Zero);
            Assert.That(fixture.Connector.Jwt, Is.EqualTo(JwtTestData.Token));
            Assert.That(fixture.Connector.Name, Is.EqualTo(name));
            Assert.That(fixture.Connector.Url, Is.EqualTo(JwtTestData.Url));
            Assert.That(fixture.Connector.Mode, Is.EqualTo(explicitOptions ? "Sign" : null));
            Assert.That(fixture.Connector.Policy, Is.EqualTo(explicitOptions ? "Basic256Sha256" : null));
            Assert.That(fixture.Manager.GetSessionInfoOrThrow(name).AuthType, Is.EqualTo("Jwt"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        [TestCase("Bearer header.payload.signature")]
        [TestCase("header.páyload.signature")]
        public async Task InvalidJwtDoesNotReplaceExistingSession(string? jwt)
        {
            await using JwtConnectionFixture fixture = new();
            await fixture.Manager.RegisterExistingSessionAsync("Jwt-A", fixture.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo original = fixture.Manager.GetSessionInfoOrThrow("Jwt-A");
            string result = await ConnectionTools.ConnectJwtAsync(
                fixture.Manager, JwtTestData.Url, jwt!, name: "Jwt-A").ConfigureAwait(false);
            AssertError(result, "BadIdentityTokenInvalid");
            Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A"), Is.SameAs(original));
            Assert.That(fixture.Connector.JwtCalls, Is.Zero);
            Assert.That(fixture.Connector.LegacyCalls, Is.Zero);
            fixture.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [Test]
        public async Task OversizedJwtDoesNotReplaceExistingSession()
        {
            await using JwtConnectionFixture fixture = new();
            fixture.Configuration.TransportQuotas!.MaxStringLength = 16;
            await fixture.Manager.RegisterExistingSessionAsync("Jwt-A", fixture.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo original = fixture.Manager.GetSessionInfoOrThrow("Jwt-A");
            string result = await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url,
                new string('a', 17), name: "Jwt-A").ConfigureAwait(false);
            AssertError(result, "BadEncodingLimitsExceeded");
            Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A"), Is.SameAs(original));
            Assert.That(fixture.Connector.JwtCalls, Is.Zero);
            fixture.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExactConfiguredTokenLimitCanReplaceSession()
        {
            await using JwtConnectionFixture fixture = new();
            fixture.Configuration.TransportQuotas!.MaxStringLength = 16;
            await fixture.Manager.RegisterExistingSessionAsync("Jwt-A", fixture.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            string originalId = fixture.Manager.GetSessionInfoOrThrow("Jwt-A").RegistrationId;
            string result = await fixture.Manager.ConnectJwtAsync("Jwt-A", JwtTestData.Url, new string('a', 16))
                .ConfigureAwait(false);
            Assert.That(result, Does.Contain("Auth=Jwt"));
            Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A").RegistrationId, Is.Not.EqualTo(originalId));
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(1));
            Assert.That(fixture.Connector.Jwt, Is.EqualTo(new string('a', 16)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task JwtToolSanitizesRawAndInnerExceptions(bool serviceError)
        {
            await using JwtConnectionFixture fixture = new();
            string secret = JwtTestData.Token;
            fixture.Connector.Error = serviceError
                ? new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "raw-" + secret)
                : new InvalidOperationException("raw-" + secret, new ArgumentException("inner-" + secret));
            string result = await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url, secret,
                name: "Jwt-A").ConfigureAwait(false);
            AssertError(result, serviceError ? "BadIdentityTokenRejected" : "BadUnexpectedError");
            Assert.That(result, Does.Not.Contain(secret).And.Not.Contain("raw-").And.Not.Contain("inner-"));
            using JsonDocument document = JsonDocument.Parse(result);
            Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(s_errorProperties));
            Assert.That(fixture.Manager.GetAllSessions(), Is.Empty);
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(1));
            Assert.That(fixture.Connector.LegacyCalls, Is.Zero);
        }

        [Test]
        public async Task JwtConnectionStatusAndResourcesNeverExposeToken()
        {
            await using JwtConnectionFixture fixture = new();
            _ = await fixture.Manager.ConnectJwtAsync("Jwt-A", JwtTestData.Url, JwtTestData.Token)
                .ConfigureAwait(false);
            string[] outputs =
            [
                ConnectionTools.GetConnectionStatus(fixture.Manager, "Jwt-A"),
                SessionResources.ListSessions(fixture.Manager),
                SessionResources.GetSession(fixture.Manager, "Jwt-A")
            ];
            foreach (string output in outputs)
            {
                Assert.That(output, Does.Not.Contain(JwtTestData.Token));
                Assert.That(output, Does.Contain("Jwt-A"));
            }
            using JsonDocument sessions = JsonDocument.Parse(outputs[1]);
            Assert.That(sessions.RootElement.GetProperty("sessionCount").GetInt32(), Is.EqualTo(1));
            Assert.That(sessions.RootElement.GetProperty("sessions")[0].GetProperty("authType").GetString(),
                Is.EqualTo("Jwt"));
        }

        [Test]
        public async Task CancellationIsPropagatedAndDoesNotReplaceExistingSession()
        {
            await using JwtConnectionFixture fixture = new();
            await fixture.Manager.RegisterExistingSessionAsync("Jwt-A", fixture.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo original = fixture.Manager.GetSessionInfoOrThrow("Jwt-A");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            TaskCanceledException error = (await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url, JwtTestData.Token,
                    name: "Jwt-A", ct: cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A"), Is.SameAs(original));
            Assert.That(fixture.Connector.JwtCalls, Is.Zero);
        }

        [Test]
        public async Task ConnectorCancellationIsNotConvertedIntoAnErrorPayload()
        {
            await using JwtConnectionFixture fixture = new();
            fixture.Connector.Error = new OperationCanceledException();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url, JwtTestData.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(1));
            Assert.That(fixture.Manager.GetAllSessions(), Is.Empty);
        }

        [TestCase("Jwt-A")]
        [TestCase("jWt-a")]
        public async Task AnotherCallerCannotReplaceOrUseJwtSession(string name)
        {
            await using JwtConnectionFixture fixture = new();
            var owner = new McpCallerIdentity("owner", "urn:jwt-test");
            var attacker = new McpCallerIdentity("attacker", "urn:jwt-test");
            OpcUaSessionManager.SessionInfo original;
            using (fixture.Callers.EnterTrustedCaller(owner))
            {
                _ = await fixture.Manager.ConnectJwtAsync("Jwt-A", JwtTestData.Url, JwtTestData.Token)
                    .ConfigureAwait(false);
                original = fixture.Manager.GetSessionInfoOrThrow("Jwt-A");
            }
            using (fixture.Callers.EnterTrustedCaller(attacker))
            {
                string denied = await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url,
                    JwtTestData.Token, name: name).ConfigureAwait(false);
                AssertError(denied, "BadUserAccessDenied");
                Assert.That(fixture.Manager.GetAllSessions(), Is.Empty);
                Assert.That(() => fixture.Manager.GetSessionOrThrow(name),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadUserAccessDenied));
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Manager.DisconnectAsync(name).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            using (fixture.Callers.EnterTrustedCaller(owner))
            {
                Assert.That(fixture.Manager.GetSessionInfoOrThrow(name), Is.SameAs(original));
                Assert.That(fixture.Manager.GetSessionOrThrow(name), Is.SameAs(fixture.Session.Object));
            }
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(1));
            fixture.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task OwnerCanReplaceJwtButNewRegistrationRetainsOwnership()
        {
            await using JwtConnectionFixture fixture = new();
            var owner = new McpCallerIdentity("owner", "urn:jwt-test");
            string originalId;
            using (fixture.Callers.EnterTrustedCaller(owner))
            {
                _ = await fixture.Manager.ConnectJwtAsync("Jwt-A", JwtTestData.Url, JwtTestData.Token)
                    .ConfigureAwait(false);
                originalId = fixture.Manager.GetSessionInfoOrThrow("Jwt-A").RegistrationId;
                _ = await fixture.Manager.ConnectJwtAsync("jWt-A", JwtTestData.Url, "new.header.signature")
                    .ConfigureAwait(false);
                Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A").RegistrationId,
                    Is.Not.EqualTo(originalId));
                Assert.That(fixture.Manager.GetSessionInfoOrThrow("Jwt-A").AuthType, Is.EqualTo("Jwt"));
            }
            Assert.That(fixture.Connector.JwtCalls, Is.EqualTo(2));
            Assert.That(fixture.Connector.Jwt, Is.EqualTo("new.header.signature"));
            fixture.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            using (fixture.Callers.EnterTrustedCaller(new McpCallerIdentity("attacker", "urn:jwt-test")))
            {
                Assert.That(() => fixture.Manager.GetSessionOrThrow("Jwt-A"),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task UnsupportedInjectedConnectorFailsExplicitlyWithoutLegacyFallback()
        {
            var legacy = new LegacyOnlyConnector();
            await using JwtConnectionFixture fixture = new(legacy);
            string result = await ConnectionTools.ConnectJwtAsync(fixture.Manager, JwtTestData.Url,
                JwtTestData.Token).ConfigureAwait(false);
            AssertError(result, "BadNotSupported");
            Assert.That(legacy.Calls, Is.Zero);
            Assert.That(fixture.Manager.GetAllSessions(), Is.Empty);
        }

        private static void AssertError(string result, string status)
        {
            using JsonDocument document = JsonDocument.Parse(result);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo(status));
            Assert.That(document.RootElement.GetProperty("message").GetString(), Is.Not.Empty);
        }

        private static void AssertSchema(McpServerTool[] tools, string name, string[] arguments, string[] required)
        {
            JsonElement schema = tools.Single(tool => tool.ProtocolTool.Name == name).ProtocolTool.InputSchema;
            Assert.That(schema.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(arguments), name);
            Assert.That(schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(required), name);
        }

        private static readonly string[] s_errorProperties = ["error", "statusCode", "message"];
    }

    internal sealed class JwtConnectionFixture : IAsyncDisposable
    {
        internal JwtConnectionFixture(IMcpSessionConnector? connector = null)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            Session.SetupGet(value => value.Connected).Returns(true);
            Session.SetupGet(value => value.ConfiguredEndpoint).Returns(
                new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(Configuration)));
            Session.SetupGet(value => value.SessionName).Returns("jwt-session");
            Session.SetupGet(value => value.SessionId).Returns(new NodeId(42));
            Session.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
            Session.SetupGet(value => value.ServerUris).Returns(new StringTable());
            Connector.Session = Session.Object;
            var services = new ServiceCollection();
            services.AddSingleton(Callers);
            services.AddSingleton<IMcpSessionConnector>(connector ?? Connector);
            services.AddSingleton(new OpcUaClientOptions { Configuration = Configuration });
            services.AddSingleton(provider => new OpcUaSessionManager(
                NullLogger<OpcUaSessionManager>.Instance, provider,
                provider.GetRequiredService<OpcUaClientOptions>(), NUnitTelemetryContext.Create()));
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaConnectionTools().WithOpcUaConnectionTools();
            Services = services.BuildServiceProvider();
            Manager = Services.GetRequiredService<OpcUaSessionManager>();
        }

        internal ServiceProvider Services { get; }
        internal OpcUaSessionManager Manager { get; }
        internal ApplicationConfiguration Configuration { get; } = JwtTestData.Configuration();
        internal Mock<ISession> Session { get; } = new();
        internal JwtRecordingConnector Connector { get; } = new();
        internal McpCallerContext Callers { get; } = new();

        internal async Task<CallToolResult> InvokeAsync(Dictionary<string, object?> arguments)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == "ConnectJwt");
            var request = new RequestContext<CallToolRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("jwt-test") },
                new CallToolRequestParams
                {
                    Name = "ConnectJwt",
                    Arguments = document.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            return await tool.InvokeAsync(request, CancellationToken.None).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            return Services.DisposeAsync();
        }
    }

    internal sealed class JwtRecordingConnector : IMcpSessionConnector
    {
        internal ISession Session { get; set; } = null!;
        internal Exception? Error { get; set; }
        internal int JwtCalls { get; private set; }
        internal int LegacyCalls { get; private set; }
        internal string? Name { get; private set; }
        internal string? Url { get; private set; }
        internal string? Jwt { get; private set; }
        internal string? Mode { get; private set; }
        internal string? Policy { get; private set; }

        public ValueTask<ISession> ConnectJwtAsync(string name, string endpointUrl, string jwt,
            string? securityMode, string? securityPolicy, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            JwtCalls++;
            Name = name;
            Url = endpointUrl;
            Jwt = jwt;
            Mode = securityMode;
            Policy = securityPolicy;
            if (Error != null)
            {
                throw Error;
            }
            return ValueTask.FromResult(Session);
        }

        public ValueTask<ISession> ConnectAsync(string name, string endpointUrl, string? securityMode,
            string? securityPolicy, string authType, string? username, string? password,
            bool autoAcceptCertificates, string? certificateIdentity, CancellationToken ct)
        {
            LegacyCalls++;
            throw new InvalidOperationException("JWT must not use legacy credentials.");
        }
    }

    internal sealed class LegacyOnlyConnector : IMcpSessionConnector
    {
        internal int Calls { get; private set; }

        public ValueTask<ISession> ConnectAsync(string name, string endpointUrl, string? securityMode,
            string? securityPolicy, string authType, string? username, string? password,
            bool autoAcceptCertificates, string? certificateIdentity, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Unsupported JWT must not fall back.");
        }
    }
}
#endif
