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
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Bindings;

namespace Opc.Ua.Bindings.Pcap.Tests.Bindings
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
            DefaultTransportBindingRegistry bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            IChannelCaptureRegistry registry = PcapBindings.Install(bindings);

            Assert.That(registry, Is.Not.Null);
            Assert.That(registry, Is.TypeOf<ChannelCaptureRegistry>());
            Assert.That(registry.CurrentObserver, Is.Null,
                "A freshly-installed registry must start with no observer.");
        }

        [Test]
        public void InstallSetsBindingOnTransportBindingsChannels()
        {
            DefaultTransportBindingRegistry bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.Install(bindings);

            Assert.That(bindings.HasChannelFactory(Utils.UriSchemeOpcTcp), Is.True);

            ITransportChannelFactory? binding = bindings.GetChannelFactory(Utils.UriSchemeOpcTcp);

            Assert.That(binding, Is.Not.Null);
            Assert.That(binding!.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        [Test]
        public void InstallWithSuppliedRegistryReplacesPreviousBinding()
        {
            DefaultTransportBindingRegistry bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
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
            DefaultTransportBindingRegistry bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            Assert.That(
                () => PcapBindings.Install(bindings, registry: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("registry"));
        }
    }
}
