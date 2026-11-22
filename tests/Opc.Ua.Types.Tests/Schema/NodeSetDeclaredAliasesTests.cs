// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Types.Tests.Schema
{
    /// <summary>
    /// A document's own <c>&lt;Aliases&gt;</c> table read as a resolver that
    /// delegates to a caller's policy. It is the one place the order of the
    /// two sources is stated, so it is stated here too.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public class NodeSetDeclaredAliasesTests
    {
        /// <summary>
        /// A policy that knows one name, which is enough to tell whether the
        /// fallback was consulted.
        /// </summary>
        private sealed class SingleNameAliasResolver : INodeSetAliasResolver
        {
            public SingleNameAliasResolver(string name, string nodeId)
            {
                m_name = name;
                m_nodeId = nodeId;
            }

            public bool TryResolve(string alias, out string nodeId)
            {
                if (string.Equals(alias, m_name, StringComparison.Ordinal))
                {
                    nodeId = m_nodeId;
                    return true;
                }
                nodeId = string.Empty;
                return false;
            }

            private readonly string m_name;
            private readonly string m_nodeId;
        }

        [Test]
        public void ADeclaredNameResolvesToWhatTheDocumentBoundItTo()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromDeclarations(
                [new NodeIdAlias { Alias = "Double", Value = "i=11" }]);

            Assert.That(aliases.TryResolve("Double", out string nodeId), Is.True);
            Assert.That(nodeId, Is.EqualTo("i=11"));
        }

        [Test]
        public void AnUndeclaredNameIsUnresolvedWithoutAPolicy()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromNodeSet(new UANodeSet());

            Assert.That(aliases.TryResolve("HasComponent", out string nodeId), Is.False);
            Assert.That(nodeId, Is.Empty);
        }

        [Test]
        public void AnUndeclaredNameIsAnsweredByThePolicy()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromNodeSet(
                new UANodeSet(),
                new SingleNameAliasResolver("HasComponent", "i=47"));

            Assert.That(aliases.TryResolve("HasComponent", out string nodeId), Is.True);
            Assert.That(nodeId, Is.EqualTo("i=47"));
        }

        [Test]
        public void ADeclarationWinsOverThePolicy()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromDeclarations(
                [new NodeIdAlias { Alias = "HasComponent", Value = "ns=1;i=4711" }],
                new SingleNameAliasResolver("HasComponent", "i=47"));

            Assert.That(aliases.TryResolve("HasComponent", out string nodeId), Is.True);
            Assert.That(
                nodeId,
                Is.EqualTo("ns=1;i=4711"),
                "What a document declares is what the name means in that document.");
        }

        /// <summary>
        /// A name declared twice keeps the first declaration, which is how the
        /// importer reads a repeated name as well.
        /// </summary>
        [Test]
        public void ARepeatedDeclarationKeepsTheFirst()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromDeclarations(
            [
                new NodeIdAlias { Alias = "Double", Value = "i=11" },
                new NodeIdAlias { Alias = "Double", Value = "i=12" }
            ]);

            Assert.That(aliases.TryResolve("Double", out string nodeId), Is.True);
            Assert.That(nodeId, Is.EqualTo("i=11"));
        }

        /// <summary>
        /// A declaration that binds a name to nothing states nothing, so the
        /// name is left to the policy rather than read as an empty identifier.
        /// </summary>
        [Test]
        public void ADeclarationWithoutAValueDeclaresNothing()
        {
            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromDeclarations(
                [new NodeIdAlias { Alias = "Double", Value = string.Empty }],
                new SingleNameAliasResolver("Double", "i=11"));

            Assert.That(aliases.TryResolve("Double", out string nodeId), Is.True);
            Assert.That(nodeId, Is.EqualTo("i=11"));
        }

        [Test]
        public void ASerializedDocumentIsReadThroughItsOwnTable()
        {
            XElement root = XElement.Parse(
                """
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <Aliases>
                    <Alias Alias="Double">i=11</Alias>
                    <Alias Alias="HasComponent">i=47</Alias>
                  </Aliases>
                </UANodeSet>
                """);

            NodeSetDeclaredAliases aliases = NodeSetDeclaredAliases.FromDocument(root);

            Assert.Multiple(() =>
            {
                Assert.That(aliases.TryResolve("Double", out string dataType), Is.True);
                Assert.That(dataType, Is.EqualTo("i=11"));
                Assert.That(aliases.TryResolve("HasComponent", out string reference), Is.True);
                Assert.That(reference, Is.EqualTo("i=47"));
                Assert.That(aliases.TryResolve("Organizes", out _), Is.False);
            });
        }

        [Test]
        public void ADocumentIsRequiredToReadOne()
        {
            Assert.Throws<ArgumentNullException>(
                () => NodeSetDeclaredAliases.FromDocument(null!));
        }

        /// <summary>
        /// The resolver is a lookup and never a rewrite: what it does not know
        /// it reports as unknown so the caller keeps the value as written.
        /// </summary>
        [Test]
        public void AVendorNameIsReportedAsUnknown()
        {
            var policies = new List<INodeSetAliasResolver>
            {
                NodeSetDeclaredAliases.FromNodeSet(null),
                NodeSetDeclaredAliases.FromDeclarations(
                    [new NodeIdAlias { Alias = "Double", Value = "i=11" }],
                    new SingleNameAliasResolver("HasComponent", "i=47"))
            };

            foreach (INodeSetAliasResolver policy in policies)
            {
                Assert.That(policy.TryResolve("VendorSpecificReference", out string nodeId), Is.False);
                Assert.That(nodeId, Is.Empty);
            }
        }
    }
}
