// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies that trusted federation identities own their Opaque identifier bytes and do not
    /// follow later mutation of the caller's buffer.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryOpaqueIdentityTests
    {
        /// <summary>
        /// Verifies that a pinned Resource NodeId keeps its original Opaque bytes.
        /// </summary>
        [Test]
        public void TargetResourceNodeIdIgnoresLaterBufferMutation()
        {
            byte[] buffer = [1, 2, 3, 4, 5, 6, 7, 8];
            byte[] original = (byte[])buffer.Clone();
            var resource = new ExpandedNodeId(new ByteString(new ReadOnlyMemory<byte>(buffer)), "urn:example:nodes");
            XRegistryFederationTarget target = CreateTarget(ExpandedNodeId.Null, resource, "urn:origin");

            buffer[0] = 0xFF;

            Assert.That(resource.TryGetValue(out ByteString aliased), Is.True);
            Assert.That(aliased.Span.ToArray(), Is.EqualTo(buffer), "control: the caller buffer is observable");
            Assert.That(target.ResourceNodeId.TryGetValue(out ByteString captured), Is.True);
            Assert.That(captured.Span.ToArray(), Is.EqualTo(original));
            Assert.That(captured.Length, Is.EqualTo(original.Length));
            Assert.That(target.ResourceNodeId.NamespaceUri, Is.EqualTo("urn:example:nodes"));
            Assert.That(target.ResourceNodeId.ServerIndex, Is.Zero);
            Assert.That(target.ResourceNodeId.NamespaceIndex, Is.Zero);
            Assert.That(
                target.ResourceNodeId,
                Is.EqualTo(new ExpandedNodeId(new ByteString(original), "urn:example:nodes")));
            Assert.That(
                target.ResourceNodeId.GetHashCode(),
                Is.EqualTo(new ExpandedNodeId(new ByteString(original), "urn:example:nodes").GetHashCode()));
        }

        /// <summary>
        /// Verifies that a pinned application/root origin keeps its original Opaque registry bytes.
        /// </summary>
        [Test]
        public void TargetOriginRegistryNodeIdIgnoresLaterBufferMutation()
        {
            byte[] buffer = [9, 8, 7, 6, 5, 4, 3, 2];
            byte[] original = (byte[])buffer.Clone();
            var root = new ExpandedNodeId(new ByteString(new ReadOnlyMemory<byte>(buffer)), "urn:example:nodes");
            var independent = new ExpandedNodeId(new ByteString(original), "urn:example:nodes");
            XRegistryFederationTarget target = CreateTarget(
                root, new ExpandedNodeId("resource", "urn:example:nodes"), string.Empty, "urn:app");

            buffer[7] = 0xFF;

            Assert.That(target.OriginRegistry.RegistryNodeId, Is.EqualTo(independent));
            Assert.That(() => target.VerifyOrigin(CreateOrigin(independent)), Throws.Nothing);
            Assert.That(
                () => target.VerifyOrigin(CreateOrigin(root)),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Verifies that non-Opaque identifiers keep their semantics.
        /// </summary>
        [Test]
        public void TargetNonOpaqueNodeIdsKeepTheirIdentity()
        {
            var guid = new ExpandedNodeId(Guid.NewGuid(), "urn:example:nodes");
            XRegistryFederationTarget guidTarget = CreateTarget(ExpandedNodeId.Null, guid, "urn:origin");
            var numeric = new ExpandedNodeId(42u, "urn:example:nodes");
            XRegistryFederationTarget numericTarget = CreateTarget(ExpandedNodeId.Null, numeric, "urn:origin");
            var text = new ExpandedNodeId("resource", "urn:example:nodes");
            XRegistryFederationTarget textTarget = CreateTarget(ExpandedNodeId.Null, text, "urn:origin");

            Assert.That(guidTarget.ResourceNodeId, Is.EqualTo(guid));
            Assert.That(numericTarget.ResourceNodeId, Is.EqualTo(numeric));
            Assert.That(textTarget.ResourceNodeId, Is.EqualTo(text));
        }

        /// <summary>
        /// Verifies that a client root NodeId keeps its original Opaque bytes.
        /// </summary>
        [Test]
        public void ClientRegistryNodeIdIgnoresLaterBufferMutation()
        {
            byte[] buffer = [10, 20, 30, 40, 50, 60];
            byte[] original = (byte[])buffer.Clone();
            var namespaceUris = new NamespaceTable();
            ushort index = namespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            var root = new NodeId(new ByteString(new ReadOnlyMemory<byte>(buffer)), index);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            var client = new GenericXRegistryClient(
                session.Object,
                XRegistryWellKnown.XRegistryNamespaceUri,
                root,
                NUnitTelemetryContext.Create());

            buffer[3] = 0xFF;

            Assert.That(root.TryGetValue(out ByteString aliased), Is.True);
            Assert.That(aliased.Span.ToArray(), Is.EqualTo(buffer), "control: the caller buffer is observable");
            Assert.That(client.RegistryNodeId.TryGetValue(out ByteString captured), Is.True);
            Assert.That(captured.Span.ToArray(), Is.EqualTo(original));
            Assert.That(client.RegistryNodeId.NamespaceIndex, Is.EqualTo(index));
            Assert.That(client.RegistryNodeId, Is.EqualTo(new NodeId(new ByteString(original), index)));
        }

        private static RegistryOriginDataType CreateOrigin(ExpandedNodeId root, string serverUri = "urn:app")
        {
            return new RegistryOriginDataType
            {
                OriginUri = string.Empty,
                ServerUri = serverUri,
                RegistryNodeId = root
            };
        }

        private static XRegistryFederationTarget CreateTarget(
            ExpandedNodeId root,
            ExpandedNodeId resource,
            string originUri,
            string serverUri = "urn:app")
        {
            var origin = new RegistryOriginDataType
            {
                OriginUri = originUri,
                ServerUri = originUri.Length == 0 ? serverUri : string.Empty,
                RegistryNodeId = root
            };
            return new XRegistryFederationTarget(origin, serverUri, resource, "/groups/schemas/resources/one");
        }
    }
}
