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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
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
