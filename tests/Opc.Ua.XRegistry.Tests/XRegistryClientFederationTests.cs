// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the rejection paths of the native federation client that run before any wire traffic.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryClientFederationTests
    {
        /// <summary>
        /// Verifies that following an external reference requires a generation-bound session provider.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsSessionWithoutBindingProvider()
        {
            GenericXRegistryClient client = CreateClient();
            XRegistryFederationTarget target = CreateTarget();
            Mock<ISession> referencing = new();

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.FollowExternalReferenceAsync(
                    referencing.Object, new NodeId("proxy", 1), target).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Verifies that following an external reference rejects null arguments.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsNullArguments()
        {
            GenericXRegistryClient client = CreateClient();
            XRegistryFederationTarget target = CreateTarget();

            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.FollowExternalReferenceAsync(
                    null!, new NodeId("proxy", 1), target).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.FollowExternalReferenceAsync(
                    new Mock<ISession>().Object, new NodeId("proxy", 1), null!).ConfigureAwait(false));
        }

        /// <summary>
        /// Verifies that verification rejects a null trusted target.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsNullTarget()
        {
            GenericXRegistryClient client = CreateClient();

            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.VerifyLogicalResourceAsync(
                    null!, "opc.tcp://remote:4840").ConfigureAwait(false));
        }

        private static GenericXRegistryClient CreateClient()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            return new GenericXRegistryClient(
                session.Object,
                XRegistryWellKnown.XRegistryNamespaceUri,
                NUnitTelemetryContext.Create());
        }

        private static XRegistryFederationTarget CreateTarget()
        {
            var origin = new RegistryOriginDataType
            {
                OriginUri = "urn:origin",
                ServerUri = string.Empty,
                RegistryNodeId = ExpandedNodeId.Null
            };
            return new XRegistryFederationTarget(
                origin, "urn:app", new ExpandedNodeId("resource", "urn:example:nodes"),
                "/groups/schemas/resources/one");
        }
    }
}
