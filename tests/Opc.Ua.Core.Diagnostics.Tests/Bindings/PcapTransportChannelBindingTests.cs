// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class PcapTransportChannelBindingTests
    {
        [Test]
        public void CtorThrowsOnNullRegistry()
        {
            Assert.That(
                () => new PcapTransportChannelBinding(registry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("registry"));
        }

        [Test]
        public void UriSchemeIsOpcTcp()
        {
            var registry = new ChannelCaptureRegistry();
            var binding = new PcapTransportChannelBinding(registry);

            Assert.That(binding.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public void CreateRejectsNullTelemetry()
        {
            var registry = new ChannelCaptureRegistry();
            var binding = new PcapTransportChannelBinding(registry);

            Assert.That(
                () => binding.Create(telemetry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void CreateReturnsTransportChannelWiredToRegistry()
        {
            var registry = new ChannelCaptureRegistry();
            var binding = new PcapTransportChannelBinding(registry);

            using ITransportChannel channel = binding.Create(TestTelemetryContext.Instance);

            Assert.That(channel, Is.Not.Null);
            Assert.That(channel, Is.AssignableTo<ITransportChannel>());
        }

        [Test]
        public void CtorAcceptsNullLoggerFactory()
        {
            var registry = new ChannelCaptureRegistry();

            // Optional logger factory parameter defaults to null; ctor
            // must accept this without throwing.
            Assert.DoesNotThrow(() =>
            {
                var binding = new PcapTransportChannelBinding(registry, loggerFactory: null);
                Assert.That(binding.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
            });
        }
    }
}
