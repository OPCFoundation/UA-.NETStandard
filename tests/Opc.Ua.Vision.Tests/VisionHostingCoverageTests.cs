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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Hosting;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Coverage for the generic overloads
    /// <c>AddVisionMediaProvider&lt;T&gt;</c>,
    /// <c>AddVisionInferenceProvider&lt;T&gt;</c> and
    /// <c>AddVisionFeedbackSink&lt;T&gt;</c>, plus argument guards, plus
    /// <see cref="VisionHostedNodeManagerFactory"/> wiring and
    /// <see cref="VisionNodeManagerFactory.CreateAsync"/> against a real server
    /// fixture.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Hosting")]
    public sealed class VisionHostingCoverageTests
    {
        [Test]
        public void AddVisionMediaProviderGenericRegistersConcreteProviderAndRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionMediaProvider<StubMediaProvider>("Sensor1");

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionMediaProviderRegistration>().FirstOrDefault();
            var concrete = provider.GetService<StubMediaProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(concrete, Is.Not.Null,
                    "the generic overload must register TProvider so DI can inject its dependencies");
                Assert.That(registration, Is.Not.Null);
                Assert.That(registration!.SensorBrowseName, Is.EqualTo("Sensor1"));
                Assert.That(registration.Provider, Is.SameAs(concrete),
                    "the wrapping registration must resolve TProvider through DI, not new it up");
            });
        }

        [Test]
        public void AddVisionMediaProviderGenericThrowsOnNullBuilder()
        {
            Assert.That(
                () => OpcUaServerVisionBuilderExtensions
                    .AddVisionMediaProvider<StubMediaProvider>(null!, "Sensor1"),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddVisionMediaProviderGenericThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.That(
                () => builder.AddVisionMediaProvider<StubMediaProvider>(string.Empty),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("sensorBrowseName"));
        }

        [Test]
        public void AddVisionInferenceProviderGenericRegistersConcreteProviderAndRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider<StubInferenceProvider>("Pipeline1");

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionInferenceProviderRegistration>().FirstOrDefault();
            var concrete = provider.GetService<StubInferenceProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(concrete, Is.Not.Null);
                Assert.That(registration, Is.Not.Null);
                Assert.That(registration!.PipelineBrowseName, Is.EqualTo("Pipeline1"));
                Assert.That(registration.Provider, Is.SameAs(concrete));
                Assert.That(registration.OnServer, Is.True,
                    "the generic overload must default to onServer=true, matching the §8.2 default facet");
            });
        }

        [Test]
        public void AddVisionInferenceProviderGenericRespectsOnServerFalseFlag()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider<StubInferenceProvider>("Pipeline1", onServer: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionInferenceProviderRegistration>().First();

            Assert.That(registration.OnServer, Is.False);
        }

        [Test]
        public void AddVisionInferenceProviderGenericThrowsOnNullBuilder()
        {
            Assert.That(
                () => OpcUaServerVisionBuilderExtensions
                    .AddVisionInferenceProvider<StubInferenceProvider>(null!, "Pipeline1"),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddVisionInferenceProviderGenericThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.That(
                () => builder.AddVisionInferenceProvider<StubInferenceProvider>(string.Empty),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("pipelineBrowseName"));
        }

        [Test]
        public void AddVisionFeedbackSinkGenericRegistersConcreteSinkAndRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionFeedbackSink<StubFeedbackSink>("Pipeline1");

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionFeedbackSinkRegistration>().FirstOrDefault();
            var concrete = provider.GetService<StubFeedbackSink>();

            Assert.Multiple(() =>
            {
                Assert.That(concrete, Is.Not.Null);
                Assert.That(registration, Is.Not.Null);
                Assert.That(registration!.PipelineBrowseName, Is.EqualTo("Pipeline1"));
                Assert.That(registration.Sink, Is.SameAs(concrete));
            });
        }

        [Test]
        public void AddVisionFeedbackSinkGenericThrowsOnNullBuilder()
        {
            Assert.That(
                () => OpcUaServerVisionBuilderExtensions
                    .AddVisionFeedbackSink<StubFeedbackSink>(null!, "Pipeline1"),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddVisionFeedbackSinkGenericThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.That(
                () => builder.AddVisionFeedbackSink<StubFeedbackSink>(string.Empty),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("pipelineBrowseName"));
        }

        [Test]
        public void AddVisionRegistersHostedFactoryAndNodeManagerRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision();

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionNodeManagerFactory standalone = provider
                .GetRequiredService<VisionNodeManagerFactory>();
            VisionHostedNodeManagerFactory hosted = provider
                .GetRequiredService<VisionHostedNodeManagerFactory>();
            var registrations = provider.GetServices<OpcUaServerNodeManagerRegistration>();

            Assert.Multiple(() =>
            {
                Assert.That(standalone.NamespacesUris.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(hosted.NamespacesUris.Count, Is.GreaterThanOrEqualTo(1),
                    "the hosted factory must report the Vision namespace so the server registers it");
                Assert.That(registrations.Any(), Is.True,
                    "AddVision must attach a hosted node-manager registration to the server");
            });
        }

        [Test]
        public async Task VisionNodeManagerFactoryCreateAsyncReturnsAsyncNodeManagerAgainstRealServer()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var factory = new VisionNodeManagerFactory();

            IAsyncNodeManager manager = await factory.CreateAsync(
                fixture.Server.CurrentInstance,
                fixture.Configuration,
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(manager, Is.Not.Null);
                Assert.That(manager, Is.InstanceOf<VisionNodeManager>(),
                    "the standalone factory must build a VisionNodeManager");
            }
            finally
            {
                if (manager is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task VisionHostedNodeManagerFactoryCreateAsyncReturnsAsyncNodeManagerAgainstRealServer()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            VisionHostedNodeManagerFactory factory = serviceProvider
                .GetRequiredService<VisionHostedNodeManagerFactory>();

            IAsyncNodeManager manager = await factory.CreateAsync(
                fixture.Server.CurrentInstance,
                fixture.Configuration,
                CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(manager, Is.Not.Null);
                Assert.That(manager, Is.InstanceOf<VisionNodeManager>());
            }
            finally
            {
                if (manager is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task VisionPostSetupRunnerInvokesConfiguratorsWhoseTargetTypeMatchesTheManager()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            int invocations = 0;
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .ConfigureVision(_ => Interlocked.Increment(ref invocations));
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IVisionPostSetupRunner runner = serviceProvider
                .GetRequiredService<IVisionPostSetupRunner>();
            var options = new VisionServerOptions();

            await runner.RunAsync(
                fixture.Manager,
                fixture.Manager.Root,
                options,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(invocations, Is.EqualTo(1),
                "the runner must have invoked the matching configurator exactly once");
        }

        [Test]
        public async Task VisionPostSetupRunnerIsANoOpForNonVisionNodeManager()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            int invocations = 0;
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .ConfigureVision(_ => Interlocked.Increment(ref invocations));
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IVisionPostSetupRunner runner = serviceProvider
                .GetRequiredService<IVisionPostSetupRunner>();
            using var otherManager = new CustomNodeManager2Stub(
                fixture.Server.CurrentInstance, fixture.Configuration);

            await runner.RunAsync(
                otherManager,
                fixture.Manager.Root,
                new VisionServerOptions(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(invocations, Is.EqualTo(0),
                "a non-Vision manager must not trigger any Vision configurator");
        }

        [Test]
        public void VisionPostSetupRunnerRejectsNullManager()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IVisionPostSetupRunner runner = serviceProvider
                .GetRequiredService<IVisionPostSetupRunner>();

            Assert.That(
                async () => await runner.RunAsync(
                    null!,
                    new VisionRootState(null),
                    new VisionServerOptions(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("manager"));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by Microsoft.Extensions.DependencyInjection via AddVisionMediaProvider<StubMediaProvider>().")]
        private sealed class StubMediaProvider : IVisionMediaProvider
        {
            public ValueTask<VisionClipResult> GetClipAsync(
                VisionClipRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<VisionClipResult>(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType(),
                    default,
                    ByteString.Empty));
            }

            public ValueTask<VisionStreamLease> GetStreamAsync(
                VisionStreamRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<VisionStreamLease>(new VisionStreamLease(
                    ServiceResult.Good, new VisionStreamSessionDataType(), NodeId.Null));
            }

            public ValueTask<ServiceResult> ReleaseStreamAsync(
                ByteString sessionToken, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> ConfigureStreamAsync(
                VisionStreamConfigurationRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SelectEndpointAsync(
                NodeId streamEndpoint, NodeId clipEndpoint, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by Microsoft.Extensions.DependencyInjection via AddVisionInferenceProvider<StubInferenceProvider>().")]
        private sealed class StubInferenceProvider : IVisionInferenceProvider
        {
            public ValueTask<VisionInferenceRunResult> RunInferenceAsync(
                VisionInferenceRunRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<VisionInferenceRunResult>(
                    new VisionInferenceRunResult(ServiceResult.Good, string.Empty));
            }

            public ValueTask<ServiceResult> StartContinuousAsync(
                NodeId pipeline, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> StopAsync(
                NodeId pipeline, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by Microsoft.Extensions.DependencyInjection via AddVisionFeedbackSink<StubFeedbackSink>().")]
        private sealed class StubFeedbackSink : IVisionFeedbackSink
        {
            public ValueTask<ServiceResult> SubmitDetectionsAsync(
                VisionSubmitDetectionsRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitInspectionResultAsync(
                VisionSubmitInspectionResultRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitCorrectionAsync(
                VisionSubmitCorrectionRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitImageReferenceAsync(
                VisionSubmitImageReferenceRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private sealed class CustomNodeManager2Stub : AsyncCustomNodeManager
        {
            public CustomNodeManager2Stub(
                IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, server.Telemetry.CreateLogger<CustomNodeManager2Stub>(),
                    s_stringValues1)
            {
            }

            private static readonly string[] s_stringValues1 = ["urn:test:stub"];
        }
    }
}
