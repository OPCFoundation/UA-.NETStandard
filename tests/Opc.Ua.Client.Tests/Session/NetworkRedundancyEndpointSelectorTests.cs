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

#nullable enable

using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Tests for <see cref="NetworkRedundancyEndpointSelector"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("NetworkRedundancy")]
    public sealed class NetworkRedundancyEndpointSelectorTests
    {
        [Test]
        public void SelectNextReturnsAlternateEndpointForSameLogicalServer()
        {
            ConfiguredEndpoint primary = CreateEndpoint("urn:server", "opc.tcp://path-a:4840");
            ConfiguredEndpoint alternate = CreateEndpoint("urn:server", "opc.tcp://path-b:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [alternate]);

            ConfiguredEndpoint? next = selector.SelectNext(primary);

            Assert.That(next, Is.SameAs(alternate));
        }

        [Test]
        public void SelectNextWrapsAndKeepsSameLogicalSession()
        {
            ConfiguredEndpoint primary = CreateEndpoint("urn:server", "opc.tcp://path-a:4840");
            ConfiguredEndpoint alternate = CreateEndpoint("urn:server", "opc.tcp://path-b:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [alternate]);

            ConfiguredEndpoint? next = selector.SelectNext(alternate);

            Assert.That(next, Is.SameAs(primary));
            Assert.That(next!.Description.Server.ApplicationUri, Is.EqualTo("urn:server"));
        }

        [Test]
        public void SelectNextIgnoresDifferentLogicalServer()
        {
            ConfiguredEndpoint primary = CreateEndpoint("urn:server", "opc.tcp://path-a:4840");
            ConfiguredEndpoint otherServer = CreateEndpoint("urn:other", "opc.tcp://path-b:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [otherServer]);

            Assert.That(selector.HasAlternates, Is.False);
            Assert.That(selector.SelectNext(primary), Is.Null);
        }

        [Test]
        public void SelectNextReturnsPrimaryWhenCurrentEndpointIsUnknown()
        {
            ConfiguredEndpoint primary = CreateEndpoint("urn:server", "opc.tcp://path-a:4840");
            ConfiguredEndpoint alternate = CreateEndpoint("urn:server", "opc.tcp://path-b:4840");
            ConfiguredEndpoint unknown = CreateEndpoint("urn:server", "opc.tcp://path-c:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [alternate]);

            ConfiguredEndpoint? next = selector.SelectNext(unknown);

            Assert.That(next, Is.SameAs(primary));
        }

        [Test]
        public void SelectNextDoesNotAddDuplicateAlternateEndpoint()
        {
            ConfiguredEndpoint primary = CreateEndpoint("urn:server", "opc.tcp://path-a:4840");
            ConfiguredEndpoint alternate = CreateEndpoint("urn:server", "opc.tcp://path-b:4840");
            ConfiguredEndpoint duplicate = CreateEndpoint("urn:server", "opc.tcp://PATH-B:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [alternate, duplicate]);

            ConfiguredEndpoint? next = selector.SelectNext(alternate);

            Assert.That(next, Is.SameAs(primary));
        }

        [Test]
        public void SelectNextAllowsMissingApplicationUriForSameLogicalServer()
        {
            ConfiguredEndpoint primary = CreateEndpoint(string.Empty, "opc.tcp://path-a:4840");
            ConfiguredEndpoint alternate = CreateEndpoint("urn:server", "opc.tcp://path-b:4840");
            var selector = new NetworkRedundancyEndpointSelector(
                primary,
                [alternate]);

            Assert.That(selector.HasAlternates, Is.True);
            Assert.That(selector.SelectNext(primary), Is.SameAs(alternate));
        }

        private static ConfiguredEndpoint CreateEndpoint(string serverUri, string endpointUrl)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri
                }
            };

            return new ConfiguredEndpoint(null, description, configuration: null);
        }
    }
}
