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

#nullable enable

using System;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Deterministic unit tests for the opt-in server-capture seam added to
    /// <see cref="TransportListenerSettings"/>
    /// (<see cref="TransportListenerSettings.AcceptedTransportDecorator"/> and
    /// <see cref="TransportListenerSettings.OnAcceptedChannel"/>).
    /// </summary>
    [TestFixture]
    [Category("TransportListenerSettings")]
    [Parallelizable]
    public sealed class TransportListenerSettingsCaptureSeamTests
    {
        [Test]
        public void CaptureSeamMembersDefaultToNull()
        {
            var settings = new TransportListenerSettings();

            Assert.That(settings.AcceptedTransportDecorator, Is.Null);
            Assert.That(settings.OnAcceptedChannel, Is.Null);
        }

        [Test]
        public void AcceptedTransportDecoratorRoundTrips()
        {
            var settings = new TransportListenerSettings();
            static IUaSCByteTransport decorator(IUaSCByteTransport transport) => transport;

            settings.AcceptedTransportDecorator = decorator;

            Assert.That(settings.AcceptedTransportDecorator, Is.SameAs(decorator));
        }

        [Test]
        public void OnAcceptedChannelRoundTrips()
        {
            var settings = new TransportListenerSettings();
            static void callback(TcpListenerChannel _)
            {
            }

            settings.OnAcceptedChannel = callback;

            Assert.That(settings.OnAcceptedChannel, Is.SameAs(callback));
        }
    }
}
