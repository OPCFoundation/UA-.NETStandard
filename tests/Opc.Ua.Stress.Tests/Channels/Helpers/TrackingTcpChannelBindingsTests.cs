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

using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Verifies that transport lifetime observation does not hide repeated disposal.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Parallelizable]
    public sealed class TrackingTcpChannelBindingsTests
    {
        [Test]
        public void CountsEveryDisposeCallIncludingDuplicates()
        {
            var bindings = new TrackingTcpChannelBindings();
            ITransportChannel channel = bindings.Create(Utils.UriSchemeOpcTcp, NUnitTelemetryContext.Create())
                ?? throw new AssertionException("The TCP binding must create a transport.");
            try
            {
                Assert.That(channel, Is.InstanceOf<TcpTransportChannel>());
                channel.Dispose();
            }
            finally
            {
                channel.Dispose();
            }

            Assert.That(bindings.Channels.Count, Is.EqualTo(1));
            TrackingTcpChannelBindings.TrackingTcpTransportChannel tracked = bindings.Channels[0];
            Assert.Multiple(() =>
            {
                Assert.That(tracked.OpenCount, Is.Zero);
                Assert.That(tracked.CloseCount, Is.Zero);
                Assert.That(tracked.DisposeCount, Is.EqualTo(2));
                Assert.That(tracked.DisposeCompletedCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void UnsupportedSchemeDoesNotCreateTransport()
        {
            var bindings = new TrackingTcpChannelBindings();
            ITransportChannel? channel = bindings.Create("unsupported", NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(channel, Is.Null);
                Assert.That(bindings.Channels.Count, Is.Zero);
            });
        }
    }
}
