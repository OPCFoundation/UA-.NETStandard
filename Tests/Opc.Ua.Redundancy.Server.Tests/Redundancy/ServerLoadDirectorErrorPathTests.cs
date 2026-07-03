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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the <see cref="ServerLoadDirector"/> fail-safe and skip paths using mocked collaborators:
    /// a policy or directory that throws, a null target, an unchanged publish signature, and a publisher that
    /// throws — all of which must degrade to serving the local Server without surfacing an exception.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ServerLoadDirectorErrorPathTests
    {
        private const string BalancingUrl = "opc.tcp://balance:4840";
        private const string NormalUrl = "opc.tcp://a:4840";
        private const string LocalServerUri = "urn:A";
        private const string PeerServerUri = "urn:B";

        [Test]
        public void ConfigureThrowsOnEmptyLocalServerUri()
        {
            var director = new ServerLoadDirector(
                new ConstantServiceLevelProvider(255), new ConstantLoadWeightProvider(0), new LoadDirectionOptions());

            Assert.That(
                () => director.Configure(
                    Mock.Of<IServerDirectionPolicy>(),
                    Mock.Of<IPeerEndpointDirectory>(),
                    Mock.Of<IPeerEndpointPublisher>(),
                    string.Empty),
                Throws.ArgumentException);
        }

        [Test]
        public async Task PolicyFailureServesLocalAsync()
        {
            var policy = new Mock<IServerDirectionPolicy>();
            policy
                .Setup(p => p.SelectTargetServerUriAsync(
                    It.IsAny<string>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("policy boom"));

            ServerLoadDirector director = Configure(policy.Object, Mock.Of<IPeerEndpointDirectory>());

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints());

            Assert.That(redirect, Is.False, "a failing policy fails safe to the local Server");
        }

        [Test]
        public async Task NullTargetServesLocalAsync()
        {
            var policy = new Mock<IServerDirectionPolicy>();
            policy
                .Setup(p => p.SelectTargetServerUriAsync(
                    It.IsAny<string>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>((string?)null));

            ServerLoadDirector director = Configure(policy.Object, Mock.Of<IPeerEndpointDirectory>());

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints());

            Assert.That(redirect, Is.False, "a null target means the local Server serves the request");
        }

        [Test]
        public async Task DirectoryFailureServesLocalAsync()
        {
            var policy = new Mock<IServerDirectionPolicy>();
            policy
                .Setup(p => p.SelectTargetServerUriAsync(
                    It.IsAny<string>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>(PeerServerUri));
            var directory = new Mock<IPeerEndpointDirectory>();
            directory
                .Setup(d => d.GetEndpointsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("directory boom"));

            ServerLoadDirector director = Configure(policy.Object, directory.Object);

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints());

            Assert.That(redirect, Is.False, "a failing endpoint directory fails safe to the local Server");
        }

        [Test]
        public async Task NormalRequestWithoutLocalEndpointsSkipsPublishAsync()
        {
            var publisher = new Mock<IPeerEndpointPublisher>();
            ServerLoadDirector director = Configure(Mock.Of<IServerDirectionPolicy>(), publisher: publisher.Object);

            // A null endpoint URL is never the balancing URL, and no local endpoints means nothing to publish.
            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(null, default);

            Assert.That(redirect, Is.False);
            publisher.Verify(
                p => p.PublishAsync(It.IsAny<ArrayOf<EndpointDescription>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task RepeatedNormalRequestPublishesLocalEndpointsOnceAsync()
        {
            var publisher = new Mock<IPeerEndpointPublisher>();
            ServerLoadDirector director = Configure(Mock.Of<IServerDirectionPolicy>(), publisher: publisher.Object);

            await director.TryGetDirectedEndpointsAsync(NormalUrl, LocalEndpoints());
            await director.TryGetDirectedEndpointsAsync(NormalUrl, LocalEndpoints());

            publisher.Verify(
                p => p.PublishAsync(It.IsAny<ArrayOf<EndpointDescription>>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "an unchanged endpoint signature must not be republished");
        }

        [Test]
        public async Task PublishFailureIsSwallowedAsync()
        {
            var publisher = new Mock<IPeerEndpointPublisher>();
            publisher
                .Setup(p => p.PublishAsync(It.IsAny<ArrayOf<EndpointDescription>>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("publish boom"));
            ServerLoadDirector director = Configure(Mock.Of<IServerDirectionPolicy>(), publisher: publisher.Object);

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(NormalUrl, LocalEndpoints());

            Assert.That(redirect, Is.False, "a failing endpoint publish must not break local discovery");
        }

        private static ServerLoadDirector Configure(
            IServerDirectionPolicy policy,
            IPeerEndpointDirectory? directory = null,
            IPeerEndpointPublisher? publisher = null)
        {
            var director = new ServerLoadDirector(
                new ConstantServiceLevelProvider(255),
                new ConstantLoadWeightProvider(200),
                new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl });
            director.Configure(
                policy,
                directory ?? Mock.Of<IPeerEndpointDirectory>(),
                publisher ?? Mock.Of<IPeerEndpointPublisher>(),
                LocalServerUri);
            return director;
        }

        private static ArrayOf<EndpointDescription> LocalEndpoints()
        {
            return
            [
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://" + LocalServerUri + ":4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    Server = new ApplicationDescription
                    {
                        ApplicationUri = LocalServerUri,
                        ApplicationType = ApplicationType.Server
                    }
                }
            ];
        }
    }
}
