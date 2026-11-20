/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
