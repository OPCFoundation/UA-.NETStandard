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

#if XREGISTRY_HTTP_MODERN
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redaction;
using Opc.Ua.Types.Redaction;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpEndpointLeaseTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [NonParallelizable]
        public async Task AuditRecordsCallerAndUpstreamWithoutPayloadOrCredentialsAsync(bool redact)
        {
            var messages = new ConcurrentQueue<string>();
            var logger = new Mock<ILogger>();
            logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            // Matching a Log expression does not evaluate a real log call.
            // TODO: Remove when CA1873 recognizes mock expression trees.
#pragma warning disable CA1873
            logger.Setup(value => value.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(call => messages.Enqueue(call.Arguments[2].ToString()!)));
#pragma warning restore CA1873
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"schemaid":"r"}"""),
                    Document = ByteString.Empty,
                    ContentType = "text/plain"
                }
            };
            string payload = Guid.NewGuid().ToString("N");
            string credential = Guid.NewGuid().ToString("N");
            RedactionStrategies.ResetStrategy();
            try
            {
                if (redact)
                {
                    RedactionStrategies.SetStrategy(new SimpleRedactionStrategy());
                }
                await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                    HttpHostTestData.OpenOptions with
                    {
                        Transport = new XRegistryHttpOptions { Telemetry = telemetry.Object },
                        CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(
                            new XRegistryCallContext("audit-caller")
                            { Authority = "audit-authority", IsAuthenticated = true })
                    }).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Put,
                    new Uri("/registry" + HttpTestData.ResourcePath, UriKind.Relative))
                {
                    Content = new StringContent(payload, Encoding.UTF8, "text/plain")
                };
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential);
                using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
                string audit = messages.Single(
                    message => message.StartsWith("xRegistry HTTP audit:", StringComparison.Ordinal));
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(audit, Does.Contain("Replace").And.Contain("status=200"));
                    Assert.That(audit, Does.Not.Contain(payload).And.Not.Contain(credential));
                    Assert.That(audit.Contains("audit-caller", StringComparison.Ordinal), Is.EqualTo(!redact));
                    Assert.That(audit.Contains("audit-authority", StringComparison.Ordinal), Is.EqualTo(!redact));
                    Assert.That(audit.Contains("independent-provider", StringComparison.Ordinal), Is.EqualTo(!redact));
                    Assert.That(
                        audit.Contains(HttpTestData.ResourcePath, StringComparison.Ordinal), Is.EqualTo(!redact));
                });
            }
            finally
            {
                RedactionStrategies.ResetStrategy();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedOutPreparedDisposalRetainsCallerLeaseUntilCleanupCompletesAsync(bool cleanupFails)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeRegistryEndpoint
            {
                PreparedDisposeCallback = async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    if (cleanupFails)
                    {
                        throw new IOException("Injected completed preparation cleanup failure.");
                    }
                }
            };
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend, caller, () =>
                {
                    released.TrySetResult(true);
                    return default;
                })));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                HttpHostTestData.OpenOptions with
                {
                    Transport = new XRegistryHttpOptions { CleanupTimeout = TimeSpan.FromMilliseconds(50) }
                }, resolver: resolver).ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/registry/", UriKind.Relative))
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(backend.Commits, Is.EqualTo(1));
                    Assert.That(released.Task.IsCompleted, Is.False,
                        "A caller lease must not be released while its preparation still uses the endpoint.");
                });
            }
            finally
            {
                release.TrySetResult(true);
                await released.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConcurrentCallersNeverShareAnUpstreamLeaseAsync()
        {
            var first = new FakeRegistryEndpoint();
            var second = new FakeRegistryEndpoint();
            int acquired = 0;
            int released = 0;
            var resolver = new XRegistryEndpointResolver((caller, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Increment(ref acquired);
                IXRegistryEndpoint selected = caller.Subject == "alice" ? first : second;
                return new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(selected, caller, () =>
                {
                    Interlocked.Increment(ref released);
                    return default;
                }));
            });
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                first, HttpHostTestData.OpenOptions, resolver: resolver).ConfigureAwait(false);
            Task<HttpContext> alice = host.Server.SendAsync(context => SetCaller(context, "alice"));
            Task<HttpContext> bob = host.Server.SendAsync(context => SetCaller(context, "bob"));
            await Task.WhenAll(alice, bob).ConfigureAwait(false);
            HttpContext aliceResult = await alice.ConfigureAwait(false);
            HttpContext bobResult = await bob.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(aliceResult.Response.StatusCode, Is.EqualTo(200));
                Assert.That(bobResult.Response.StatusCode, Is.EqualTo(200));
                Assert.That(first.Inspections, Has.Count.EqualTo(1));
                Assert.That(second.Inspections, Has.Count.EqualTo(1));
                Assert.That(first.Requests[0].Context.Subject, Is.EqualTo("alice"));
                Assert.That(second.Requests[0].Context.Subject, Is.EqualTo("bob"));
                Assert.That(acquired, Is.EqualTo(2));
                Assert.That(released, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task RevocationAfterPreparationAbortsBeforePublicationAsync()
        {
            bool authorized = true;
            int released = 0;
            var backend = new FakeRegistryEndpoint
            {
                ExecuteCallback = (_, _) =>
                {
                    authorized = false;
                    return new ValueTask<XRegistryResponse>(new XRegistryResponse(200)
                    {
                        Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":1}""")
                    });
                }
            };
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend, caller,
                    () =>
                    {
                        released++;
                        return default;
                    },
                    _ => new ValueTask<bool>(authorized))));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions, resolver: resolver).ConfigureAwait(false);
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await host.Client.PutAsync(
                new Uri("/registry/", UriKind.Relative), content).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(backend.Commits, Is.Zero);
                Assert.That(backend.Aborts, Is.EqualTo(1));
                Assert.That(released, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ForeignOrFailedLeasesReleaseWithoutPublishingAsync(bool backendFailure)
        {
            var backend = new FakeRegistryEndpoint
            {
                InspectCallback = (_, _) => throw new IOException("Injected upstream failure.")
            };
            int released = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend,
                    backendFailure ? caller : new XRegistryCallContext("foreign"),
                    () =>
                    {
                        released++;
                        return default;
                    })));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions, resolver: resolver).ConfigureAwait(false);
            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode,
                    Is.EqualTo(backendFailure ? HttpStatusCode.BadGateway : HttpStatusCode.Forbidden));
                Assert.That(backend.Requests, Is.Empty);
                Assert.That(backend.Inspections, Has.Count.EqualTo(backendFailure ? 1 : 0));
                Assert.That(released, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CleanupFailureOrTimeoutPreservesAKnownCommittedResponseAsync(bool delayed)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response =
                    new XRegistryResponse(200) { Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":1}""") }
            };
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend, caller, async () =>
                {
                    Interlocked.Increment(ref calls);
                    try
                    {
                        if (delayed)
                        {
                            await release.Task.ConfigureAwait(false);
                        }
                        throw new IOException("Injected caller cleanup failure.");
                    }
                    finally
                    {
                        finished.TrySetResult(true);
                    }
                })));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                HttpHostTestData.OpenOptions with
                {
                    Transport = new XRegistryHttpOptions { CleanupTimeout = TimeSpan.FromMilliseconds(50) }
                }, resolver: resolver).ConfigureAwait(false);
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await host.Client.PutAsync(
                    new Uri("/registry/", UriKind.Relative), content).WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(backend.Commits, Is.EqualTo(1));
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(finished.Task.IsCompleted, Is.EqualTo(!delayed));
                });
            }
            finally
            {
                release.TrySetResult(true);
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ModelChangingResponsesUseTheCandidateSchemaBeforePublicationAsync(bool missingModel)
        {
            var backend = new FakeRegistryEndpoint
            {
                ExecuteCallback = (request, _) =>
                {
                    Assert.That(request.Parameters.ToList().Any(parameter =>
                        parameter.Name == "inline" && parameter.Value == "model"), Is.True);
                    return new ValueTask<XRegistryResponse>(new XRegistryResponse(200)
                    {
                        Metadata = HttpTestData.Json(missingModel ? /*lang=json,strict*/ """{"epoch":1}""" :
                            /*lang=json,strict*/ """
                            {"epoch":1,"model":{"groups":{"newgroups":{"singular":"newgroup","resources":{}}}},
                             "newgroupsurl":"/newgroups","newgroupscount":1,
                             "newgroups":{"one":{"newgroupid":"one","self":"/newgroups/one","epoch":0}}}
                            """)
                    });
                }
            };
            await using HttpRouteTestHost host =
                await HttpRouteTestHost.StartAsync(backend, HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using var content = new StringContent(
                /*lang=json,strict*/ """{"modelsource":{"groups":{"newgroups":{"singular":"newgroup"}}}}""",
                Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await host.Client.PutAsync(
                new Uri("/registry/", UriKind.Relative), content).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(missingModel ? HttpStatusCode.BadGateway : HttpStatusCode.OK));
            Assert.That(backend.Commits, Is.EqualTo(missingModel ? 0 : 1));
            if (!missingModel)
            {
                using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(body.RootElement.TryGetProperty("model", out _), Is.False);
                    Assert.That(body.RootElement.GetProperty("newgroupsurl").GetString(),
                        Is.EqualTo("https://public.example/registry/newgroups"));
                    Assert.That(
                        body.RootElement.GetProperty("newgroups").GetProperty("one").GetProperty("self").GetString(),
                        Is.EqualTo("https://public.example/registry/newgroups/one"));
                });
            }
        }

        [TestCase("inspect", 0)]
        [TestCase("prepare", 0)]
        [TestCase("commit", 1)]
        public async Task UncooperativeBackendPhasesRespectDeadlineAndRetainTheirLeaseAsync(string phase, int commits)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response =
                    new XRegistryResponse(200) { Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":1}""") }
            };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (phase == "inspect")
            {
                backend.InspectCallback = async (_, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return backend.Description;
                };
            }
            else if (phase == "prepare")
            {
                backend.ExecuteCallback = async (_, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return backend.Response;
                };
            }
            else
            {
                backend.CommitCallback = async _ =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return backend.Response;
                };
            }
            var resolver = new XRegistryEndpointResolver((caller, _) =>
                new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(backend, caller, () =>
                {
                    released.TrySetResult(true);
                    return default;
                })));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                HttpHostTestData.OpenOptions with
                {
                    Transport = new XRegistryHttpOptions { RequestTimeout = TimeSpan.FromMilliseconds(500) }
                }, resolver: resolver).ConfigureAwait(false);
            Task<HttpResponseMessage> pending = SendEmptyUpdateAsync(host.Client);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                using HttpResponseMessage response =
                    await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
                    Assert.That(released.Task.IsCompleted, Is.False);
                    Assert.That(backend.Commits, Is.EqualTo(commits));
                });
            }
            finally
            {
                release.TrySetResult(true);
                using HttpResponseMessage response = await pending.ConfigureAwait(false);
                await released.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            Assert.That(backend.Commits, Is.EqualTo(commits), "A late preparation must never start a new commit.");
        }

        private static async Task<HttpResponseMessage> SendEmptyUpdateAsync(HttpClient client)
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            return await client.PutAsync(new Uri("/registry/", UriKind.Relative), content).ConfigureAwait(false);
        }

        private static void SetCaller(HttpContext context, string subject)
        {
            context.Request.Method = "GET";
            context.Request.Path = "/registry/model";
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subject)], "test-authentication"));
        }
    }
}
#endif
