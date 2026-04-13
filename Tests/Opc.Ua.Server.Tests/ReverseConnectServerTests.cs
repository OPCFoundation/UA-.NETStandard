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
using System.Collections.ObjectModel;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ReverseConnectServer")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReverseConnectServerTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void GetReverseConnections_EmptyByDefault()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            ReadOnlyDictionary<Uri, ReverseConnectProperty> connections = server.GetReverseConnections();

            Assert.That(connections, Is.Not.Null);
            Assert.That(connections, Is.Empty);
        }

        [Test]
        public void AddReverseConnection_AddsConnection()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url = new Uri("opc.tcp://localhost:4840");

            server.AddReverseConnection(url, timeout: 5000);

            ReadOnlyDictionary<Uri, ReverseConnectProperty> connections = server.GetReverseConnections();
            Assert.That(connections, Has.Count.EqualTo(1));
            Assert.That(connections.ContainsKey(url), Is.True);
        }

        [Test]
        public void AddReverseConnection_DuplicateUrl_ThrowsArgumentException()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url = new Uri("opc.tcp://localhost:4840");

            server.AddReverseConnection(url);

            Assert.That(
                () => server.AddReverseConnection(url),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddReverseConnection_MultipleUrls_AllAdded()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url1 = new Uri("opc.tcp://host1:4840");
            var url2 = new Uri("opc.tcp://host2:4840");
            var url3 = new Uri("opc.tcp://host3:4840");

            server.AddReverseConnection(url1);
            server.AddReverseConnection(url2);
            server.AddReverseConnection(url3);

            ReadOnlyDictionary<Uri, ReverseConnectProperty> connections = server.GetReverseConnections();
            Assert.That(connections, Has.Count.EqualTo(3));
        }

        [Test]
        public void RemoveReverseConnection_ExistingUrl_ReturnsTrue()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url = new Uri("opc.tcp://localhost:4840");
            server.AddReverseConnection(url);

            bool removed = server.RemoveReverseConnection(url);

            Assert.That(removed, Is.True);
            Assert.That(server.GetReverseConnections(), Is.Empty);
        }

        [Test]
        public void RemoveReverseConnection_NonExistingUrl_ReturnsFalse()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url = new Uri("opc.tcp://localhost:4840");

            bool removed = server.RemoveReverseConnection(url);

            Assert.That(removed, Is.False);
        }

        [Test]
        public void RemoveReverseConnection_NullUrl_ThrowsArgumentNullException()
        {
            using var server = new ReverseConnectServer(m_telemetry);

            Assert.That(
                () => server.RemoveReverseConnection(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddAndRemove_Lifecycle()
        {
            using var server = new ReverseConnectServer(m_telemetry);
            var url1 = new Uri("opc.tcp://host1:4840");
            var url2 = new Uri("opc.tcp://host2:4840");

            server.AddReverseConnection(url1);
            server.AddReverseConnection(url2);
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(2));

            server.RemoveReverseConnection(url1);
            ReadOnlyDictionary<Uri, ReverseConnectProperty> remaining = server.GetReverseConnections();
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining.ContainsKey(url2), Is.True);

            server.RemoveReverseConnection(url2);
            Assert.That(server.GetReverseConnections(), Is.Empty);
        }

        [Test]
        public void DefaultConstants_HaveExpectedValues()
        {
            Assert.That(ReverseConnectServer.DefaultReverseConnectInterval, Is.EqualTo(15000));
            Assert.That(ReverseConnectServer.DefaultReverseConnectTimeout, Is.EqualTo(30000));
            Assert.That(ReverseConnectServer.DefaultReverseConnectRejectTimeout, Is.EqualTo(60000));
        }
    }
}
