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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Live-server tests for the ReferenceType half of the WoT Binding
    /// Section 5.1.5 local context.
    /// </summary>
    /// <remarks>
    /// A real <see cref="ReferenceServer"/> is started and its AddressSpace is
    /// asked for the names a relation may use. The resolver holds no table of
    /// its own, so every answer here is read out of the Server: the
    /// ReferenceType hierarchy it loaded, and the NodeClass, BrowseName,
    /// InverseName and Symmetric Attributes of each Node in it. That is what
    /// makes a companion model's own ReferenceType resolvable by exactly the
    /// same path as a base-namespace one.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotCon")]
    [Category("Server")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class AddressSpaceWotReferenceTypeLiveTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(AddressSpaceWotReferenceTypeLiveTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_resolver = new AddressSpaceWotNodeResolver(m_server.CurrentInstance);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            m_server?.Dispose();

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// A BrowseName reads the reference forward.
        /// </summary>
        [Test]
        public async Task ResolvesAReferenceTypeOfTheLoadedAddressSpaceByBrowseNameAsync()
        {
            ArrayOf<WotResolvedReferenceType> matches = await m_resolver!
                .ResolveReferenceTypesAsync(OpcUaNamespace, "HasComponent")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(matches[0].NodeId, Is.EqualTo("i=47"));
                Assert.That(matches[0].Name, Is.EqualTo("HasComponent"));
                Assert.That(matches[0].IsForward, Is.True);
            });
        }

        /// <summary>
        /// The InverseName the Server states for the same Node reads it
        /// backwards, which is the direction a link <c>rel</c> expresses
        /// (Section 5.1.2).
        /// </summary>
        [Test]
        public async Task ResolvesAReferenceTypeOfTheLoadedAddressSpaceByInverseNameAsync()
        {
            ArrayOf<WotResolvedReferenceType> matches = await m_resolver!
                .ResolveReferenceTypesAsync(OpcUaNamespace, "ComponentOf")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(matches[0].NodeId, Is.EqualTo("i=47"));
                Assert.That(matches[0].Name, Is.EqualTo("ComponentOf"));
                Assert.That(matches[0].IsForward, Is.False);
            });
        }

        /// <summary>
        /// A symmetric ReferenceType has one name for both directions, so the
        /// AddressSpace offers it once, forward.
        /// </summary>
        [Test]
        public async Task OffersASymmetricReferenceTypeOfTheAddressSpaceOnceAsync()
        {
            ArrayOf<WotResolvedReferenceType> matches = await m_resolver!
                .ResolveReferenceTypesAsync(OpcUaNamespace, "NonHierarchicalReferences")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].IsForward, Is.True);
        }

        /// <summary>
        /// An ObjectType the Server holds is not a relation, so its BrowseName
        /// resolves to no ReferenceType. The converter reports that as the
        /// wrong NodeClass rather than as unresolvable.
        /// </summary>
        [Test]
        public async Task DoesNotOfferAnObjectTypeOfTheAddressSpaceAsARelationAsync()
        {
            ArrayOf<WotResolvedReferenceType> matches = await m_resolver!
                .ResolveReferenceTypesAsync(OpcUaNamespace, "BaseObjectType")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        /// <summary>
        /// A name in a namespace the Server has not loaded resolves to
        /// nothing: the prefix of a compact model name binds the namespace.
        /// </summary>
        [Test]
        public async Task DoesNotResolveARelationInAnUnloadedNamespaceAsync()
        {
            ArrayOf<WotResolvedReferenceType> matches = await m_resolver!
                .ResolveReferenceTypesAsync("urn:never:loaded", "HasComponent")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.Zero);
        }

        private const string OpcUaNamespace = "http://opcfoundation.org/UA/";

        private ServerFixture<ReferenceServer>? m_fixture;
        private ReferenceServer? m_server;
        private AddressSpaceWotNodeResolver? m_resolver;
        private string? m_pkiRoot;
    }
}
