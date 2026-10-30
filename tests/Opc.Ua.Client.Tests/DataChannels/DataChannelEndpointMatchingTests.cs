/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Client.Tests.DataChannels
{
    /// <summary>
    /// An ordinary CreateSession response cannot replace a selected data profile or resource.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelEndpointMatchingTests
    {
        [TestCase(DataChannelProfiles.SharedUasc, DataChannelProfiles.SharedResource, true)]
        [TestCase(Profiles.UaTcpTransport, DataChannelProfiles.SharedResource, false)]
        [TestCase(Profiles.UaTcpTransport, "/ordinary", false)]
        [TestCase(DataChannelProfiles.DedicatedUasc, DataChannelProfiles.DedicatedResource, false)]
        [TestCase(DataChannelProfiles.SharedUasc, DataChannelProfiles.DedicatedResource, false)]
        [TestCase(DataChannelProfiles.SharedUasc, "/wrong", false)]
        [TestCase(DataChannelProfiles.SharedUasc, "/flow/v1/shared?mode=ordinary", false)]
        public void SharedSessionRequiresExactProfileAndResource(string actualProfile, string actualPath, bool expected)
        {
            EndpointDescription selected = Endpoint(DataChannelProfiles.SharedUasc, DataChannelProfiles.SharedResource);
            EndpointDescription returned = Endpoint(actualProfile, actualPath);
            Assert.That(DataChannelEndpointMatching.CanMatch(
                selected, returned, new Uri(selected.EndpointUrl!), new Uri(returned.EndpointUrl!)),
                Is.EqualTo(expected));
        }

        [TestCase(DataChannelProfiles.DedicatedUasc)]
        [TestCase(DataChannelProfiles.Quic)]
        [TestCase(DataChannelProfiles.WebTransport)]
        public void DataOnlyProfileNeverAcquiresSessionSemantics(string profile)
        {
            EndpointDescription description = Endpoint(profile, "/data");
            Assert.That(DataChannelEndpointMatching.CanMatch(
                description, description, new Uri(description.EndpointUrl!), new Uri(description.EndpointUrl!)),
                Is.False);
        }

        [Test]
        public void OrdinaryClientDoesNotAdoptSharedDescriptionAndKeepsOrdinaryMatchingRules()
        {
            EndpointDescription ordinary = Endpoint(Profiles.UaTcpTransport, "/ordinary");
            EndpointDescription shared = Endpoint(DataChannelProfiles.SharedUasc, DataChannelProfiles.SharedResource);
            EndpointDescription anotherOrdinary = Endpoint(Profiles.UaTcpTransport, "/other-ordinary");
            Assert.Multiple(() =>
            {
                Assert.That(DataChannelEndpointMatching.CanMatch(
                    ordinary, shared, new Uri(ordinary.EndpointUrl!), new Uri(shared.EndpointUrl!)), Is.False);
                Assert.That(DataChannelEndpointMatching.CanMatch(
                    ordinary, anotherOrdinary, new Uri(ordinary.EndpointUrl!), new Uri(anotherOrdinary.EndpointUrl!)),
                    Is.True);
            });
        }

        private static EndpointDescription Endpoint(string profile, string path)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840" + path,
                TransportProfileUri = profile,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
        }

        [Test]
        public void CanonicalPart30CanMatchAcceptsExactCanonicalPair(
            [Values("opc.tcp", "opc.wss")] string scheme, [Values] bool sameInstance)
        {
            EndpointDescription selected = CanonicalPart30Endpoint(scheme, DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            EndpointDescription returned = sameInstance ? selected : (EndpointDescription)selected.Clone();

            Assert.Multiple(() =>
            {
                Assert.That(CanonicalPart30Matches(selected, returned), Is.True);
                Assert.That(CanonicalPart30Matches(returned, selected), Is.True);
                Assert.That(returned.TransportProfileUri,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
                Assert.That(returned.EndpointUrl, Is.EqualTo($"{scheme}://localhost:4840/pubsub/uasc"));
            });
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public void CanonicalPart30CanMatchRejectsCrossProfileSelectionsInBothDirections(string scheme)
        {
            EndpointDescription canonical = CanonicalPart30Endpoint(scheme, DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            (string Profile, string Resource)[] others =
            [
                (DataChannelProfiles.SharedUasc, "/flow/v1/shared"),
                (DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated"),
                (Profiles.UaTcpTransport, "/ordinary")
            ];
            Assert.That(CanonicalPart30Matches(canonical, canonical), Is.True);
            foreach ((string profile, string resource) in others)
            {
                EndpointDescription other = CanonicalPart30Endpoint(scheme, profile, resource);
                Assert.Multiple(() =>
                {
                    Assert.That(CanonicalPart30Matches(canonical, other), Is.False, profile);
                    Assert.That(CanonicalPart30Matches(other, canonical), Is.False, profile);
                    Assert.That(CanonicalPart30Matches(other, other),
                        Is.EqualTo(profile != DataChannelProfiles.DedicatedUasc), profile);
                });
            }
        }

        [Test]
        public void CanonicalPart30CanMatchRejectsCrossResourcesAndQueriesInBothDirections(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values(DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc)] string profile)
        {
            string path = profile == DataChannelProfiles.PubSubUasc ? "/pubsub/uasc" : "/flow/v1/shared";
            string otherPath = profile == DataChannelProfiles.PubSubUasc ? "/flow/v1/shared" : "/pubsub/uasc";
            EndpointDescription selected = CanonicalPart30Endpoint(scheme, profile, path);
            Assert.That(CanonicalPart30Matches(selected, selected), Is.True);
            foreach (string invalid in new[] { otherPath, "/ordinary", "/pubsub/other", path + "/", path + "?fallback=true" })
            {
                EndpointDescription returned = CanonicalPart30Endpoint(scheme, profile, invalid);
                Assert.Multiple(() =>
                {
                    Assert.That(CanonicalPart30Matches(selected, returned), Is.False, invalid);
                    Assert.That(CanonicalPart30Matches(returned, selected), Is.False, invalid);
                    Assert.That(CanonicalPart30Matches(returned, returned), Is.False, invalid);
                });
            }
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public void CanonicalPart30CanMatchPreservesOrdinaryAndLegacyRules(string scheme)
        {
            EndpointDescription shared = CanonicalPart30Endpoint(scheme, DataChannelProfiles.SharedUasc, "/flow/v1/shared");
            EndpointDescription dedicated = CanonicalPart30Endpoint(
                scheme, DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated");
            EndpointDescription ordinary = CanonicalPart30Endpoint(scheme, Profiles.UaTcpTransport, "/ordinary");
            EndpointDescription otherOrdinary = CanonicalPart30Endpoint(scheme, Profiles.UaTcpTransport, "/other?x=1");
            EndpointDescription reserved = CanonicalPart30Endpoint(scheme, Profiles.UaTcpTransport, "/pubsub/other");
            Assert.Multiple(() =>
            {
                Assert.That(CanonicalPart30Matches(shared, (EndpointDescription)shared.Clone()), Is.True);
                Assert.That(CanonicalPart30Matches(dedicated, dedicated), Is.False);
                Assert.That(CanonicalPart30Matches(ordinary, otherOrdinary), Is.True);
                Assert.That(CanonicalPart30Matches(otherOrdinary, ordinary), Is.True);
                Assert.That(CanonicalPart30Matches(ordinary, reserved), Is.False);
                Assert.That(CanonicalPart30Matches(reserved, ordinary), Is.False);
                Assert.That(CanonicalPart30Matches(ordinary, shared), Is.False);
                Assert.That(CanonicalPart30Matches(shared, ordinary), Is.False);
            });
        }

        private static EndpointDescription CanonicalPart30Endpoint(string scheme, string profile, string path)
        {
            EndpointDescription endpoint = Endpoint(profile, path);
            endpoint.EndpointUrl = $"{scheme}://localhost:4840{path}";
            return endpoint;
        }

        private static bool CanonicalPart30Matches(EndpointDescription expected, EndpointDescription actual)
        {
            return DataChannelEndpointMatching.CanMatch(
                expected, actual, new Uri(expected.EndpointUrl!), new Uri(actual.EndpointUrl!));
        }
    }
}
