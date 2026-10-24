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
    }
}
