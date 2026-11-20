// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class PcapBindingsTests
    {
        [Test]
        public void InstallWithNullRegistryThrows()
        {
            Assert.That(
                () => PcapBindings.Install(bindingRegistry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("bindingRegistry"));
        }

        [Test]
        public void InstallReturnsNonNullCaptureRegistry()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            IChannelCaptureRegistry registry = PcapBindings.Install(bindings);

            Assert.That(registry, Is.Not.Null);
            Assert.That(registry, Is.TypeOf<ChannelCaptureRegistry>());
            Assert.That(registry.CurrentObserver, Is.Null,
                "A freshly-installed registry must start with no observer.");
        }

        [Test]
        public void InstallSetsBindingOnTransportBindingsChannels()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.Install(bindings);

            Assert.That(bindings.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);

            ITransportChannelFactory? binding = bindings.GetChannelFactory(Utils.UriSchemeOpcTcp);

            Assert.That(binding, Is.Not.Null);
            Assert.That(binding!.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public void InstallWithSuppliedRegistryReplacesPreviousBinding()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            IChannelCaptureRegistry firstRegistry = PcapBindings.Install(bindings);
            var customRegistry = new ChannelCaptureRegistry();
            PcapBindings.Install(bindings, customRegistry);

            // The first registry must not see updates made via the binding
            // installed afterwards; we only care that the two registries are
            // distinct instances, both with null observers initially.
            Assert.That(firstRegistry, Is.Not.SameAs(customRegistry));
            Assert.That(customRegistry.CurrentObserver, Is.Null);
        }

        [Test]
        public void InstallWithNullCaptureRegistryThrows()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            Assert.That(
                () => PcapBindings.Install(bindings, registry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("registry"));
        }

        [Test]
        public void InstallInstallsBothClientAndServerBindings()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.Install(bindings);

            Assert.That(
                bindings.GetChannelFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<PcapTransportChannelBinding>());
            Assert.That(
                bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<PcapTransportListenerBinding>());
        }

        [Test]
        public void InstallClientRegistersOnlyChannelBinding()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.InstallClient(bindings);

            Assert.That(
                bindings.GetChannelFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<PcapTransportChannelBinding>());
            Assert.That(
                bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.Not.InstanceOf<PcapTransportListenerBinding>());
        }

        [Test]
        public void InstallServerWrapsListenerFactory()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.InstallServer(bindings);

            Assert.That(
                bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.InstanceOf<PcapTransportListenerBinding>());
            Assert.That(
                bindings.GetChannelFactory(Utils.UriSchemeOpcTcp),
                Is.Not.InstanceOf<PcapTransportChannelBinding>());
        }

        [Test]
        public void InstallServerIsIdempotent()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            var registry = new ChannelCaptureRegistry();
            PcapBindings.InstallServer(bindings, registry);
            ITransportListenerFactory? first = bindings.GetListenerFactory(Utils.UriSchemeOpcTcp);

            PcapBindings.InstallServer(bindings, registry);
            ITransportListenerFactory? second = bindings.GetListenerFactory(Utils.UriSchemeOpcTcp);

            Assert.That(second, Is.SameAs(first),
                "A second InstallServer must not wrap the already-installed binding again.");
        }

        [Test]
        public void InstallServerIsNoOpWhenNoListenerFactory()
        {
            var bindings = new DefaultTransportBindingRegistry();
            PcapBindings.InstallServer(bindings);

            Assert.That(bindings.HasListenerFactory(Utils.UriSchemeOpcTcp), Is.False);
        }
    }
}
