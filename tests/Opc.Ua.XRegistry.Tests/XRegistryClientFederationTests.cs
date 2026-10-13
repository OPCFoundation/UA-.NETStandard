/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Server;

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
        public void FollowExternalReferenceRejectsSessionWithoutBindingProvider()
        {
            GenericXRegistryClient client = CreateClient();
            XRegistryFederationTarget target = CreateTarget();
            Mock<ISession> referencing = new();

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.FollowExternalReferenceAsync(
                    referencing.Object, new NodeId("proxy", 1), target).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Verifies that following an external reference rejects null arguments.
        /// </summary>
        [Test]
        public void FollowExternalReferenceRejectsNullArguments()
        {
            GenericXRegistryClient client = CreateClient();
            XRegistryFederationTarget target = CreateTarget();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.FollowExternalReferenceAsync(
                    null!, new NodeId("proxy", 1), target).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.FollowExternalReferenceAsync(
                    new Mock<ISession>().Object, new NodeId("proxy", 1), null!).ConfigureAwait(false));
        }

        /// <summary>
        /// Verifies that verification rejects a null trusted target.
        /// </summary>
        [Test]
        public void VerifyLogicalResourceRejectsNullTarget()
        {
            GenericXRegistryClient client = CreateClient();

            Assert.ThrowsAsync<ArgumentNullException>(
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