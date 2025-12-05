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
 * copies of the Software, and to permit persons to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
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
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class NullChannelTests
    {
        [Test]
        public void SetOperationTimeoutDoesNotThrow()
        {
            using var sut = new NullChannel();
            Assert.That(sut.OperationTimeout, Is.EqualTo(0));
            sut.OperationTimeout = 1;
            Assert.That(sut.OperationTimeout, Is.EqualTo(1));
        }

        [Test]
        public void SupportedFeaturesShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => _ = sut.SupportedFeatures,
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("SupportedFeatures called in unexpected state"));
        }

        [Test]
        public void EndpointDescriptionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => _ = sut.EndpointDescription,
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("EndpointDescription called in unexpected state"));
        }

        [Test]
        public void EndpointConfigurationShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => _ = sut.EndpointConfiguration,
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("EndpointConfiguration called in unexpected state"));
        }

        [Test]
        public void MessageContextShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => _ = sut.MessageContext,
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("MessageContext called in unexpected state"));
        }

        [Test]
        public void CurrentTokenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => { ChannelToken _ = sut.CurrentToken; },
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("CurrentToken called in unexpected state"));
        }

        [Test]
        public void OnTokenActivatedAddShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => sut.OnTokenActivated += (a, b, e) => { },
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("OnTokenActivated called in unexpected state"));
        }

        [Test]
        public void OnTokenActivatedRemoveShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => sut.OnTokenActivated -= (a, b, e) => { },
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("OnTokenActivated called in unexpected state"));
        }

        [Test]
        public void CloseAsyncShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(async () => await sut.CloseAsync(CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("CloseAsync called in unexpected state"));
        }

        [Test]
        public void SendRequestAsyncShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            Assert.That(() => sut.SendRequestAsync(null!, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("SendRequestAsync called in unexpected state"));
        }

        [Test]
        public void OpenAsyncWithUrlShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            var url = new Uri("opc.tcp://localhost:4840");
            var settings = new TransportChannelSettings();
            Assert.That(async () => await sut.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("OpenAsync called in unexpected state"));
        }

        [Test]
        public void OpenAsyncWithWaitingConnectionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            ITransportWaitingConnection connection = null; // should throw before dereference
            var settings = new TransportChannelSettings();
            Assert.That(async () => await sut.OpenAsync(
                connection!,
                settings,
                CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("OpenAsync called in unexpected state"));
        }

        [Test]
        public void ReconnectAsyncShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            ITransportWaitingConnection connection = null;
            Assert.That(async () => await sut.ReconnectAsync(
                connection,
                CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Message
                    .Contains("ReconnectAsync called in unexpected state"));
        }

        [Test]
        public void DisposeShouldNotThrow()
        {
            var sut = new NullChannel();
            Assert.DoesNotThrow(sut.Dispose);
        }
    }
}
