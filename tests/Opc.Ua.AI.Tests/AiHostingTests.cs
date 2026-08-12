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
    public sealed class AIHostingTests
    {
        [Test]
        public void AddAIThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerAIBuilderExtensions.AddAI(null!));
        }

        [Test]
        public void AddAIRegistersNodeManagerFactoryAndOptions()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<IChatClientFactory>(new StubChatClientFactory());

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAI(
                    ai => ai.PrimaryDeploymentId = "configured",
                    backend => backend.Site = InferenceSite.EdgeOffServer,
                    fallback => fallback.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();
            AINodeManagerFactory factory = provider.GetRequiredService<AINodeManagerFactory>();
            AIOptions aiOptions = provider.GetRequiredService<IOptions<AIOptions>>().Value;
            InferenceBackendOptions backendOptions =
                provider.GetRequiredService<IOptions<InferenceBackendOptions>>().Value;
            var registrations = provider.GetServices<OpcUaServerNodeManagerRegistration>();

            Assert.Multiple(() =>
            {
                Assert.That(factory, Is.Not.Null);
                Assert.That(aiOptions.PrimaryDeploymentId, Is.EqualTo("configured"));
                Assert.That(backendOptions.Site, Is.EqualTo(InferenceSite.EdgeOffServer));
                Assert.That(registrations.Any(r => r.AsyncFactory is AINodeManagerFactory), Is.True);
            });
        }

        [Test]
        public void AddAIDefaultsToChatClientBackend()
        {
            IServiceCollection services = new ServiceCollection();
            var factory = new StubChatClientFactory();
            services.AddSingleton<IChatClientFactory>(factory);

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAI(configureFallbackBackend: options => options.Enabled = false);

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
        public void AddAIRegistersRestBackendWhenConfigured()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAI(
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
        public void AddAICreatesFallbackFromNamedOptions()
        {
            IServiceCollection services = new ServiceCollection();
            var factory = new StubChatClientFactory();
            services.AddSingleton<IChatClientFactory>(factory);

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAI(configureFallbackBackend: options =>
                {
                    options.Enabled = true;
                    options.Site = InferenceSite.OnServer;
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            InferenceBackends backends = provider.GetRequiredService<InferenceBackends>();
            InferenceBackendOptions fallbackOptions = provider
                .GetRequiredService<IOptionsMonitor<InferenceBackendOptions>>()
                .Get(AINodeManagerFactory.FallbackOptionsName);

            Assert.Multiple(() =>
            {
                Assert.That(backends.Fallback, Is.TypeOf<ChatClientInferenceBackend>());
                Assert.That(fallbackOptions.Site, Is.EqualTo(InferenceSite.OnServer));
                Assert.That(factory.CreatedNames, Does.Contain(AINodeManagerFactory.FallbackOptionsName));
            });
        }

        [Test]
        public void AddAIRequiresChatClientFactoryForDefaultBackend()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddAI(configureFallbackBackend: options => options.Enabled = false);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<InferenceBackends>());
        }

        private sealed class StubChatClientFactory : IChatClientFactory
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
