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
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Tests for the AI hosting extensions on <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    [TestFixture]
    [Category("AI")]
    [Category("Hosting")]
    public sealed class AiHostingTests
    {
        [Test]
        public void AddAiThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerAiBuilderExtensions.AddAi(null!));
        }

        [Test]
        public void AddAiRegistersNodeManagerFactoryAndOptions()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<IAiChatClientFactory>(new StubChatClientFactory());

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAi(
                    ai => ai.PrimaryDeploymentId = "configured",
                    backend => backend.Site = InferenceSite.EdgeOffServer,
                    fallback => fallback.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();
            AiNodeManagerFactory factory = provider.GetRequiredService<AiNodeManagerFactory>();
            AiOptions aiOptions = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            InferenceBackendOptions backendOptions =
                provider.GetRequiredService<IOptions<InferenceBackendOptions>>().Value;
            var registrations = provider.GetServices<OpcUaServerNodeManagerRegistration>();

            Assert.Multiple(() =>
            {
                Assert.That(factory, Is.Not.Null);
                Assert.That(aiOptions.PrimaryDeploymentId, Is.EqualTo("configured"));
                Assert.That(backendOptions.Site, Is.EqualTo(InferenceSite.EdgeOffServer));
                Assert.That(registrations.Any(r => r.AsyncFactory is AiNodeManagerFactory), Is.True);
            });
        }

        [Test]
        public void AddAiDefaultsToChatClientBackend()
        {
            IServiceCollection services = new ServiceCollection();
            var factory = new StubChatClientFactory();
            services.AddSingleton<IAiChatClientFactory>(factory);

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAi(configureFallbackBackend: options => options.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();
            InferenceBackends backends = provider.GetRequiredService<InferenceBackends>();

            Assert.Multiple(() =>
            {
                Assert.That(backends.Primary, Is.TypeOf<ChatClientInferenceBackend>());
                Assert.That(backends.Fallback, Is.Null);
                Assert.That(factory.CreatedNames, Does.Contain(string.Empty));
            });
        }

        [Test]
        public void AddAiRegistersRestBackendWhenConfigured()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAi(
                    configureBackend: options =>
                    {
                        options.Kind = InferenceBackendKind.RestChatCompletions;
                        options.Authentication = BackendAuthentication.Anonymous;
                    },
                    configureFallbackBackend: options => options.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();
            InferenceBackends backends = provider.GetRequiredService<InferenceBackends>();

            Assert.That(backends.Primary, Is.TypeOf<RestChatCompletionsBackend>());
        }

        [Test]
        public void AddAiCreatesFallbackFromNamedOptions()
        {
            IServiceCollection services = new ServiceCollection();
            var factory = new StubChatClientFactory();
            services.AddSingleton<IAiChatClientFactory>(factory);

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAi(configureFallbackBackend: options =>
                {
                    options.Enabled = true;
                    options.Site = InferenceSite.OnServer;
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            InferenceBackends backends = provider.GetRequiredService<InferenceBackends>();
            InferenceBackendOptions fallbackOptions = provider
                .GetRequiredService<IOptionsMonitor<InferenceBackendOptions>>()
                .Get(AiNodeManagerFactory.FallbackOptionsName);

            Assert.Multiple(() =>
            {
                Assert.That(backends.Fallback, Is.TypeOf<ChatClientInferenceBackend>());
                Assert.That(fallbackOptions.Site, Is.EqualTo(InferenceSite.OnServer));
                Assert.That(factory.CreatedNames, Does.Contain(AiNodeManagerFactory.FallbackOptionsName));
            });
        }

        [Test]
        public void AddAiRequiresChatClientFactoryForDefaultBackend()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAi(configureFallbackBackend: options => options.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<InferenceBackends>());
        }

        private sealed class StubChatClientFactory : IAiChatClientFactory
        {
            public List<string> CreatedNames { get; } = [];

            public IChatClient CreateChatClient(
                string backendName,
                InferenceBackendOptions options)
            {
                CreatedNames.Add(backendName);
                return Mock.Of<IChatClient>();
            }
        }
    }
}
