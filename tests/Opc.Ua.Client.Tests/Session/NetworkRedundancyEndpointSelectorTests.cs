// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
