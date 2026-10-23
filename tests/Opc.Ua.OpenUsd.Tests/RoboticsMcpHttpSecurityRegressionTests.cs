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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using HttpStatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Exercises both sample HTTP compositions without starting a server or an OPC UA session.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class RoboticsMcpHttpSecurityRegressionTests
    {
        /// <summary>
        /// Rejects rebinding and unapproved origins before the viewer's downstream dispatch.
        /// </summary>
        [TestCaseSource(nameof(RejectedRequests))]
        [Category("SecurityRegression")]
        public async Task SecurityF18ViewerHttpRejectsRebindingBeforeSessionUse(
            string method,
            string? host,
            string? origin)
        {
            await AssertHttpRequestAsync(
                IntentViewerClient.Program.BuildHttpMcpHost,
                "intent-viewer",
                Port,
                method,
                host,
                origin,
                HttpStatusCodes.Status403Forbidden,
                expectedDispatches: 0).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects rebinding and unapproved origins before the bin-picking bridge's downstream dispatch.
        /// </summary>
        [TestCaseSource(nameof(RejectedRequests))]
        [Category("SecurityRegression")]
        public async Task SecurityF47BinPickingHttpRejectsRebindingBeforeSessionUse(
            string method,
            string? host,
            string? origin)
        {
            await AssertHttpRequestAsync(
                BinPickingClient.Program.BuildHttpMcpHost,
                "bin-picking",
                Port,
                method,
                host,
                origin,
                HttpStatusCodes.Status403Forbidden,
                expectedDispatches: 0).ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves native and approved local browser clients on the viewer's loopback-only HTTP host.
        /// </summary>
        [TestCaseSource(nameof(ApprovedRequests))]
        [Category("SecurityRegression")]
        public async Task SecurityF18ViewerHttpPreservesLocalMcpClients(
            int port,
            string method,
            string host,
            string? origin)
        {
            await AssertHttpRequestAsync(
                IntentViewerClient.Program.BuildHttpMcpHost,
                "intent-viewer",
                port,
                method,
                host,
                origin,
                HttpStatusCodes.Status204NoContent,
                expectedDispatches: 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves native and approved local browser clients on the bin-picking loopback-only HTTP host.
        /// </summary>
        [TestCaseSource(nameof(ApprovedRequests))]
        [Category("SecurityRegression")]
        public async Task SecurityF47BinPickingHttpPreservesLocalMcpClients(
            int port,
            string method,
            string host,
            string? origin)
        {
            await AssertHttpRequestAsync(
                BinPickingClient.Program.BuildHttpMcpHost,
                "bin-picking",
                port,
                method,
                host,
                origin,
                HttpStatusCodes.Status204NoContent,
                expectedDispatches: 1).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects duplicate Host or Origin headers even when every individual value is approved.
        /// </summary>
        [TestCase("Host")]
        [TestCase("Origin")]
        [Category("SecurityRegression")]
        public async Task SecurityF18ViewerHttpRejectsAmbiguousHeaders(string duplicateHeader)
        {
            await AssertHttpRequestAsync(
                IntentViewerClient.Program.BuildHttpMcpHost,
                "intent-viewer",
                Port,
                HttpMethods.Post,
                LocalAuthority,
                LocalOrigin,
                HttpStatusCodes.Status403Forbidden,
                expectedDispatches: 0,
                duplicateHeader).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects duplicate Host or Origin headers even when every individual value is approved.
        /// </summary>
        [TestCase("Host")]
        [TestCase("Origin")]
        [Category("SecurityRegression")]
        public async Task SecurityF47BinPickingHttpRejectsAmbiguousHeaders(string duplicateHeader)
        {
            await AssertHttpRequestAsync(
                BinPickingClient.Program.BuildHttpMcpHost,
                "bin-picking",
                Port,
                HttpMethods.Post,
                LocalAuthority,
                LocalOrigin,
                HttpStatusCodes.Status403Forbidden,
                expectedDispatches: 0,
                duplicateHeader).ConfigureAwait(false);
        }

        /// <summary>
        /// Fails closed for ports that cannot identify a fixed loopback HTTP listener.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(65536)]
        [Category("SecurityRegression")]
        public async Task SecurityF18ViewerHttpRejectsInvalidPort(int port)
        {
            await AssertInvalidPortAsync(IntentViewerClient.Program.BuildHttpMcpHost, port).ConfigureAwait(false);
        }

        /// <summary>
        /// Fails closed for ports that cannot identify a fixed loopback HTTP listener.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(65536)]
        [Category("SecurityRegression")]
        public async Task SecurityF47BinPickingHttpRejectsInvalidPort(int port)
        {
            await AssertInvalidPortAsync(BinPickingClient.Program.BuildHttpMcpHost, port).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps stdio independent of HTTP while granting only the viewer's provisioned session.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF18ViewerStdioDoesNotComposeAnHttpServer()
        {
            await AssertStdioSessionGrantAsync(
                IntentViewerClient.Program.BuildStdioMcpHost, "intent-viewer").ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps stdio independent of HTTP while granting only the bin-picking provisioned session.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF47BinPickingStdioDoesNotComposeAnHttpServer()
        {
            await AssertStdioSessionGrantAsync(
                BinPickingClient.Program.BuildStdioMcpHost, "bin-picking").ConfigureAwait(false);
        }

        private static async Task AssertHttpRequestAsync(
            Func<int, WebApplication> createHost,
            string sessionName,
            int port,
            string method,
            string? host,
            string? origin,
            int expectedStatusCode,
            int expectedDispatches,
            string? duplicateHeader = null)
        {
            string otherSessionName = sessionName == "intent-viewer" ? "bin-picking" : "intent-viewer";
            using ISession intendedSession = CreateSession(sessionName);
            using ISession otherSession = CreateSession(otherSessionName);
            await using WebApplication app = createHost(port);
            OpcUaSessionManager manager = app.Services.GetRequiredService<OpcUaSessionManager>();
            McpCallerContext callers = app.Services.GetRequiredService<McpCallerContext>();
            await manager.RegisterExistingSessionAsync(
                sessionName, intendedSession, "Anonymous").ConfigureAwait(false);
            await manager.RegisterExistingSessionAsync(
                otherSessionName, otherSession, "Anonymous").ConfigureAwait(false);
            AssertAccessDenied(() => app.Services.GetRequiredService<IMcpOperationPolicy>().RequireCaller());

            string portText = port.ToString(CultureInfo.InvariantCulture);
            var context = new DefaultHttpContext
            {
                RequestServices = app.Services
            };
            context.Request.Scheme = "http";
            context.Request.Method = method;
            context.Request.Path = "/mcp";
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.LocalPort = port;
            if (host is not null)
            {
                context.Request.Headers.Host = host;
            }
            if (origin is not null)
            {
                context.Request.Headers.Origin = origin;
            }
            if (duplicateHeader is not null)
            {
                context.Request.Headers.Append(
                    duplicateHeader,
                    duplicateHeader == "Host" ? host : origin);
            }
            context.Request.Headers["Forwarded"] =
                $"for=127.0.0.1;host=\"localhost:{portText}\";proto=http";
            context.Request.Headers["X-Forwarded-Host"] = $"localhost:{portText}";
            context.Request.Headers["X-Forwarded-Port"] = portText;

            int dispatches = 0;
            app.Run(async accepted =>
            {
                McpCallerIdentity? identity = McpCallerContext.FromPrincipal(accepted.User);
                Assert.That(identity, Is.Not.Null);
                Assert.That(identity!.IsLocal, Is.False);
                using IDisposable callerScope = callers.EnterTrustedCaller(identity);
                await AssertSessionGrantAsync(
                    manager, sessionName, intendedSession, otherSessionName).ConfigureAwait(false);
                dispatches++;
                accepted.Response.StatusCode = HttpStatusCodes.Status204NoContent;
            });
            RequestDelegate pipeline = ((IApplicationBuilder)app).Build();

            await pipeline(context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(expectedStatusCode));
                Assert.That(dispatches, Is.EqualTo(expectedDispatches));
                Assert.That(context.User.Identity?.IsAuthenticated, Is.EqualTo(expectedDispatches != 0));
                Assert.That(app.Urls, Is.EqualTo([$"http://localhost:{portText}"]));
            });
        }

        private static async Task AssertStdioSessionGrantAsync(Func<IHost> createHost, string sessionName)
        {
            string otherSessionName = sessionName == "intent-viewer" ? "bin-picking" : "intent-viewer";
            using ISession intendedSession = CreateSession(sessionName);
            using ISession otherSession = CreateSession(otherSessionName);
            using IHost host = createHost();
            OpcUaSessionManager manager = host.Services.GetRequiredService<OpcUaSessionManager>();
            McpCallerContext callers = host.Services.GetRequiredService<McpCallerContext>();
            IMcpOperationPolicy policy = host.Services.GetRequiredService<IMcpOperationPolicy>();
            await manager.RegisterExistingSessionAsync(
                sessionName, intendedSession, "Anonymous").ConfigureAwait(false);
            await manager.RegisterExistingSessionAsync(
                otherSessionName, otherSession, "Anonymous").ConfigureAwait(false);

            McpCallerIdentity identity = policy.RequireCaller();
            Assert.That(identity.IsLocal, Is.True);
            Assert.That(host.Services.GetService<IServer>(), Is.Null);
            using IDisposable callerScope = callers.EnterTrustedCaller(identity);
            await AssertSessionGrantAsync(
                manager, sessionName, intendedSession, otherSessionName).ConfigureAwait(false);
        }

        private static async Task AssertSessionGrantAsync(
            OpcUaSessionManager manager,
            string sessionName,
            ISession intendedSession,
            string otherSessionName)
        {
            Assert.That(manager.IsTrustedHostCall, Is.False, "Borrowing must cross the actual caller policy boundary.");
            Assert.That(manager.GetSessionOrThrow(sessionName), Is.SameAs(intendedSession));
            Assert.That(manager.GetSessionOrThrow(), Is.SameAs(intendedSession));
            AssertAccessDenied(() => manager.GetSessionOrThrow(otherSessionName));
            AssertAccessDenied(() => manager.OperationPolicy.Require(McpCapability.IdentityAdministration));
            AssertAccessDenied(() => manager.OperationPolicy.RequireSecret(
                McpCapability.IdentityAdministration, "unconfigured"));

            ServiceResultException? denial = null;
            try
            {
                await manager.DisconnectAsync(sessionName).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                denial = exception;
            }
            Assert.That(denial, Is.Not.Null, "A session-use grant must not permit removing the host registration.");
            Assert.That(denial!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(manager.GetSessionOrThrow(sessionName), Is.SameAs(intendedSession));
        }

        private static void AssertAccessDenied(Action action)
        {
            Assert.That(action, Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        private static ISession CreateSession(string sessionName)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.SessionName).Returns(sessionName);
            session.SetupGet(value => value.SessionId).Returns(new NodeId(sessionName, 1));
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = $"opc.tcp://localhost:4840/{sessionName}",
                    Server = new ApplicationDescription { ApplicationUri = $"urn:security-test:{sessionName}" },
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                },
                EndpointConfiguration.Create()));
            session.Setup(value => value.Dispose());
            return session.Object;
        }

        private static async Task AssertInvalidPortAsync(Func<int, WebApplication> createHost, int port)
        {
            WebApplication? app = null;
            try
            {
                Assert.That(() => app = createHost(port), Throws.TypeOf<ArgumentOutOfRangeException>());
            }
            finally
            {
                if (app is not null)
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static IEnumerable<TestCaseData> RejectedRequests()
        {
            foreach (string method in new[]
            {
                HttpMethods.Post,
                HttpMethods.Get,
                HttpMethods.Options,
                HttpMethods.Delete
            })
            {
                yield return new TestCaseData(
                    method, "rebinding.attacker.invalid:5100", "http://rebinding.attacker.invalid:5100");
            }

            yield return new TestCaseData(HttpMethods.Post, null, null);
            foreach (string host in new[]
            {
                string.Empty,
                "localhost",
                "localhost:5101",
                "localhost:bad",
                "localhost:05100",
                "127.0.0.1:5101",
                "127.0.0.2:5100",
                "2130706433:5100",
                "[::1]:5101",
                "localhost.attacker.invalid:5100",
                "localhost.:5100",
                "localhost:5100, rebinding.attacker.invalid:5100",
                "localhost:5100@rebinding.attacker.invalid"
            })
            {
                yield return new TestCaseData(HttpMethods.Post, host, null);
            }

            foreach (string origin in new[]
            {
                string.Empty,
                "null",
                "http://attacker.invalid:5100",
                "http://localhost:5101",
                "https://localhost:5100",
                "http://localhost:5100/",
                "http://localhost:5100/path",
                "http://localhost:5100@attacker.invalid",
                "http://localhost:5100?query",
                "http://localhost:5100#fragment",
                "http://localhost:5100, http://attacker.invalid:5100"
            })
            {
                yield return new TestCaseData(HttpMethods.Post, LocalAuthority, origin);
            }

            yield return new TestCaseData(HttpMethods.Get, LocalAuthority, "http://attacker.invalid:5100");
            yield return new TestCaseData(HttpMethods.Options, LocalAuthority, "http://attacker.invalid:5100");
            yield return new TestCaseData(HttpMethods.Delete, LocalAuthority, "http://attacker.invalid:5100");
        }

        private static IEnumerable<TestCaseData> ApprovedRequests()
        {
            foreach (string method in new[]
            {
                HttpMethods.Post,
                HttpMethods.Get,
                HttpMethods.Options,
                HttpMethods.Delete
            })
            {
                yield return new TestCaseData(Port, method, LocalAuthority, null);
                yield return new TestCaseData(Port, method, LocalAuthority, LocalOrigin);
            }

            yield return new TestCaseData(Port, HttpMethods.Post, "127.0.0.1:5100", null);
            yield return new TestCaseData(Port, HttpMethods.Post, "127.0.0.1:5100", "http://127.0.0.1:5100");
            yield return new TestCaseData(Port, HttpMethods.Post, "[::1]:5100", null);
            yield return new TestCaseData(Port, HttpMethods.Post, "[::1]:5100", "http://[::1]:5100");
            yield return new TestCaseData(Port, HttpMethods.Post, "LOCALHOST:5100", "http://localhost:5100");
            yield return new TestCaseData(Port, HttpMethods.Post, "127.0.0.1:5100", LocalOrigin);
            yield return new TestCaseData(5201, HttpMethods.Post, "localhost:5201", "http://localhost:5201");
            yield return new TestCaseData(80, HttpMethods.Post, "localhost", "http://localhost");
            yield return new TestCaseData(80, HttpMethods.Post, "localhost:80", "http://localhost");
            yield return new TestCaseData(80, HttpMethods.Post, "127.0.0.1", "http://127.0.0.1");
            yield return new TestCaseData(80, HttpMethods.Post, "[::1]", "http://[::1]");
            yield return new TestCaseData(1, HttpMethods.Post, "localhost:1", "http://localhost:1");
            yield return new TestCaseData(65535, HttpMethods.Post, "localhost:65535", "http://localhost:65535");
        }

        private const int Port = 5100;
        private const string LocalAuthority = "localhost:5100";
        private const string LocalOrigin = "http://localhost:5100";
    }
}
#endif
