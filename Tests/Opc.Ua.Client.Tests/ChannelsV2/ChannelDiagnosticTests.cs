#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Net;
using NUnit.Framework;

namespace Opc.Ua.Client
{
    [TestFixture]
    public class ChannelDiagnosticTests
    {
        [Test]
        public void ChannelDiagnosticShouldInitializePropertiesCorrectly()
        {
            // Arrange
            DateTimeOffset timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");
            var remoteIpAddress = IPAddress.Parse("192.168.1.1");
            var localIpAddress = IPAddress.Parse("192.168.1.2");
            const uint channelId = 123u;
            const uint tokenId = 456u;
            DateTime createdAt = DateTime.UtcNow;
            var lifetime = TimeSpan.FromMinutes(30);
            var clientKey = new ChannelKey([1, 2, 3], [4, 5, 6], 32);
            var serverKey = new ChannelKey([], [], 0)
            {
                Iv = [7, 8, 9],
                Key = [10, 11, 12],
                SigLen = 16
            };

            // Act
            var diagnostic = new TransportChannelDiagnostic
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
            Assert.That(diagnostic.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.GetHashCode(), Is.Not.Zero);
            Assert.That(diagnostic.TimeStamp, Is.EqualTo(timeStamp));
            Assert.That(diagnostic.Endpoint, Is.EqualTo(endpoint));
            Assert.That(diagnostic.RemoteIpAddress, Is.EqualTo(remoteIpAddress));
            Assert.That(diagnostic.RemotePort, Is.EqualTo(4840));
            Assert.That(diagnostic.LocalIpAddress, Is.EqualTo(localIpAddress));
            Assert.That(diagnostic.LocalPort, Is.EqualTo(4841));
            Assert.That(diagnostic.ChannelId, Is.EqualTo(channelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(tokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(createdAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(lifetime));
            Assert.That(diagnostic.Client, Is.EqualTo(clientKey));
            Assert.That(diagnostic.Server, Is.EqualTo(serverKey));
            Assert.That(serverKey.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(clientKey.GetHashCode(), Is.Not.EqualTo(serverKey.GetHashCode()));
            ChannelKey clientClone = clientKey with { };
            Assert.That(clientClone, Is.EqualTo(clientKey));
            TransportChannelDiagnostic diagnosticClone = diagnostic with { };
            Assert.That(diagnosticClone, Is.EqualTo(diagnostic));
        }

        [Test]
        public void WriteToWiresharkKeySetFileShouldWriteCorrectly()
        {
            // Arrange
            DateTimeOffset timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");
            const uint channelId = 123u;
            const uint tokenId = 456u;
            var clientKey = new ChannelKey([1, 2, 3], [4, 5, 6], 32);
            var serverKey = new ChannelKey([7, 8, 9], [10, 11, 12], 16);

            var diagnostic = new TransportChannelDiagnostic
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
            string result = reader.ReadToEnd();

            // Assert
            string expected = $"""
            client_iv_{channelId}_{tokenId}: {Utils.ToHexString([.. clientKey.Iv])}
            client_key_{channelId}_{tokenId}: {Utils.ToHexString([.. clientKey.Key])}
            client_siglen_{channelId}_{tokenId}: {clientKey.SigLen.ToString(CultureInfo.InvariantCulture)}
            server_iv_{channelId}_{tokenId}: {Utils.ToHexString([.. serverKey.Iv])}
            server_key_{channelId}_{tokenId}: {Utils.ToHexString([.. serverKey.Key])}
            server_siglen_{channelId}_{tokenId}: {serverKey.SigLen.ToString(CultureInfo.InvariantCulture)}

            """.ReplaceLineEndings();

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void WriteToWiresharkKeySetFileShouldNotWriteWhenClientOrServerIsNull()
        {
            // Arrange
            DateTimeOffset timeStamp = DateTimeOffset.UtcNow;
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");
            const uint channelId = 123u;
            const uint tokenId = 456u;

            var diagnostic = new TransportChannelDiagnostic
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
            string result = reader.ReadToEnd();

            // Assert
            Assert.That(result, Is.Empty);
        }
    }
}
#endif
