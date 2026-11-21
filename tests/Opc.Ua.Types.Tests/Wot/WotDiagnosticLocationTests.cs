// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// What a <see cref="WotLocation"/> says about where a diagnostic belongs,
    /// including for the Nodes that carry no identity to be located by.
    /// </summary>
    /// <remarks>
    /// A location is read by a human, so the interesting cases are the ones
    /// where there is nothing to say: a location that reads
    /// <c>NodeId=</c> is worse than one that says the diagnostic is about the
    /// document, and a caller reporting on a Node it was handed should not have
    /// to decide that.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDiagnosticLocationTests
    {
        /// <summary>
        /// A Node that carries a NodeId is located by it.
        /// </summary>
        [Test]
        public void ANodeIsLocatedByItsIdentity()
        {
            WotLocation location = WotLocation.FromNode("nsu=urn:test;i=5001", "DataType");

            Assert.Multiple(() =>
            {
                Assert.That(location.NodeId, Is.EqualTo("nsu=urn:test;i=5001"));
                Assert.That(location.Attribute, Is.EqualTo("DataType"));
                Assert.That(location.JsonPointer, Is.Null);
                Assert.That(location.Reference, Is.Null);
                Assert.That(
                    location.ToString(),
                    Is.EqualTo("NodeId=nsu=urn:test;i=5001, Attribute=DataType"));
            });
        }

        /// <summary>
        /// A Node that carries no NodeId locates a diagnostic no better than
        /// the document does, so the location reads as the document rather than
        /// as an empty identity.
        /// </summary>
        [Test]
        public void ANodeWithNoIdentityLocatesTheDocument()
        {
            WotLocation location = WotLocation.FromNode(null);

            Assert.Multiple(() =>
            {
                Assert.That(location.NodeId, Is.Empty);
                Assert.That(location.ToString(), Is.EqualTo("(document)"));
            });
        }

        /// <summary>
        /// A JSON Pointer locates a diagnostic in the WoT document rather than
        /// in the NodeSet the conversion produces.
        /// </summary>
        [Test]
        public void APointerLocatesTheWotDocument()
        {
            WotLocation location = WotLocation.FromPointer("/properties/Speed");

            Assert.Multiple(() =>
            {
                Assert.That(location.JsonPointer, Is.EqualTo("/properties/Speed"));
                Assert.That(location.NodeId, Is.Null);
                Assert.That(location.ToString(), Is.EqualTo("JsonPointer=/properties/Speed"));
            });
        }
    }
}
