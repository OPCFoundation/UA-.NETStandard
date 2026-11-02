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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
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
        [TestCase(MachineVisionParts.None)]
        [TestCase(MachineVisionParts.Control)]
        public void VisionWithoutAssetsDoesNotClaimDi(MachineVisionParts parts)
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .AddVision(options => options.MachineVisionParts = parts)
                .AddOpcUaDi();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                Is.EqualTo("AddOpcUaDi"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void VisionAssetsRejectConflictingDiOwnerInEitherOrder(bool visionFirst)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            if (visionFirst)
            {
                builder.AddVision(options => options.MachineVisionParts = MachineVisionParts.Assets);
                Assert.That(() => builder.AddOpcUaDi(), Throws.InvalidOperationException);
            }
            else
            {
                builder.AddOpcUaDi();
                Assert.That(
                    () => builder.AddVision(options => options.MachineVisionParts = MachineVisionParts.Assets),
                    Throws.InvalidOperationException.With.Message.Contains("AddVisionToMachinery"));
            }
            Assert.That(services.Count(service => service.ServiceType == typeof(DiAddressSpaceOwnership)),
                Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AddMachineVisionPreservesSingleOwnerAndProviderOnRepeatedCalls(bool assetsInitiallyEnabled)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddVision(options => options.MachineVisionParts = assetsInitiallyEnabled
                    ? MachineVisionParts.All
                    : MachineVisionParts.Control);
            builder.AddMachineVision().AddMachineVision();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo("AddVision"));
                Assert.That(provider.GetServices<IVisionModelProvider>()
                    .OfType<VisionMachineryStandaloneModelProvider>().Count(), Is.EqualTo(1));
                Assert.That(provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<VisionServerOptions>>()
                    .Value.MachineVisionParts, Is.EqualTo(MachineVisionParts.All));
            });
        }

        [Test]
        public void AddMachineVisionChecksConflictingOwnerBeforeVisionPrerequisite()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { }).AddOpcUaDi();

            Assert.That(() => builder.AddMachineVision(),
                Throws.InvalidOperationException.With.Message.Contains("AddVisionToMachinery"));
            Assert.That(services.Count(service => service.ServiceType == typeof(DiAddressSpaceOwnership)),
                Is.EqualTo(1));
        }

        [Test]
        public void AddMachineVisionRequiresVisionBeforeClaimingDi()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            Assert.That(() => builder.AddMachineVision(),
                Throws.InvalidOperationException.With.Message.Contains("Register AddVision"));
            Assert.That(services.Any(service => service.ServiceType == typeof(DiAddressSpaceOwnership)), Is.False);
        }

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
            IEnumerable<IVisionModelProvider> modelProviders = provider.GetServices<IVisionModelProvider>();
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
            IVisionMediaProvider provider = new Mock<IVisionMediaProvider>().Object;

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
            IVisionMediaProvider provider = new Mock<IVisionMediaProvider>().Object;

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
                builder.AddVisionMediaProvider("Sensor1", null!));
        }

        [Test]
        public void AddVisionMediaProviderInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            IVisionMediaProvider mediaProvider = new Mock<IVisionMediaProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionMediaProvider("Sensor1", mediaProvider);

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionMediaProviderRegistration? registration = provider
                .GetServices<VisionMediaProviderRegistration>().FirstOrDefault();

            Assert.That(registration, Is.Not.Null);
            Assert.That(registration!.SensorBrowseName, Is.EqualTo("Sensor1"));
            Assert.That(registration.Provider, Is.SameAs(mediaProvider));
        }

        [Test]
        public void AddVisionInferenceProviderThrowsOnNullBuilder()
        {
            IVisionInferenceProvider provider = new Mock<IVisionInferenceProvider>().Object;

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
            IVisionInferenceProvider provider = new Mock<IVisionInferenceProvider>().Object;

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
                    "Pipeline1", null!));
        }

        [Test]
        public void AddVisionInferenceProviderInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            IVisionInferenceProvider inferenceProvider = new Mock<IVisionInferenceProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider("Pipeline1", inferenceProvider);

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionInferenceProviderRegistration? registration = provider
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
            IVisionInferenceProvider inferenceProvider = new Mock<IVisionInferenceProvider>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionInferenceProvider("Pipeline1", inferenceProvider,
                    onServer: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionInferenceProviderRegistration registration = provider
                .GetServices<VisionInferenceProviderRegistration>().First();

            Assert.That(registration.OnServer, Is.False);
        }

        [Test]
        public void AddVisionFeedbackSinkThrowsOnNullBuilder()
        {
            IVisionFeedbackSink sink = new Mock<IVisionFeedbackSink>().Object;

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
            IVisionFeedbackSink sink = new Mock<IVisionFeedbackSink>().Object;

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
                builder.AddVisionFeedbackSink("Pipeline1", null!));
        }

        [Test]
        public void AddVisionFeedbackSinkInstanceRegistersRegistration()
        {
            IServiceCollection services = new ServiceCollection();
            IVisionFeedbackSink feedbackSink = new Mock<IVisionFeedbackSink>().Object;
            services.AddOpcUa()
                .AddServer(o => o.ApplicationName = "test")
                .AddVision()
                .AddVisionFeedbackSink("Pipeline1", feedbackSink);

            using ServiceProvider provider = services.BuildServiceProvider();
            VisionFeedbackSinkRegistration? registration = provider
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
            IEnumerable<IVisionPostSetupConfigurator> configurators =
                provider.GetServices<IVisionPostSetupConfigurator>();

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
            IEnumerable<IVisionPostSetupConfigurator> configurators =
                provider.GetServices<IVisionPostSetupConfigurator>();

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
                if (namespaces[ii] == Perception.Namespaces.Vision)
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
            ArrayOf<IVisionModelProvider> providers = new IVisionModelProvider[] { new VisionModelProvider() }
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
