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

using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies private candidate identity and single-owner publication and abort transitions.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class PreparedNodeManagerTests
    {
        /// <summary>
        /// Every required construction input is checked before candidate ownership is established.
        /// </summary>
        [TestCase("nodeManager")]
        [TestCase("externalReferences")]
        [TestCase("generation")]
        public void ConstructorRejectsMissingOwnershipInputs(string parameter)
        {
            PreparedNodeManager candidate = CreateCandidate();
            Assert.That(() => new PreparedNodeManager(
                parameter == "nodeManager" ? null! : candidate.NodeManager,
                parameter == "externalReferences" ? null! : candidate.ExternalReferences,
                parameter == "generation" ? null! : candidate.Generation),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo(parameter));
        }

        /// <summary>
        /// A candidate cannot borrow another resource's identity.
        /// </summary>
        [Test]
        public void ConstructorRejectsForeignGeneration()
        {
            PreparedNodeManager candidate = CreateCandidate();
            Assert.That(() => new PreparedNodeManager(Mock.Of<IAsyncNodeManager>(), [], candidate.Generation),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("generation"));
        }

        /// <summary>
        /// Replacement requires the exact preceding generation in the assigned slot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRejectsForeignOrSkippedPredecessor(bool sameSlot)
        {
            IAsyncNodeManager original = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration predecessor = NodeManagerGenerationRegistry.GetOrCreate(original);
            IAsyncNodeManager replacement = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration next = NodeManagerGenerationRegistry.CreateReplacement(predecessor, replacement);
            IAsyncNodeManager later = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration laterGeneration = sameSlot
                ? NodeManagerGenerationRegistry.CreateReplacement(next, later)
                : NodeManagerGenerationRegistry.CreateAddition(later);
            Assert.That(() => new PreparedNodeManager(later, [], laterGeneration, original),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("replacedNodeManager"));
        }

        /// <summary>
        /// Routing preparation consumes the already assigned identity rather than creating another one.
        /// </summary>
        [Test]
        public void PrivateCandidateRetainsIdentityAndRejectsPrematurePublication()
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.ExternalReferences.Add(new NodeId(1), []);
            Assert.DoesNotThrow(candidate.ValidateForPublication);
            Assert.That(candidate.Publish, Throws.InvalidOperationException);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Published, Is.False);
                Assert.That(candidate.Generation, Is.SameAs(
                    NodeManagerGenerationRegistry.GetOrCreate(candidate.NodeManager)));
                Assert.That(candidate.ExternalReferences.ContainsKey(new NodeId(1)), Is.True);
                Assert.That(candidate.Generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            }
        }

        /// <summary>
        /// Concurrent private abort callers obtain exactly one destruction claim.
        /// </summary>
        [Test]
        public async Task ConcurrentAbortClaimsExactlyOneOwnerAsync()
        {
            PreparedNodeManager candidate = CreateCandidate();
            bool[] claims = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => Task.Run(candidate.TryBeginAbort))).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(claims.Count(claim => claim), Is.EqualTo(1));
                Assert.That(candidate.TryBeginAbort(), Is.False);
                Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
                Assert.That(candidate.Publish, Throws.InvalidOperationException);
                Assert.That(candidate.Published, Is.False);
            }
        }

        /// <summary>
        /// A released routing reservation may close admission before the host claims private destruction.
        /// </summary>
        [Test]
        public void AbortedAdmissionStillAllowsExactlyOnePrivateCleanup()
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.Generation.Gate.Abort();
            Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
            Assert.That(candidate.TryBeginAbort(), Is.True);
            Assert.That(candidate.TryBeginAbort(), Is.False);
        }

        /// <summary>
        /// Publication releases the predecessor and never returns destruction to private preparation.
        /// </summary>
        [Test]
        public void PublishedReplacementReleasesPredecessorAndRejectsAbortAfterRetirement()
        {
            IAsyncNodeManager original = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration previous = NodeManagerGenerationRegistry.GetOrCreate(original);
            IAsyncNodeManager replacement = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration next = NodeManagerGenerationRegistry.CreateReplacement(previous, replacement);
            var candidate = new PreparedNodeManager(replacement, [], next, original);
            Assert.That(candidate.ReplacedNodeManager, Is.SameAs(original));
            next.Gate.Activate();
            candidate.Publish();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidate.Published, Is.True);
                Assert.That(candidate.ReplacedNodeManager, Is.Null);
                Assert.That(candidate.Generation, Is.SameAs(next));
                Assert.That(candidate.Publish, Throws.InvalidOperationException);
                Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
                Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
            }
            next.Gate.Retire();
            Assert.That(candidate.Published, Is.True);
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
            next.Gate.DisposeGeneration();
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
        }

        /// <summary>
        /// A facade that never received publication bookkeeping still cannot abort a retired identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RetiredIdentityRejectsPrivateAbortWithoutPublicationBookkeeping(bool disposed)
        {
            PreparedNodeManager candidate = CreateCandidate();
            candidate.Generation.Gate.Activate();
            candidate.Generation.Gate.Retire();
            if (disposed)
            {
                candidate.Generation.Gate.DisposeGeneration();
            }
            Assert.That(candidate.TryBeginAbort, Throws.InvalidOperationException);
            Assert.That(candidate.ValidateForPublication, Throws.InvalidOperationException);
        }

        /// <summary>
        /// Creates an isolated private candidate with its identity assigned first.
        /// </summary>
        /// <returns>The private candidate.</returns>
        private static PreparedNodeManager CreateCandidate()
        {
            IAsyncNodeManager manager = Mock.Of<IAsyncNodeManager>();
            return new PreparedNodeManager(manager, [], NodeManagerGenerationRegistry.CreateAddition(manager));
        }
    }
}
