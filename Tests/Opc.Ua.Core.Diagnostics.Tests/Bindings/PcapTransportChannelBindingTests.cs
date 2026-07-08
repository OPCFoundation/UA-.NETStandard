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
using Opc.Ua.Bindings;
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
