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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Hosting;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for the Vision hosting extensions on
    /// <see cref="IOpcUaServerBuilder"/>. These validate DI wiring and
    /// argument guards without booting a full server.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Hosting")]
    public sealed class VisionHostingTests
    {
        [Test]
        public void AddVisionThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerVisionBuilderExtensions.AddVision(null!));
        }

        [Test]
        public void AddVisionRegistersModelProviderAndPostSetupRunner()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision();

            using ServiceProvider provider = services.BuildServiceProvider();
            var modelProviders = provider.GetServices<IVisionModelProvider>();
            IVisionPostSetupRunner runner =
                provider.GetRequiredService<IVisionPostSetupRunner>();

            Assert.That(modelProviders, Is.Not.Empty);
            Assert.That(runner, Is.Not.Null);
        }

        [Test]
        public void AddVisionAppliesConfigurationDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision(options => options.InstanceNamespaceUri = "urn:test:vision");

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionServerOptions options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<VisionServerOptions>>()
                .Value;

            Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:test:vision"));
        }

        [Test]
        public void AddVisionMediaProviderThrowsOnNullBuilder()
        {
            var provider = new Mock<IVisionMediaProvider>().Object;

            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerVisionBuilderExtensions.AddVisionMediaProvider(
                    null!, "Sensor1", provider));
        }

        [Test]
        public void AddVisionMediaProviderInstanceThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");
            var provider = new Mock<IVisionMediaProvider>().Object;

            Assert.Throws<ArgumentException>(() =>
                builder.AddVisionMediaProvider(string.Empty, provider));
        }

        [Test]
        public void AddVisionMediaProviderInstanceThrowsOnNullProvider()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<ArgumentNullException>(() =>
                builder.AddVisionMediaProvider("Sensor1", (IVisionMediaProvider)null!));
        }

        [Test]
        public void AddVisionMediaProviderInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            var mediaProvider = new Mock<IVisionMediaProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionMediaProvider("Sensor1", mediaProvider);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionMediaProviderRegistration>().FirstOrDefault();

            Assert.That(registration, Is.Not.Null);
            Assert.That(registration!.SensorBrowseName, Is.EqualTo("Sensor1"));
            Assert.That(registration.Provider, Is.SameAs(mediaProvider));
        }

        [Test]
        public void AddVisionInferenceProviderThrowsOnNullBuilder()
        {
            var provider = new Mock<IVisionInferenceProvider>().Object;

            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerVisionBuilderExtensions.AddVisionInferenceProvider(
                    null!, "Pipeline1", provider));
        }

        [Test]
        public void AddVisionInferenceProviderInstanceThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");
            var provider = new Mock<IVisionInferenceProvider>().Object;

            Assert.Throws<ArgumentException>(() =>
                builder.AddVisionInferenceProvider(string.Empty, provider));
        }

        [Test]
        public void AddVisionInferenceProviderInstanceThrowsOnNullProvider()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<ArgumentNullException>(() =>
                builder.AddVisionInferenceProvider(
                    "Pipeline1", (IVisionInferenceProvider)null!));
        }

        [Test]
        public void AddVisionInferenceProviderInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            var inferenceProvider = new Mock<IVisionInferenceProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider("Pipeline1", inferenceProvider);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionInferenceProviderRegistration>().FirstOrDefault();

            Assert.That(registration, Is.Not.Null);
            Assert.That(registration!.PipelineBrowseName, Is.EqualTo("Pipeline1"));
            Assert.That(registration.Provider, Is.SameAs(inferenceProvider));
            Assert.That(registration.OnServer, Is.True);
        }

        [Test]
        public void AddVisionInferenceProviderRegistersOffServerWhenFlagFalse()
        {
            IServiceCollection services = new ServiceCollection();
            var inferenceProvider = new Mock<IVisionInferenceProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider("Pipeline1", inferenceProvider,
                    onServer: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionInferenceProviderRegistration>().First();

            Assert.That(registration.OnServer, Is.False);
        }

        [Test]
        public void AddVisionFeedbackSinkThrowsOnNullBuilder()
        {
            var sink = new Mock<IVisionFeedbackSink>().Object;

            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerVisionBuilderExtensions.AddVisionFeedbackSink(
                    null!, "Pipeline1", sink));
        }

        [Test]
        public void AddVisionFeedbackSinkInstanceThrowsOnEmptyBrowseName()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");
            var sink = new Mock<IVisionFeedbackSink>().Object;

            Assert.Throws<ArgumentException>(() =>
                builder.AddVisionFeedbackSink(string.Empty, sink));
        }

        [Test]
        public void AddVisionFeedbackSinkInstanceThrowsOnNullSink()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<ArgumentNullException>(() =>
                builder.AddVisionFeedbackSink("Pipeline1", (IVisionFeedbackSink)null!));
        }

        [Test]
        public void AddVisionFeedbackSinkInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            var feedbackSink = new Mock<IVisionFeedbackSink>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionFeedbackSink("Pipeline1", feedbackSink);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registration = provider
                .GetServices<VisionFeedbackSinkRegistration>().FirstOrDefault();

            Assert.That(registration, Is.Not.Null);
            Assert.That(registration!.PipelineBrowseName, Is.EqualTo("Pipeline1"));
            Assert.That(registration.Sink, Is.SameAs(feedbackSink));
        }

        [Test]
        public void ConfigureVisionThrowsOnNullDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<ArgumentNullException>(() =>
                builder.ConfigureVision((Action<IVisionBuildContext>)null!));
        }

        [Test]
        public void ConfigureVisionAcceptsSyncDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .ConfigureVision(_ => { });

            using ServiceProvider provider = services.BuildServiceProvider();
            var configurators = provider.GetServices<IVisionPostSetupConfigurator>();

            Assert.That(configurators, Is.Not.Empty);
        }

        [Test]
        public void ConfigureVisionAcceptsAsyncDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .ConfigureVision((_, _) => default);

            using ServiceProvider provider = services.BuildServiceProvider();
            var configurators = provider.GetServices<IVisionPostSetupConfigurator>();

            Assert.That(configurators, Is.Not.Empty);
        }

        [Test]
        public void ConfigureVisionForThrowsOnUnsupportedNodeManagerType()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<NotSupportedException>(() =>
                builder.ConfigureVisionFor<UnsupportedNodeManager>((_, _) => default));
        }

        [Test]
        public void ConfigureVisionForThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaServerVisionBuilderExtensions.ConfigureVisionFor<VisionNodeManager>(
                    null!, (_, _) => default));
        }

        [Test]
        public void ConfigureVisionForThrowsOnNullDelegate()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test");

            Assert.Throws<ArgumentNullException>(() =>
                builder.ConfigureVisionFor<VisionNodeManager>(null!));
        }

        [Test]
        public void VisionNodeManagerFactoryDefaultCtorReportsVisionNamespace()
        {
            var factory = new VisionNodeManagerFactory();

            ArrayOf<string> namespaces = factory.NamespacesUris;

            Assert.That(namespaces.Count, Is.GreaterThanOrEqualTo(1));
            bool containsVision = false;
            for (int ii = 0; ii < namespaces.Count; ii++)
            {
                if (namespaces[ii] == global::Opc.Ua.Vision.Namespaces.Vision)
                {
                    containsVision = true;
                    break;
                }
            }
            Assert.That(containsVision, Is.True);
        }

        [Test]
        public void VisionNodeManagerFactoryReportsInstanceNamespaceInAdditionToVision()
        {
            var options = new VisionServerOptions
            {
                InstanceNamespaceUri = "urn:test:vision:instance"
            };
            var providers = new IVisionModelProvider[] { new VisionModelProvider() }
                .ToArrayOf();
            var factory = new VisionNodeManagerFactory(providers, options);

            ArrayOf<string> namespaces = factory.NamespacesUris;

            bool hasInstance = false;
            for (int ii = 0; ii < namespaces.Count; ii++)
            {
                if (namespaces[ii] == "urn:test:vision:instance")
                {
                    hasInstance = true;
                    break;
                }
            }
            Assert.That(hasInstance, Is.True);
        }

        private abstract class UnsupportedNodeManager : AsyncCustomNodeManager
        {
            protected UnsupportedNodeManager(IServerInternal server)
                : base(server, "urn:test:unsupported")
            {
            }
        }
    }
}
