// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using FluentAssertions;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using Xunit;

    public class ChannelDiagnosticTests
    {
        [Fact]
        public void ChannelDiagnosticShouldInitializePropertiesCorrectly()
        {
            // Arrange
            var timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new Opc.Ua.EndpointDescription("opc.tcp://localhost:4840");
            var remoteIpAddress = IPAddress.Parse("192.168.1.1");
            var localIpAddress = IPAddress.Parse("192.168.1.2");
            const uint channelId = 123u;
            const uint tokenId = 456u;
            var createdAt = DateTime.UtcNow;
            var lifetime = TimeSpan.FromMinutes(30);
            var clientKey = new ChannelKey([1, 2, 3], [4, 5, 6], 32);
            var serverKey = new ChannelKey([], [], 0)
            {
                Iv = [7, 8, 9],
                Key = [10, 11, 12],
                SigLen = 16
            };

            // Act
            var diagnostic = new ChannelDiagnostic
            {
                TimeStamp = timeStamp,
                Endpoint = endpoint,
                RemoteIpAddress = remoteIpAddress,
                RemotePort = 4840,
                LocalIpAddress = localIpAddress,
                LocalPort = 4841,
                ChannelId = channelId,
                TokenId = tokenId,
                CreatedAt = createdAt,
                Lifetime = lifetime,
                Client = clientKey,
                Server = serverKey
            };

            // Assert
            diagnostic.ToString().Should().NotBeNull();
            diagnostic.GetHashCode().Should().NotBe(0);
            diagnostic.TimeStamp.Should().Be(timeStamp);
            diagnostic.Endpoint.Should().Be(endpoint);
            diagnostic.RemoteIpAddress.Should().Be(remoteIpAddress);
            diagnostic.RemotePort.Should().Be(4840);
            diagnostic.LocalIpAddress.Should().Be(localIpAddress);
            diagnostic.LocalPort.Should().Be(4841);
            diagnostic.ChannelId.Should().Be(channelId);
            diagnostic.TokenId.Should().Be(tokenId);
            diagnostic.CreatedAt.Should().Be(createdAt);
            diagnostic.Lifetime.Should().Be(lifetime);
            diagnostic.Client.Should().Be(clientKey);
            diagnostic.Server.Should().Be(serverKey);
            serverKey.ToString().Should().NotBeNull();
            clientKey.GetHashCode().Should().NotBe(serverKey.GetHashCode());
            clientKey.Should().BeEquivalentTo(clientKey with { });
            clientKey.Should().BeEquivalentTo(clientKey with { });
            diagnostic.Should().BeEquivalentTo(diagnostic with { });
        }

        [Fact]
        public void WriteToWiresharkKeySetFileShouldWriteCorrectly()
        {
            // Arrange
            var timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");
            const uint channelId = 123u;
            const uint tokenId = 456u;
            var clientKey = new ChannelKey([1, 2, 3], [4, 5, 6], 32);
            var serverKey = new ChannelKey([7, 8, 9], [10, 11, 12], 16);

            var diagnostic = new ChannelDiagnostic
            {
                TimeStamp = timeStamp,
                Endpoint = endpoint,
                ChannelId = channelId,
                TokenId = tokenId,
                Client = clientKey,
                Server = serverKey
            };

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream);

            // Act
            diagnostic.WriteToWiresharkKeySetFile(writer);
            writer.Flush();
            memoryStream.Position = 0;

            using var reader = new StreamReader(memoryStream);
            var result = reader.ReadToEnd();

            // Assert
            var expected = $"""
            client_iv_{channelId}_{tokenId}: {Convert.ToHexString([.. clientKey.Iv])}
            client_key_{channelId}_{tokenId}: {Convert.ToHexString([.. clientKey.Key])}
            client_siglen_{channelId}_{tokenId}: {clientKey.SigLen.ToString(CultureInfo.InvariantCulture)}
            server_iv_{channelId}_{tokenId}: {Convert.ToHexString([.. serverKey.Iv])}
            server_key_{channelId}_{tokenId}: {Convert.ToHexString([.. serverKey.Key])}
            server_siglen_{channelId}_{tokenId}: {serverKey.SigLen.ToString(CultureInfo.InvariantCulture)}

            """.ReplaceLineEndings();

            result.Should().Be(expected);
        }

        [Fact]
        public void WriteToWiresharkKeySetFileShouldNotWriteWhenClientOrServerIsNull()
        {
            // Arrange
            var timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");
            const uint channelId = 123u;
            const uint tokenId = 456u;

            var diagnostic = new ChannelDiagnostic
            {
                TimeStamp = timeStamp,
                Endpoint = endpoint,
                ChannelId = channelId,
                TokenId = tokenId,
                Client = null,
                Server = null
            };

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream);

            // Act
            diagnostic.WriteToWiresharkKeySetFile(writer);
            writer.Flush();
            memoryStream.Position = 0;

            using var reader = new StreamReader(memoryStream);
            var result = reader.ReadToEnd();

            // Assert
            result.Should().BeEmpty();
        }
    }
}
