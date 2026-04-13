/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests for the <see cref="HttpsTransportListener"/> class.
    /// </summary>
    [TestFixture]
    [Category("HttpsTransportListenerTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsTransportListenerTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected void TearDown()
        {
        }

        // Verify constructor with https scheme creates a valid instance.
        [Test]
        public void ConstructorWithHttpsSchemeCreatesInstance()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        // Verify constructor with opc.https scheme creates a valid instance.
        [Test]
        public void ConstructorWithOpcHttpsSchemeCreatesInstance()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener, Is.Not.Null);
        }

        // Verify UriScheme property returns the scheme passed to the constructor.
        [Test]
        public void UriSchemeReturnsHttpsWhenConstructedWithHttps()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        // Verify UriScheme property returns opc.https when constructed with that scheme.
        [Test]
        public void UriSchemeReturnsOpcHttpsWhenConstructedWithOpcHttps()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeOpcHttps, m_telemetry);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify ListenerId is null before Open is called.
        [Test]
        public void ListenerIdIsNullBeforeOpen()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        // Verify EndpointUrl is null before Open is called.
        [Test]
        public void EndpointUrlIsNullBeforeOpen()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener.EndpointUrl, Is.Null);
        }

        // Verify Close on an unopened listener does not throw.
        [Test]
        public void CloseOnUnopenedListenerDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.Close());
        }

        // Verify Dispose on an unopened listener does not throw.
        [Test]
        public void DisposeOnUnopenedListenerDoesNotThrow()
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify calling Dispose twice does not throw.
        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Dispose();
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify Close followed by Dispose does not throw.
        [Test]
        public void CloseFollowedByDisposeDoesNotThrow()
        {
            var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Close();
            Assert.DoesNotThrow(() => listener.Dispose());
        }

        // Verify CreateReverseConnection throws NotImplementedException.
        [Test]
        public void CreateReverseConnectionThrowsNotImplementedException()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            var uri = new Uri("https://localhost:4840");
            Assert.Throws<NotImplementedException>(() => listener.CreateReverseConnection(uri, 30000));
        }

        // Verify UpdateChannelLastActiveTime does not throw on unopened listener.
        [Test]
        public void UpdateChannelLastActiveTimeDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime("test-channel-id"));
        }

        // Verify UpdateChannelLastActiveTime with null does not throw.
        [Test]
        public void UpdateChannelLastActiveTimeWithNullDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(null));
        }

        // Verify UpdateChannelLastActiveTime with empty string does not throw.
        [Test]
        public void UpdateChannelLastActiveTimeWithEmptyStringDoesNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.DoesNotThrow(() => listener.UpdateChannelLastActiveTime(string.Empty));
        }

        // Verify the listener implements ITransportListener.
        [Test]
        public void ListenerImplementsITransportListener()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListener>());
        }

        // Verify the listener implements IDisposable.
        [Test]
        public void ListenerImplementsIDisposable()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<IDisposable>());
        }

        // Verify HttpsTransportListenerFactory creates an instance with correct scheme.
        [Test]
        public void HttpsTransportListenerFactoryCreatesListener()
        {
            var factory = new HttpsTransportListenerFactory();
            using var listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("https"));
        }

        // Verify HttpsTransportListenerFactory UriScheme property.
        [Test]
        public void HttpsTransportListenerFactoryUriSchemeIsHttps()
        {
            var factory = new HttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("https"));
        }

        // Verify OpcHttpsTransportListenerFactory creates an instance with correct scheme.
        [Test]
        public void OpcHttpsTransportListenerFactoryCreatesListener()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            using var listener = factory.Create(m_telemetry);
            Assert.That(listener, Is.Not.Null);
            Assert.That(listener.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify OpcHttpsTransportListenerFactory UriScheme property.
        [Test]
        public void OpcHttpsTransportListenerFactoryUriSchemeIsOpcHttps()
        {
            var factory = new OpcHttpsTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo("opc.https"));
        }

        // Verify factory-created listener has null ListenerId before Open.
        [Test]
        public void FactoryCreatedListenerHasNullListenerId()
        {
            var factory = new HttpsTransportListenerFactory();
            using var listener = factory.Create(m_telemetry);
            Assert.That(listener.ListenerId, Is.Null);
        }

        // Verify CreateReverseConnection with null uri throws NotImplementedException.
        [Test]
        public void CreateReverseConnectionWithNullUriThrowsNotImplementedException()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.Throws<NotImplementedException>(() => listener.CreateReverseConnection(null, 0));
        }

        // Verify multiple Close calls on an unopened listener do not throw.
        [Test]
        public void MultipleCloseCallsDoNotThrow()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            listener.Close();
            Assert.DoesNotThrow(() => listener.Close());
        }
    }
}
