/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises the identity a Thing Model projects, which is what a WoT
    /// Binding Section 5.1.5 local context indexes its sibling documents by.
    /// </summary>
    [TestFixture]
    public sealed class WotProjectedTypeDescriptionTests
    {
        private const string PumpNamespace = "urn:test:pump";

        /// <summary>
        /// A Thing Model projects an ObjectType, identified by its authored
        /// uav:id and uav:browseName.
        /// </summary>
        [Test]
        public void DescribesTypeProjectedByThingModel()
        {
            using WotDocument document = Parse(
                "tm:ThingModel", "\"uav:id\":\"nsu=urn:test:pump;i=1042\",");

            bool described = WotNodeSetConverter.TryDescribeProjectedType(
                document,
                out string namespaceUri,
                out string browseName,
                out string nodeId);

            Assert.That(described, Is.True);
            Assert.That(namespaceUri, Is.EqualTo(PumpNamespace));
            Assert.That(browseName, Is.EqualTo("Tank"));
            Assert.That(nodeId, Is.EqualTo("nsu=urn:test:pump;i=1042"));
        }

        /// <summary>
        /// A Thing Description projects an instance, so it is never a
        /// type-binding target and describes no type.
        /// </summary>
        [Test]
        public void DescribesNoTypeForThingDescription()
        {
            using WotDocument document = Parse(
                "uav:object", "\"uav:id\":\"nsu=urn:test:pump;i=1042\",");

            bool described = WotNodeSetConverter.TryDescribeProjectedType(
                document,
                out string namespaceUri,
                out string browseName,
                out string nodeId);

            Assert.That(described, Is.False);
            Assert.That(namespaceUri, Is.Empty);
            Assert.That(browseName, Is.Empty);
            Assert.That(nodeId, Is.Empty);
        }

        /// <summary>
        /// The sibling index and conversion must agree on generated identity,
        /// otherwise type binding points at a node that was never emitted.
        /// </summary>
        [Test]
        public void DerivesGeneratedIdentityWhenNoneIsAuthored()
        {
            using WotDocument document = Parse("tm:ThingModel", string.Empty);

            bool described = WotNodeSetConverter.TryDescribeProjectedType(
                document,
                out string namespaceUri,
                out string browseName,
                out string nodeId);

            Assert.That(described, Is.True);
            Assert.That(namespaceUri, Is.EqualTo("urn:test:thing"));
            Assert.That(browseName, Is.EqualTo("Tank"));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(result.Value, Is.Not.Null);

            UANode root = result.Value.Items!.Single(i => i is UAObjectType);
            Assert.That(
                nodeId,
                Is.EqualTo(ToPortableNodeId(root.NodeId!, result.Value.NamespaceUris!)));
        }

        /// <summary>
        /// A null document is a caller error, not a document that describes no
        /// type.
        /// </summary>
        [Test]
        public void ThrowsOnNullDocument()
        {
            Assert.That(
                () => WotNodeSetConverter.TryDescribeProjectedType(
                    null!, out _, out _, out _),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static WotDocument Parse(string typeToken, string idTerm)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"" + typeToken + "\"]," +
                "\"id\":\"urn:test:thing\"," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                idTerm +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}}");
            return WotDocument.Parse(json);
        }

        private static string ToPortableNodeId(string rawNodeId, string[] namespaceUris)
        {
            NodeId parsed = NodeId.Parse(rawNodeId);
            var buffer = new StringBuilder();
            ushort index = parsed.NamespaceIndex;
            if (index != 0)
            {
                Assert.That(index - 1, Is.LessThan(namespaceUris.Length));
                buffer.Append("nsu=")
                    .Append(CoreUtils.EscapeUri(namespaceUris[index - 1]))
                    .Append(';');
            }
            NodeId.Format(
                CultureInfo.InvariantCulture,
                buffer,
                parsed.IdentifierAsString,
                parsed.IdType,
                0);
            return buffer.ToString();
        }
    }
}
