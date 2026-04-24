// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Obsolete
{
    using FluentAssertions;
    using Opc.Ua;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public sealed class NullChannelTests
    {
        [Fact]
        public void SetOperationTimeoutDoesNotThrow()
        {
            using var sut = new NullChannel();

            // Act
            sut.OperationTimeout.Should().Be(0);
            sut.OperationTimeout = 1;

            // Assert
            sut.OperationTimeout.Should().Be(1);
        }

        [Fact]
        public void SupportedFeaturesShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.SupportedFeatures; };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*SupportedFeatures deprecated*");
        }

        [Fact]
        public void EndpointDescriptionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.EndpointDescription; };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndpointDescription deprecated*");
        }

        [Fact]
        public void EndpointConfigurationShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.EndpointConfiguration; };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndpointConfiguration deprecated*");
        }

        [Fact]
        public void MessageContextShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.MessageContext; };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*MessageContext deprecated*");
        }

        [Fact]
        public void CurrentTokenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => { var _ = sut.CurrentToken; };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*CurrentToken deprecated*");
        }

        [Fact]
        public void OnTokenActivatedAddShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.OnTokenActivated += (a, b, e) => { };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*OnTokenActivated deprecated*");
        }

        [Fact]
        public void OnTokenActivatedRemoveShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.OnTokenActivated -= (a, b, e) => { };

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*OnTokenActivated deprecated*");
        }

        [Fact]
        public void BeginCloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginClose(null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*BeginClose deprecated*");
        }

        [Fact]
        public void BeginOpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginOpen(null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*BeginOpen deprecated*");
        }

        [Fact]
        public void BeginReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginReconnect(null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*BeginReconnect deprecated*");
        }

        [Fact]
        public void BeginSendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.BeginSendRequest(null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*BeginSendRequest deprecated*");
        }

        [Fact]
        public void CloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Close;

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Close deprecated*");
        }

        [Fact]
        public async Task CloseAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.CloseAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*CloseAsync deprecated*");
        }

        [Fact]
        public void EndCloseShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndClose(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndClose deprecated*");
        }

        [Fact]
        public void EndOpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndOpen(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndOpen deprecated*");
        }

        [Fact]
        public void EndReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndReconnect(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndReconnect deprecated*");
        }

        [Fact]
        public void EndSendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.EndSendRequest(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*EndSendRequest deprecated*");
        }

        [Fact]
        public async Task EndSendRequestAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.EndSendRequestAsync(null!, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*EndSendRequestAsync deprecated*");
        }

        [Fact]
        public void InitializeWithUriShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.Initialize(new Uri("http://localhost"), null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Initialize deprecated*");
        }

        [Fact]
        public void InitializeWithConnectionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();
            // Act
            Action act = () => sut.Initialize((ITransportWaitingConnection?)null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Initialize deprecated*");
        }

        [Fact]
        public void OpenShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Open;

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Open deprecated*");
        }

        [Fact]
        public void ReconnectShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = sut.Reconnect;

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Reconnect deprecated*");
        }

        [Fact]
        public void ReconnectWithConnectionShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.Reconnect(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Reconnect deprecated*");
        }

        [Fact]
        public void SendRequestShouldThrowNotSupported()
        {
            using var sut = new NullChannel();

            // Act
            Action act = () => sut.SendRequest(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*SendRequest deprecated*");
        }

        [Fact]
        public async Task SendRequestAsyncShouldThrowNotSupportedAsync()
        {
            using var sut = new NullChannel();

            // Act
            Func<Task> act = async () => await sut.SendRequestAsync(null!, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*SendRequestAsync deprecated*");
        }
    }
}
