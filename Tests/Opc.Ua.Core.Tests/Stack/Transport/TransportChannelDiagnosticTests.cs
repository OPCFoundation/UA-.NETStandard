/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Net;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
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

            // Assert (converted from FluentAssertions to NUnit)
            Assert.That(diagnostic.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.GetHashCode(), Is.Not.EqualTo(0));
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
