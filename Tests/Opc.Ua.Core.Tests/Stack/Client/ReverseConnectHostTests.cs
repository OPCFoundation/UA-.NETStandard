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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for <see cref="ReverseConnectHost"/> lifecycle and
    /// null-guard contracts.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnectHost")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReverseConnectHostTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorWithTelemetryOnlySucceeds()
        {
            Assert.That(() => new ReverseConnectHost(m_telemetry), Throws.Nothing);
        }

        [Test]
        public void ConstructorWithRegistrySucceeds()
        {
            ITransportBindingRegistry registry =
                DefaultTransportBindingRegistry.WithDefaultTcp();

            Assert.That(
                () => new ReverseConnectHost(m_telemetry, registry),
                Throws.Nothing);
        }

        [Test]
        public void ConstructorWithNullRegistryFallsBackToDefaultTcpOnCreateListener()
        {
            // Passing null registry is allowed; the host creates one lazily.
            var host = new ReverseConnectHost(m_telemetry, transportBindings: null);

            // Before CreateListener is called the Url is unset.
            Assert.That(host.Url, Is.Null);
        }

        [Test]
        public void UrlIsNullBeforeCreateListener()
        {
            var host = new ReverseConnectHost(m_telemetry);
            Assert.That(host.Url, Is.Null);
        }

        [Test]
        public void CreateListenerThrowsOnNullUrl()
        {
            var host = new ReverseConnectHost(m_telemetry);

            Assert.That(
                () => host.CreateListener(null!, null!, null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("url"));
        }

        [Test]
        public void CreateListenerWithUnsupportedSchemeThrows()
        {
            // Registry that has no factories registered → scheme lookup returns null.
            var emptyRegistry = new DefaultTransportBindingRegistry();
            var host = new ReverseConnectHost(m_telemetry, emptyRegistry);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => host.CreateListener(
                    new Uri("opc.nonexistent://localhost:4840"),
                    null!,
                    null!))!;

            Assert.That(ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadProtocolVersionUnsupported));
        }

        [Test]
        public void CreateListenerSetsUrlProperty()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);
            var url = new Uri("opc.tcp://localhost:4840");

            host.CreateListener(url, null!, null!);

            Assert.That(host.Url, Is.EqualTo(url));

            // Clean up the listener created internally.
            host.Close();
        }

        [Test]
        public void OpenBeforeCreateListenerThrowsBadInvalidState()
        {
            var host = new ReverseConnectHost(m_telemetry);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => host.Open())!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseWithoutCreateListenerIsNoOp()
        {
            var host = new ReverseConnectHost(m_telemetry);

            // Must not throw even though CreateListener was never called.
            Assert.That(() => host.Close(), Throws.Nothing);
        }

        [Test]
        public void CloseAfterCreateListenerIsIdempotent()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);

            host.CreateListener(new Uri("opc.tcp://localhost:4840"), null!, null!);

            // First close: wires down the listener.
            Assert.That(() => host.Close(), Throws.Nothing);

            // A second Close must not throw (m_listener is still non-null but the
            // event unsubscriptions and inner Close() are idempotent).
            Assert.That(() => host.Close(), Throws.Nothing);
        }

        [Test]
        public void CreateListenerWithTlsParametersOverloadSetsUrl()
        {
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();
            var host = new ReverseConnectHost(m_telemetry, registry);
            var url = new Uri("opc.tcp://localhost:4840");

            // 5-argument overload with null TLS params (valid for plain TCP).
            host.CreateListener(url, null!, null!, serverCertificates: null, certificateValidator: null);

            Assert.That(host.Url, Is.EqualTo(url));

            host.Close();
        }

        [Test]
        public async Task CreateListenerWithNullRegistryUsesDefaultTcpAsync()
        {
            // When transportBindings is null the host lazily creates the default
            // TCP registry; the opc.tcp scheme must therefore be resolvable.
            var host = new ReverseConnectHost(m_telemetry, transportBindings: null);

            Assert.That(
                () => host.CreateListener(
                    new Uri("opc.tcp://localhost:4840"),
                    null!,
                    null!),
                Throws.Nothing);

            host.Close();
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
