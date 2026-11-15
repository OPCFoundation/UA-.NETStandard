/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Buffers;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises cleanup-recovery participant snapshots without starting a server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerCleanupRecoveryTests
    {
        /// <summary>
        /// Participant-owned arrays cannot change a snapshot after the host validates it.
        /// </summary>
        [Test]
        public void ParticipantSnapshotDoesNotAliasProviderStorage()
        {
            var first = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "Resource", null, StatusCodes.BadUnexpectedError);
            var replacement = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "OtherResource", null, StatusCodes.BadUnexpectedError);
            NodeManagerCleanupFailure[] storage = [first];
            var participant = new Mock<INodeManagerCleanupParticipant>(MockBehavior.Strict);
            NodeManagerCleanupAccess? access = null;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess capability) =>
                {
                    access = capability;
                    Assert.That(capability.IsAuthorizedFor(participant.Object), Is.True);
                    return new ArrayOf<NodeManagerCleanupFailure>(storage);
                });

            ArrayOf<NodeManagerCleanupFailure> snapshot =
                NodeManagerCleanupRecovery.CaptureFailures(participant.Object);
            storage[0] = replacement;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(snapshot.IsNull, Is.False);
                Assert.That(snapshot.Count, Is.EqualTo(1));
                Assert.That(snapshot[0], Is.SameAs(first));
                Assert.That(snapshot[0].Id, Is.EqualTo(first.Id));
                Assert.That(access, Is.Not.Null);
                Assert.That(access!.IsAuthorizedFor(participant.Object), Is.False);
            }
        }

        /// <summary>
        /// A null snapshot is rejected while a non-null empty snapshot is preserved.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ParticipantSnapshotDistinguishesNullAndEmpty(bool isNull)
        {
            var participant = new Mock<INodeManagerCleanupParticipant>(MockBehavior.Strict);
            NodeManagerCleanupAccess? access = null;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess capability) =>
                {
                    access = capability;
                    return isNull
                        ? ArrayOf<NodeManagerCleanupFailure>.Null
                        : ArrayOf<NodeManagerCleanupFailure>.Empty;
                });

            if (isNull)
            {
                Assert.That(
                    () => NodeManagerCleanupRecovery.CaptureFailures(participant.Object),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "The cleanup participant returned a null failure snapshot."));
            }
            else
            {
                ArrayOf<NodeManagerCleanupFailure> snapshot =
                    NodeManagerCleanupRecovery.CaptureFailures(participant.Object);
                Assert.That(snapshot.IsNull, Is.False);
                Assert.That(snapshot.IsEmpty, Is.True);
            }
            Assert.That(access, Is.Not.Null);
            Assert.That(access!.IsAuthorizedFor(participant.Object), Is.False);
        }

        /// <summary>
        /// Null entries and duplicate failure identifiers cannot enter the detached snapshot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ParticipantSnapshotRejectsInvalidEntries(bool nullEntry)
        {
            Guid id = Guid.NewGuid();
            var first = new NodeManagerCleanupFailure(id, "Resource", null, StatusCodes.BadUnexpectedError);
            NodeManagerCleanupFailure[] storage =
            [
                first,
                nullEntry ? null! : new NodeManagerCleanupFailure(
                    id, "OtherResource", null, StatusCodes.BadUnexpectedError)
            ];
            var participant = new Mock<INodeManagerCleanupParticipant>(MockBehavior.Strict);
            NodeManagerCleanupAccess? access = null;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess capability) =>
                {
                    access = capability;
                    return new ArrayOf<NodeManagerCleanupFailure>(storage);
                });

            Assert.That(
                () => NodeManagerCleanupRecovery.CaptureFailures(participant.Object),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The cleanup participant returned invalid failure IDs."));
            Assert.That(access, Is.Not.Null);
            Assert.That(access!.IsAuthorizedFor(participant.Object), Is.False);
        }

        /// <summary>
        /// A provider error remains observable and the discovery capability expires on failure.
        /// </summary>
        [Test]
        public void ParticipantSnapshotFailurePreservesExceptionAndRevokesAccess()
        {
            var failure = new InvalidOperationException("The participant snapshot failed.");
            var participant = new Mock<INodeManagerCleanupParticipant>(MockBehavior.Strict);
            NodeManagerCleanupAccess? access = null;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess capability) =>
                {
                    access = capability;
                    throw failure;
                });

            Assert.That(
                () => NodeManagerCleanupRecovery.CaptureFailures(participant.Object),
                Throws.Exception.SameAs(failure));
            Assert.That(access, Is.Not.Null);
            Assert.That(access!.IsAuthorizedFor(participant.Object), Is.False);
        }

        /// <summary>
        /// Discovery authority expires before the host reads participant-owned snapshot storage.
        /// </summary>
        [Test]
        public void ParticipantSnapshotRevokesAccessBeforeReadingStorage()
        {
            var failure = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "Resource", null, StatusCodes.BadUnexpectedError);
            var participant = new Mock<INodeManagerCleanupParticipant>(MockBehavior.Strict);
            NodeManagerCleanupAccess? access = null;
            using var storage = new CapabilityInspectingMemoryManager(
                failure, () => access!.IsAuthorizedFor(participant.Object));
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess capability) =>
                {
                    access = capability;
                    Assert.That(capability.IsAuthorizedFor(participant.Object), Is.True);
                    return new ArrayOf<NodeManagerCleanupFailure>(storage.Snapshot);
                });

            ArrayOf<NodeManagerCleanupFailure> snapshot =
                NodeManagerCleanupRecovery.CaptureFailures(participant.Object);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(storage.AuthorizedDuringRead, Is.False);
                Assert.That(snapshot.Count, Is.EqualTo(1));
                Assert.That(snapshot[0], Is.SameAs(failure));
                Assert.That(access!.IsAuthorizedFor(participant.Object), Is.False);
            }
        }

        /// <summary>
        /// Revoking a confirmation preserves whether it was consumed, without allowing later use.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConfirmationCapabilityRevocationDistinguishesConsumption(bool consumed)
        {
            INodeManagerCleanupParticipant participant = Mock.Of<INodeManagerCleanupParticipant>();
            INodeManagerCleanupParticipant other = Mock.Of<INodeManagerCleanupParticipant>();
            Guid id = Guid.NewGuid();
            var capability = new NodeManagerCleanupConfirmation(participant, id);

            Assert.That(capability.TryConsume(other, out Guid wrongId), Is.False);
            Assert.That(wrongId, Is.EqualTo(Guid.Empty));
            if (consumed)
            {
                Assert.That(capability.TryConsume(participant, out Guid acceptedId), Is.True);
                Assert.That(acceptedId, Is.EqualTo(id));
            }
            capability.Revoke();
            capability.Revoke();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capability.IsConsumed, Is.EqualTo(consumed));
                Assert.That(capability.TryConsume(participant, out Guid replayId), Is.False);
                Assert.That(replayId, Is.EqualTo(Guid.Empty));
            }
        }

        /// <summary>
        /// Verification uses the same one-use and revocation boundary as confirmation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void VerificationCapabilityRevocationDistinguishesConsumption(bool consumed)
        {
            INodeManagerCleanupParticipant participant = Mock.Of<INodeManagerCleanupParticipant>();
            INodeManagerCleanupParticipant other = Mock.Of<INodeManagerCleanupParticipant>();
            Guid id = Guid.NewGuid();
            var capability = new NodeManagerCleanupVerification(participant, id);

            Assert.That(capability.TryConsume(other, out Guid wrongId), Is.False);
            Assert.That(wrongId, Is.EqualTo(Guid.Empty));
            if (consumed)
            {
                Assert.That(capability.TryConsume(participant, out Guid acceptedId), Is.True);
                Assert.That(acceptedId, Is.EqualTo(id));
            }
            capability.Revoke();
            capability.Revoke();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capability.IsConsumed, Is.EqualTo(consumed));
                Assert.That(capability.TryConsume(participant, out Guid replayId), Is.False);
                Assert.That(replayId, Is.EqualTo(Guid.Empty));
            }
        }

        /// <summary>
        /// Only known kinds and numeric status codes are exposed from participant metadata.
        /// </summary>
        [TestCase("DirectoryCursor", "DirectoryCursor")]
        [TestCase("CompanionResource", "CompanionResource")]
        [TestCase("RetiredConsumer", "RetiredConsumer")]
        [TestCase("C:\\private\\resource", "Resource")]
        public void FailureDescriptionRemovesUntrustedMetadata(string suppliedKind, string expectedKind)
        {
            Guid id = Guid.NewGuid();
            var suppliedStatus = new StatusCode(
                StatusCodes.BadUnexpectedError.Code | 1u, "untrusted status text");
            var supplied = new NodeManagerCleanupFailure(
                id, suppliedKind, "C:\\private\\name", suppliedStatus);

            NodeManagerCleanupFailure sanitized =
                NodeManagerCleanupRecovery.SanitizeFailureDescription(supplied);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sanitized, Is.Not.SameAs(supplied));
                Assert.That(sanitized.Id, Is.EqualTo(id));
                Assert.That(sanitized.ResourceKind, Is.EqualTo(expectedKind));
                Assert.That(sanitized.ResourceName, Is.Null);
                Assert.That(sanitized.StatusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
                Assert.That(sanitized.StatusCode.SymbolicId, Is.Empty);
                Assert.That(supplied.ResourceName, Is.EqualTo("C:\\private\\name"));
                Assert.That(supplied.StatusCode.SymbolicId, Is.EqualTo("untrusted status text"));
            }
        }

        /// <summary>
        /// Observes discovery authority when the host reads the returned memory.
        /// </summary>
        private sealed class CapabilityInspectingMemoryManager(
            NodeManagerCleanupFailure failure,
            Func<bool> isAuthorized) : MemoryManager<NodeManagerCleanupFailure>
        {
            /// <summary>
            /// Gets a memory view without reading its contents.
            /// </summary>
            internal ReadOnlyMemory<NodeManagerCleanupFailure> Snapshot => CreateMemory(m_values.Length);

            /// <summary>
            /// Gets whether the capability was active when the contents were read.
            /// </summary>
            internal bool? AuthorizedDuringRead { get; private set; }

            /// <inheritdoc/>
            public override Span<NodeManagerCleanupFailure> GetSpan()
            {
                AuthorizedDuringRead = isAuthorized();
                return m_values;
            }

            /// <inheritdoc/>
            public override MemoryHandle Pin(int elementIndex = 0)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Unpin()
            {
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
            }

            private readonly NodeManagerCleanupFailure[] m_values = [failure];
        }
    }
}
