/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Replaces draft NodeId/offer registries with exact resource identity and complete source policy tests.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelSourceRegistryTests
    {
        [Test]
        public void SourceLookupUsesExactUriAndRuntimeNamespaceMapping()
        {
            var source = Source("urn:source:one", new ExpandedNodeId(41u, "urn:source:model"));
            var registry = new DataChannelSourceRegistry();
            registry.Register(source);
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unrelated");
            ushort index = namespaces.GetIndexOrAppend("urn:source:model");
            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGet("urn:source:one", out DataChannelSource? exact), Is.True);
                Assert.That(exact, Is.SameAs(source));
                Assert.That(registry.TryGet("urn:source:ONE", out _), Is.False);
                Assert.That(registry.TryGet(
                    new NodeId(41u, index), namespaces, out DataChannelSource? mapped), Is.True);
                Assert.That(mapped, Is.SameAs(source));
                Assert.That(registry.TryGet(new NodeId(41u, 1), namespaces, out _), Is.False);
            });
        }

        [Test]
        public void DuplicateResourceAndDuplicateSourceIdentityAreRejectedWithoutReplacingPolicy()
        {
            var registry = new DataChannelSourceRegistry();
            var id = new ExpandedNodeId(42u, "urn:source:model");
            DataChannelSource original = Source("urn:source:one", id);
            registry.Register(original);
            Assert.Multiple(() =>
            {
                Assert.That(() => registry.Register(Source("urn:source:one")), Throws.ArgumentException);
                Assert.That(() => registry.Register(Source("urn:source:two", id)), Throws.ArgumentException);
                Assert.That(registry.Sources, Is.EqualTo(new[] { original }));
                Assert.That(() => registry.Register(null!), Throws.ArgumentNullException);
            });
        }

        [TestCase("")]
        [TestCase("relative/resource")]
        public void SourceRejectsNonAbsoluteResource(string resource)
        {
            Assert.That(() => Source(resource), Throws.ArgumentException);
        }

        [Test]
        public void SourceRejectsResourceBeyondUtf8Bound()
        {
            Assert.That(() => Source("urn:" + new string('a', 4093)), Throws.ArgumentException);
        }

        [TestCase(DataChannelDirection.OpenerToAcceptor)]
        [TestCase(DataChannelDirection.Duplex)]
        public void SourceRejectsUnsupportedDirectionInsteadOfWeakeningIt(DataChannelDirection direction)
        {
            DataChannelSource source = Source("urn:source:one");
            DataChannelParametersDataType parameters = Parameters(source);
            parameters.Direction = direction;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
        }

        [Test]
        public void SourceValidatesDeliveryProtectionProtocolAndAllBounds()
        {
            DataChannelSource source = Source("urn:source:one");
            DataChannelParametersDataType parameters = Parameters(source);
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Good));
            parameters.Protocol = DataChannelProfiles.JsonPayload;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
            parameters = Parameters(source);
            parameters.Delivery = DataChannelDeliveryMode.Datagram;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
            parameters = Parameters(source);
            parameters.Protection = DataChannelProtectionMode.Integrity;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
            parameters = Parameters(source);
            parameters.MaxMessageSize++;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.TooLarge));
            parameters = Parameters(source);
            parameters.MaxReceiveBytes++;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.TooLarge));
            parameters = Parameters(source);
            parameters.MaxRate = double.NaN;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
            parameters = Parameters(source);
            parameters.AllowQueueExpiry = true;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
        }

        [Test]
        public void WithdrawingSourceDoesNotAffectOtherResourcesAndCapabilitiesAreSnapshots()
        {
            string[] protocols = [DataChannelProfiles.OpaquePayload];
            var source = new DataChannelSource("urn:source:one", new DataChannelApplicationPolicy(["urn:peer"]))
            {
                SupportedProtocols = ArrayOf.Wrapped(protocols)
            };
            DataChannelSource other = Source("urn:source:two");
            var registry = new DataChannelSourceRegistry();
            registry.Register(source);
            registry.Register(other);
            protocols[0] = DataChannelProfiles.JsonPayload;
            Assert.That(source.SupportedProtocols[0], Is.EqualTo(DataChannelProfiles.OpaquePayload));
            Assert.That(registry.Unregister(source.ResourceUri), Is.True);
            Assert.That(registry.Unregister(source.ResourceUri), Is.False);
            Assert.That(registry.TryGet(source.ResourceUri, out _), Is.False);
            Assert.That(registry.TryGet(other.ResourceUri, out DataChannelSource? retained), Is.True);
            Assert.That(retained, Is.SameAs(other));
            Assert.Multiple(() =>
            {
                Assert.That((int)RequestType.Call, Is.EqualTo(11));
                Assert.That((int)RequestType.UnregisterNodes, Is.EqualTo(34));
#if NET8_0_OR_GREATER
                Assert.That(Enum.GetNames<RequestType>(), Has.Length.EqualTo(35));
#else
                Assert.That(Enum.GetNames(typeof(RequestType)), Has.Length.EqualTo(35));
#endif
            });
        }

        [Test]
        public void DescriptorRejectsUnknownEnumsAndBoundsContentTypeByUtf8Bytes()
        {
            DataChannelSource source = Source("urn:source:one");
            DataChannelParametersDataType parameters = Parameters(source);
            parameters.ContentType = new string('a', 255);
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Good));
            parameters.ContentType = new string('a', 256);
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
            parameters.ContentType = new string('é', 128);
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
            parameters = Parameters(source);
            parameters.Direction = (DataChannelDirection)99;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
            parameters = Parameters(source);
            parameters.Delivery = (DataChannelDeliveryMode)99;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
            parameters = Parameters(source);
            parameters.Protection = (DataChannelProtectionMode)99;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.InvalidArgument));
        }

        private static DataChannelSource Source(string resource, ExpandedNodeId nodeId = default)
        {
            return new DataChannelSource(resource, new DataChannelApplicationPolicy(["urn:peer"]))
            {
                SourceNodeId = nodeId,
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096
            };
        }

        private static DataChannelParametersDataType Parameters(DataChannelSource source)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = source.ResourceUri,
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
                Direction = DataChannelDirection.AcceptorToOpener,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = source.MaxMessageSize,
                MaxReceiveBytes = source.MaxReceiveBytes
            };
        }
    }
}
