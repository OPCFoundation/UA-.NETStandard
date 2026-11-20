// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    public sealed partial class SessionContinuationOwnershipTests
    {
        /// <summary>
        /// Continuation points identify only their issuing generation, not declared dependency owners.
        /// </summary>
        [Test]
        public void ContinuationIdentifiesOnlyItsIssuingGeneration()
        {
            var issuer = new Mock<IAsyncNodeManager>();
            var other = new Mock<IAsyncNodeManager>();
            using ContinuationPoint point = CreatePoint(issuer.Object);

            Assert.That(point.Generation!.TryGetGeneration(out NodeManagerGeneration? generation), Is.True);
            Assert.That(generation!.NodeManager, Is.SameAs(issuer.Object));
            Assert.That(point.Generation.Identifies(NodeManagerGenerationRegistry.GetOrCreate(issuer.Object)), Is.True);
            Assert.That(point.Generation.Identifies(NodeManagerGenerationRegistry.GetOrCreate(other.Object)), Is.False);
        }

        /// <summary>
        /// Retirement removes available continuations for the exact generation and invalidates checked-out points.
        /// </summary>
        [Test]
        public void GenerationRetirementInvalidatesOnlyItsContinuationPoints()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 3, 1, null);
            var issuer = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            using ContinuationPoint checkedOut = CreatePoint(issuer.Object);
            using ContinuationPoint available = CreatePoint(issuer.Object);
            using ContinuationPoint independent = CreatePoint(unrelated.Object);
            holder.SaveBrowse(checkedOut);
            holder.SaveBrowse(available);
            holder.SaveBrowse(independent);
            ByteString checkedOutToken = checkedOut.Id.ToByteArray().ToByteString();
            ByteString availableToken = available.Id.ToByteArray().ToByteString();
            ByteString independentToken = independent.Id.ToByteArray().ToByteString();

            Assert.That(holder.RestoreBrowse(checkedOutToken), Is.SameAs(checkedOut));
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(issuer.Object);
            holder.RemoveForGeneration(generation);

            Assert.That(holder.RestoreBrowse(availableToken), Is.Null);
            Assert.That(holder.RestoreBrowse(checkedOutToken), Is.Null);
            Assert.That(holder.HasBrowseForGeneration(NodeManagerGenerationRegistry.GetOrCreate(unrelated.Object)), Is.True);
            Assert.That(holder.HasBrowseForGeneration(generation), Is.True,
                "A checked-out point remains owned until its in-flight request releases it.");

            checkedOut.Dispose();

            Assert.That(holder.HasBrowseForGeneration(generation), Is.False);
            Assert.That(holder.RestoreBrowse(independentToken), Is.SameAs(independent));
            holder.Clear();
        }
    }
}
