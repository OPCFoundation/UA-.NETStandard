// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Opc.Ua;
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Obsolete
{
    [TestFixture]
    public sealed class NullChannelTests
    {
        [Test]
        public void SetOperationTimeoutDoesNotThrow()
        {
            using var sut = new NullChannel();

            // Act
            Assert.That(sut.OperationTimeout, Is.EqualTo(0));
            sut.OperationTimeout = 1;

            // Assert
            Assert.That(sut.OperationTimeout, Is.EqualTo(1));
        }

        [Test]
        public void SupportedFeaturesShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.SupportedFeatures; };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*SupportedFeatures deprecated*"));
        }

        [Test]
        public void EndpointDescriptionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.EndpointDescription; };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndpointDescription deprecated*"));
        }

        [Test]
        public void EndpointConfigurationShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.EndpointConfiguration; };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndpointConfiguration deprecated*"));
        }

        [Test]
        public void MessageContextShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.MessageContext; };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*MessageContext deprecated*"));
        }

        [Test]
        public void CurrentTokenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.CurrentToken; };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*CurrentToken deprecated*"));
        }

        [Test]
        public void OnTokenActivatedAddShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.OnTokenActivated += (a, b, e) => { };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*OnTokenActivated deprecated*"));
        }

        [Test]
        public void OnTokenActivatedRemoveShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.OnTokenActivated -= (a, b, e) => { };

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*OnTokenActivated deprecated*"));
        }

        [Test]
        public void BeginCloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginClose(null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*BeginClose deprecated*"));
        }

        [Test]
        public void BeginOpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginOpen(null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*BeginOpen deprecated*"));
        }

        [Test]
        public void BeginReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginReconnect(null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*BeginReconnect deprecated*"));
        }

        [Test]
        public void BeginSendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginSendRequest(null!, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*BeginSendRequest deprecated*"));
        }

        [Test]
        public void CloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Close;

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Close deprecated*"));
        }

        [Test]
        public async Task CloseAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.CloseAsync(CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*CloseAsync deprecated*"));
        }

        [Test]
        public void EndCloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndClose(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndClose deprecated*"));
        }

        [Test]
        public void EndOpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndOpen(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndOpen deprecated*"));
        }

        [Test]
        public void EndReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndReconnect(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndReconnect deprecated*"));
        }

        [Test]
        public void EndSendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndSendRequest(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*EndSendRequest deprecated*"));
        }

        [Test]
        public async Task EndSendRequestAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.EndSendRequestAsync(null!, CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*EndSendRequestAsync deprecated*"));
        }

        [Test]
        public void InitializeWithUriShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.Initialize(new Uri("http://localhost"), null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Initialize deprecated*"));
        }

        [Test]
        public void InitializeWithConnectionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            // Act
            Action act = () => sut.Initialize((ITransportWaitingConnection?)null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Initialize deprecated*"));
        }

        [Test]
        public void OpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Open;

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Open deprecated*"));
        }

        [Test]
        public void ReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Reconnect;

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Reconnect deprecated*"));
        }

        [Test]
        public void ReconnectWithConnectionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.Reconnect(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Reconnect deprecated*"));
        }

        [Test]
        public void SendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.SendRequest(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*SendRequest deprecated*"));
        }

        [Test]
        public async Task SendRequestAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.SendRequestAsync(null!, CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*SendRequestAsync deprecated*"));
        }
    }
}
