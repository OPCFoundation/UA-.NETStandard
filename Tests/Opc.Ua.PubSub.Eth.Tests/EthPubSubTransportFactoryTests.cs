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
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates <see cref="EthPubSubTransportFactory"/> creation and
    /// input validation.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.3", Summary = "Ethernet transport factory")]
    public sealed class EthPubSubTransportFactoryTests
    {
        private static EthPubSubTransportFactory NewFactory()
        {
            return new EthPubSubTransportFactory(
                Options.Create(new EthTransportOptions()),
                new InMemoryEthernetFrameChannelFactory());
        }

        [Test]
        public void TransportProfileUriIsEthernetUadp()
        {
            Assert.That(NewFactory().TransportProfileUri, Is.EqualTo(Profiles.PubSubEthUadpTransport));
        }

        [Test]
        public async Task CreateReturnsEthernetTransport()
        {
            EthPubSubTransportFactory factory = NewFactory();
            await using IPubSubTransport transport = factory.Create(
                EthTestHelpers.NewConnection("opc.eth://01-00-5E-00-00-01"),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(
                    transport.TransportProfileUri,
                    Is.EqualTo(Profiles.PubSubEthUadpTransport));
                Assert.That(transport, Is.InstanceOf<EthernetDatagramTransport>());
            });
        }

        [Test]
        public void CreateNullConnectionThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            Assert.That(
                () => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateAddressNotNetworkAddressUrlThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "Bad",
                TransportProfileUri = Profiles.PubSubEthUadpTransport,
                Address = new ExtensionObject(new NetworkAddressDataType())
            };

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void CreateEmptyUrlThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "Empty",
                TransportProfileUri = Profiles.PubSubEthUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = string.Empty })
            };

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void ConstructorNullChannelFactoryThrows()
        {
            Assert.That(
                () => new EthPubSubTransportFactory(
                    Options.Create(new EthTransportOptions()), null!),
                Throws.ArgumentNullException);
        }
    }
}
