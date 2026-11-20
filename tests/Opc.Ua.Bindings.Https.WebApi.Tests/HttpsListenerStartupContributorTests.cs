// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Tests for HTTPS listener startup contributor propagation and
    /// invocation order.
    /// </summary>
    [TestFixture]
    [Category("WebApiStartupContributors")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsListenerStartupContributorTests
    {
        private static readonly string[] s_expectedContributorOrder = ["A", "B"];

        [Test]
        public async Task FactoryPropagatesContributorsToCreatedListenersAsync()
        {
            var factory = new HttpsTransportListenerFactory();
            var contributor = new RecordingContributor();
            factory.StartupContributors.Add(contributor);

            await using ITransportListener created = factory.Create(new TestTelemetryContext());
            var listener = (HttpsTransportListener)created;

            Assert.That(listener.StartupContributors, Has.Count.EqualTo(1));
            Assert.That(listener.StartupContributors[0], Is.SameAs(contributor));
        }

        [Test]
        public async Task ListenerInvokesContributorsBeforeTerminalDispatcher()
        {
            await using var listener = new HttpsTransportListener(
                Utils.UriSchemeHttps,
                new TestTelemetryContext());
            var contributor = new RecordingContributor(appBuilder => appBuilder.Use(async (context, next) =>
                {
                    context.Response.Headers["X-Contributor-Ran"] = "yes";
                    await next().ConfigureAwait(false);
                }));
            listener.StartupContributors = [contributor];

            HttpContext context = await InvokeStartupPipelineAsync(listener).ConfigureAwait(false);

            Assert.That(contributor.Invocations, Has.Count.EqualTo(1));
            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.MethodNotAllowed));
            Assert.That(context.Response.Headers["X-Contributor-Ran"], Has.Some.EqualTo("yes"));
        }

        [Test]
        public async Task MultipleContributorsRunInRegistrationOrder()
        {
            await using var listener = new HttpsTransportListener(
                Utils.UriSchemeHttps,
                new TestTelemetryContext());
            var order = new List<string>();
            listener.StartupContributors =
            [
                new RecordingContributor(appBuilder => AddOrderMiddleware(appBuilder, order, "A")),
                new RecordingContributor(appBuilder => AddOrderMiddleware(appBuilder, order, "B"))
            ];

            await InvokeStartupPipelineAsync(listener).ConfigureAwait(false);

            Assert.That(order, Is.EqualTo(s_expectedContributorOrder));
        }

        private static async Task<HttpContext> InvokeStartupPipelineAsync(HttpsTransportListener listener)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            var appBuilder = new ApplicationBuilder(serviceProvider);
            new Startup().Configure(appBuilder, listener);
            RequestDelegate app = appBuilder.Build();
            var context = new DefaultHttpContext
            {
                Request =
                {
                    Method = HttpMethods.Get
                },
                Response =
                {
                    Body = new MemoryStream()
                }
            };

            await app(context).ConfigureAwait(false);
            return context;
        }

        private static void AddOrderMiddleware(
            IApplicationBuilder appBuilder,
            List<string> order,
            string value)
        {
            appBuilder.Use(async (_, next) =>
            {
                order.Add(value);
                await next().ConfigureAwait(false);
            });
        }

        private sealed class RecordingContributor : IHttpsListenerStartupContributor
        {
            private readonly Action<IApplicationBuilder>? m_configure;

            public RecordingContributor(Action<IApplicationBuilder>? configure = null)
            {
                m_configure = configure;
            }

            public List<HttpsTransportListener> Invocations { get; } = [];

            public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
            {
                Invocations.Add(listener);
                m_configure?.Invoke(appBuilder);
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
