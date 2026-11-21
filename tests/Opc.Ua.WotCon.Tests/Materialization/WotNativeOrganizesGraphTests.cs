// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Verifies that native Organizes targets are address-space node identifiers rather
    /// than document dependencies.
    /// </summary>
    [TestFixture]
    public sealed class WotNativeOrganizesGraphTests
    {
        /// <summary>
        /// A node identifier target names an address-space node, so it is not a document dependency.
        /// </summary>
        [Test]
        public void OrganizesNativeNodeIdTargetIsNotADocumentDependency()
        {
            const string href = "nsu=http://opcfoundation.org/UA/DI/;i=15034";

            var references = WotDependencyGraph.ExtractReferences(Document(href), 64);

            Assert.That(references, Is.Empty);
        }

        /// <summary>
        /// A target that is not a node identifier remains a document dependency.
        /// </summary>
        [Test]
        public void OrganizesDocumentTargetRemainsADocumentDependency()
        {
            const string href = "urn:r30:device-features";

            var references = WotDependencyGraph.ExtractReferences(Document(href), 64);

            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references.Single().Href, Is.EqualTo(href));
            Assert.That(references.Single().RefType, Is.EqualTo("ua:Organizes"));
        }

        private static byte[] Document(string href)
        {
            return Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"ua:Organizes\",\"href\":\"" + href + "\"}]}");
        }
    }
}
