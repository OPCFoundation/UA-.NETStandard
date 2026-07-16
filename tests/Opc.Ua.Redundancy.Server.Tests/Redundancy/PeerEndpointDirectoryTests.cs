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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the GetEndpoints load-direction peer endpoint directory
    /// (<see cref="SharedPeerEndpointPublisher"/> / <see cref="SharedPeerEndpointDirectory"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class PeerEndpointDirectoryTests
    {
        [Test]
        public async Task PublishAndReadRoundTripReturnsEndpointsAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();

            var publisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, "urn:server:a");
            ArrayOf<EndpointDescription> published =
            [
                Endpoint("opc.tcp://a:4840", MessageSecurityMode.None, "urn:server:a"),
                Endpoint("opc.tcp://a:4841", MessageSecurityMode.SignAndEncrypt, "urn:server:a")
            ];
            await publisher.PublishAsync(published).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:a").ConfigureAwait(false)).ToArray();

            Assert.That(resolved, Has.Length.EqualTo(2));
            Assert.That(resolved[0].EndpointUrl, Is.EqualTo("opc.tcp://a:4840"));
            Assert.That(resolved[1].EndpointUrl, Is.EqualTo("opc.tcp://a:4841"));
            Assert.That(resolved[1].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [Test]
        public async Task UnknownPeerReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:missing").ConfigureAwait(false)).ToArray();

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task EmptyServerUriReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync(string.Empty).ConfigureAwait(false)).ToArray();

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task MalformedRecordReturnsEmptyAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions();
            await store.SetAsync("endpoint/urn:server:a", ByteString.From(new byte[] { 7, 7, 7 })).ConfigureAwait(false);

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] resolved = (await directory.GetEndpointsAsync("urn:server:a").ConfigureAwait(false)).ToArray();

            Assert.That(resolved, Is.Empty, "an undecodable endpoint record must be dropped (fail-closed)");
        }

        private static EndpointDescription Endpoint(string url, MessageSecurityMode mode, string serverUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    ApplicationType = ApplicationType.Server
                }
            };
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
