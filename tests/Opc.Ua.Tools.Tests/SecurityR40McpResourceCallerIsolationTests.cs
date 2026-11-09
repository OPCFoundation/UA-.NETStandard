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
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.SecurityRegression
{
    /// <summary>
    /// Verifies session resources run under the same trusted caller scope as MCP tool calls.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SecurityR40McpResourceCallerIsolationTests
    {
        /// <summary>
        /// Every caller-capture surface rejects incomplete host configuration before dispatch or scope changes.
        /// </summary>
        [TestCase("list", false)]
        [TestCase("list", true)]
        [TestCase("read", false)]
        [TestCase("read", true)]
        [TestCase("tool", false)]
        [TestCase("tool", true)]
        [Category("SecurityRegression")]
        public async Task SecurityR40MissingExecutionOptionsNeverDispatchesOrReplacesCallerAsync(
            string surface,
            bool authenticated)
        {
            var caller = new McpCallerIdentity("resource-reader", "urn:test:issuer");
            var outer = new McpCallerIdentity("outer-host", "urn:test:issuer");
            var callers = new McpCallerContext();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUaMcpCore();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null);
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServer server = provider.GetRequiredService<McpServer>();
            var incompleteServices = new ServiceCollection();
            incompleteServices.AddSingleton(callers);
            await using ServiceProvider incomplete = incompleteServices.BuildServiceProvider();
            McpCallerIdentity? identity = authenticated ? caller : null;
            int dispatches = 0;
            Func<Task> action = surface switch
            {
                "list" => () => McpExecutionFilters.CaptureCallerForResourceList((_, _) =>
                {
                    dispatches++;
                    return ValueTask.FromResult(new ListResourcesResult());
                })(ListRequest(server, incomplete, identity), CancellationToken.None).AsTask(),
                "read" => () => McpExecutionFilters.CaptureCallerForResourceRead((_, _) =>
                {
                    dispatches++;
                    return ValueTask.FromResult(new ReadResourceResult());
                })(ReadRequest(server, incomplete, identity), CancellationToken.None).AsTask(),
                "tool" => () => McpExecutionFilters.CaptureCaller((_, _) =>
                {
                    dispatches++;
                    return ValueTask.FromResult(new CallToolResult());
                })(new RequestContext<CallToolRequestParams>(
                    server,
                    new JsonRpcRequest { Method = "tools/call", Id = new RequestId("security-r40-tool") },
                    new CallToolRequestParams { Name = "test" })
                {
                    Services = incomplete,
                    User = Principal(identity)
                }, CancellationToken.None).AsTask(),
                _ => throw new ArgumentOutOfRangeException(nameof(surface))
            };

            using (callers.EnterTrustedCaller(outer))
            {
                await Assert.ThatAsync(action,
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo(
                            "No service for type 'Opc.Ua.Mcp.McpExecutionOptions' has been registered."))
                    .ConfigureAwait(false);
                Assert.That(dispatches, Is.Zero);
                Assert.That(callers.Current, Is.SameAs(outer));
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [Test]
        public async Task SecurityR40ResourcesHideOtherSessionsAndEnforceExactGrantAsync()
        {
            var caller = new McpCallerIdentity("resource-reader", "urn:test:issuer");
            var deniedCaller = new McpCallerIdentity("resource-outsider", "urn:test:issuer");
            var callers = new McpCallerContext();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(callers);
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    CallerGrants =
                    [
                        new McpCallerGrant { Caller = caller, SessionNames = ["visible"] },
                        new McpCallerGrant { Caller = deniedCaller }
                    ]
                }
            });
            services.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaCoreTools(McpToolProfile.Core);
            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            await using RemoteModelTestSession fixture = await RemoteModelTestSession
                .CreateAsync(false, false).ConfigureAwait(false);
            fixture.Session.SetupGet(session => session.ServerUris).Returns(new StringTable());
            _ = await manager.RegisterExistingSessionAsync(
                "visible", fixture.Session.Object, "Anonymous").ConfigureAwait(false);

            McpRequestFilter<ListResourcesRequestParams, ListResourcesResult> listFilter =
                RegisteredListResourcesFilter(provider);
            McpRequestFilter<ReadResourceRequestParams, ReadResourceResult> readFilter =
                RegisteredReadResourceFilter(provider);
            var server = provider.GetRequiredService<McpServer>();

            string? listForCaller = null;
            RequestContext<ListResourcesRequestParams> authorizedList = ListRequest(server, provider, caller);
            _ = await listFilter((_, _) =>
            {
                listForCaller = SessionResources.ListSessions(manager);
                return ValueTask.FromResult(new ListResourcesResult());
            })(authorizedList, CancellationToken.None).ConfigureAwait(false);
            using JsonDocument callerSessions = JsonDocument.Parse(listForCaller!);
            Assert.That(callerSessions.RootElement.GetProperty("sessionCount").GetInt32(), Is.EqualTo(1));
            Assert.That(callerSessions.RootElement.GetProperty("sessions")[0].GetProperty("name").GetString(),
                Is.EqualTo("visible"));

            string? listForOutsider = null;
            RequestContext<ListResourcesRequestParams> deniedList = ListRequest(server, provider, deniedCaller);
            _ = await listFilter((_, _) =>
            {
                listForOutsider = SessionResources.ListSessions(manager);
                return ValueTask.FromResult(new ListResourcesResult());
            })(deniedList, CancellationToken.None).ConfigureAwait(false);
            using JsonDocument outsiderSessions = JsonDocument.Parse(listForOutsider!);
            Assert.That(outsiderSessions.RootElement.GetProperty("sessionCount").GetInt32(), Is.Zero);
            Assert.That(outsiderSessions.RootElement.GetProperty("sessions").GetArrayLength(), Is.Zero);
            Assert.That(listForOutsider, Does.Not.Contain("opc.tcp://"));

            string? visibleDetails = null;
            RequestContext<ReadResourceRequestParams> authorizedRead = ReadRequest(server, provider, caller);
            _ = await readFilter((_, _) =>
            {
                visibleDetails = SessionResources.GetSession(manager, "visible");
                return ValueTask.FromResult(new ReadResourceResult());
            })(authorizedRead, CancellationToken.None).ConfigureAwait(false);
            using JsonDocument details = JsonDocument.Parse(visibleDetails!);
            Assert.That(details.RootElement.GetProperty("name").GetString(), Is.EqualTo("visible"));

            RequestContext<ReadResourceRequestParams> deniedRead = ReadRequest(server, provider, deniedCaller);
            int readDispatches = 0;
            await Assert.ThatAsync(
                () => readFilter((_, _) =>
                {
                    readDispatches++;
                    _ = SessionResources.GetSession(manager, "visible");
                    return ValueTask.FromResult(new ReadResourceResult());
                })(deniedRead, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(readDispatches, Is.EqualTo(1));

            RequestContext<ListResourcesRequestParams> anonymousList = ListRequest(server, provider, null);
            string? anonymousSessions = null;
            _ = await listFilter((_, _) =>
            {
                Assert.That(callers.HasRequestScope, Is.True);
                Assert.That(callers.Current, Is.Null);
                anonymousSessions = SessionResources.ListSessions(manager);
                return ValueTask.FromResult(new ListResourcesResult());
            })(anonymousList, CancellationToken.None).ConfigureAwait(false);
            using JsonDocument anonymous = JsonDocument.Parse(anonymousSessions!);
            Assert.That(anonymous.RootElement.GetProperty("sessionCount").GetInt32(), Is.Zero);
            Assert.That(anonymous.RootElement.GetProperty("sessions").GetArrayLength(), Is.Zero);
        }

        private static McpRequestFilter<ListResourcesRequestParams, ListResourcesResult>
            RegisteredListResourcesFilter(IServiceProvider services)
        {
            McpRequestFilter<ListResourcesRequestParams, ListResourcesResult>[] filters =
            [
                .. services.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request
                    .ListResourcesFilters.Where(filter =>
                        filter == McpExecutionFilters.CaptureCallerForResourceList)
            ];
            Assert.That(filters, Has.Length.EqualTo(1));
            return filters[0];
        }

        private static McpRequestFilter<ReadResourceRequestParams, ReadResourceResult>
            RegisteredReadResourceFilter(IServiceProvider services)
        {
            McpRequestFilter<ReadResourceRequestParams, ReadResourceResult>[] filters =
            [
                .. services.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request
                    .ReadResourceFilters.Where(filter =>
                        filter == McpExecutionFilters.CaptureCallerForResourceRead)
            ];
            Assert.That(filters, Has.Length.EqualTo(1));
            return filters[0];
        }

        private static RequestContext<ListResourcesRequestParams> ListRequest(
            McpServer server,
            IServiceProvider services,
            McpCallerIdentity? caller)
        {
            return new RequestContext<ListResourcesRequestParams>(
                server,
                new JsonRpcRequest { Method = "resources/list", Id = new RequestId("security-r40-list") },
                new ListResourcesRequestParams())
            {
                Services = services,
                User = Principal(caller)
            };
        }

        private static RequestContext<ReadResourceRequestParams> ReadRequest(
            McpServer server,
            IServiceProvider services,
            McpCallerIdentity? caller)
        {
            return new RequestContext<ReadResourceRequestParams>(
                server,
                new JsonRpcRequest { Method = "resources/read", Id = new RequestId("security-r40-read") },
                new ReadResourceRequestParams { Uri = "opcua://sessions/visible" })
            {
                Services = services,
                User = Principal(caller)
            };
        }

        private static ClaimsPrincipal? Principal(McpCallerIdentity? caller)
        {
            return caller is null
                ? null
                : new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, caller.Subject, ClaimValueTypes.String, caller.Issuer)],
                    "trusted-transport"));
        }
    }
}
#endif
