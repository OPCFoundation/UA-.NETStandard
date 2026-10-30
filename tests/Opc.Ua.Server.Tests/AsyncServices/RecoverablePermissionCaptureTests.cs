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
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Verifies bounded, exact permission capture rather than cached grants or truncated authority records.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverablePermissionCaptureTests
    {
        /// <summary>
        /// Rejects an invalid budget rather than treating it as an empty successful capture.
        /// </summary>
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NegativeBudgetIsRejected(int budget)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
                () => new RecoverablePermissionCapture(budget))!;
            Assert.That(error.ParamName, Is.EqualTo("byteBudget"));
        }

        /// <summary>
        /// An invocation with no checked targets needs no permission records.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(128)]
        public void EmptyCaptureHasNoInventedPermissions(int budget)
        {
            var capture = new RecoverablePermissionCapture(budget);
            Assert.That(capture.Snapshot().Count, Is.Zero);
        }

        /// <summary>
        /// A duplicate check consumes capacity once and retains the exact namespace-qualified target.
        /// </summary>
        [TestCase(63, false)]
        [TestCase(64, true)]
        [TestCase(65, true)]
        public void NumericTargetUsesExactBudgetAndDeduplicates(int budget, bool fits)
        {
            var capture = new RecoverablePermissionCapture(budget);
            for (int i = 0; i < 10; i++)
            {
                capture.Record(new NodeId(200u, 3), PermissionType.Read);
            }
            if (!fits)
            {
                AssertTooLarge(capture);
                return;
            }
            ArrayOf<RecoverableNodePermission> result = capture.Snapshot();
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].NodeId, Is.EqualTo(new NodeId(200u, 3)));
            Assert.That(result[0].Permission, Is.EqualTo(PermissionType.Read));
        }

        /// <summary>
        /// Identifier bytes are included in the existing message-sized budget.
        /// </summary>
        [TestCase(false, -1)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(true, -1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        public void VariableLengthIdentifiersAreCharged(bool opaque, int offset)
        {
            const string text = "node-\u00b5";
            var bytes = ByteString.From(0, 1, 128, 255);
            NodeId node = opaque ? new NodeId(bytes, 3) : new NodeId(text, 3);
            int size = 64 + (opaque ? bytes.Length : Encoding.UTF8.GetByteCount(text));
            var capture = new RecoverablePermissionCapture(size + offset);
            capture.Record(node, PermissionType.Browse);
            if (offset < 0)
            {
                AssertTooLarge(capture);
                return;
            }
            ArrayOf<RecoverableNodePermission> result = capture.Snapshot();
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(new RecoverableNodePermission(node, PermissionType.Browse)));
        }

        /// <summary>
        /// Separate checks retain their original permission masks, including type-visibility exemptions.
        /// </summary>
        [Test]
        public void DifferentChecksAreNotMergedIntoADifferentPermissionRule()
        {
            var capture = new RecoverablePermissionCapture(128);
            var node = new NodeId(200u, 3);
            capture.Record(node, PermissionType.Read);
            capture.Record(node, PermissionType.Write);
            Assert.That(capture.Snapshot().ToArray(), Is.EquivalentTo(
            [
                new RecoverableNodePermission(node, PermissionType.Read),
                new RecoverableNodePermission(node, PermissionType.Write)
            ]));
        }

        /// <summary>
        /// The namespace is part of the target; equal local numeric IDs do not deduplicate.
        /// </summary>
        [Test]
        public void NamespaceQualifiedTargetsRemainDistinct()
        {
            var capture = new RecoverablePermissionCapture(128);
            capture.Record(new NodeId(1u, 1), PermissionType.Browse);
            capture.Record(new NodeId(1u, 2), PermissionType.Browse);
            Assert.That(capture.Snapshot().ToArray(), Is.EquivalentTo(
            [
                new RecoverableNodePermission(new NodeId(1u, 1), PermissionType.Browse),
                new RecoverableNodePermission(new NodeId(1u, 2), PermissionType.Browse)
            ]));
        }

        /// <summary>
        /// Overflow rejects the entire authority snapshot, not just the final target.
        /// </summary>
        [Test]
        public void OverflowCannotReturnAPartialPermissionList()
        {
            var capture = new RecoverablePermissionCapture(127);
            capture.Record(new NodeId(1u, 1), PermissionType.Read);
            capture.Record(new NodeId(2u, 1), PermissionType.Write);
            AssertTooLarge(capture);
            AssertTooLarge(capture);
        }

        /// <summary>
        /// Ownership transfers once; later mutations cannot change the retained immutable checks.
        /// </summary>
        [Test]
        public void TransferredCaptureRejectsLateChanges()
        {
            var capture = new RecoverablePermissionCapture(128);
            capture.Record(new NodeId(1u, 1), PermissionType.Read);
            ArrayOf<RecoverableNodePermission> frozen = capture.Snapshot();
            _ = Assert.Throws<InvalidOperationException>(
                () => capture.Record(new NodeId(2u, 1), PermissionType.Write));
            _ = Assert.Throws<InvalidOperationException>(() => capture.Snapshot());
            Assert.That(frozen.Count, Is.EqualTo(1));
            Assert.That(frozen[0], Is.EqualTo(
                new RecoverableNodePermission(new NodeId(1u, 1), PermissionType.Read)));
        }

        /// <summary>
        /// A null target does not create a fictitious authority check or consume capacity.
        /// </summary>
        [Test]
        public void NullTargetDoesNotConsumeCapacity()
        {
            var capture = new RecoverablePermissionCapture(64);
            capture.Record(NodeId.Null, PermissionType.Read);
            capture.Record(new NodeId(1u), PermissionType.Read);
            Assert.That(capture.Snapshot().ToArray(), Is.EqualTo(
            [
                new RecoverableNodePermission(new NodeId(1u), PermissionType.Read)
            ]));
        }

        private static void AssertTooLarge(RecoverablePermissionCapture capture)
        {
            ServiceResultException error = Assert.Throws<ServiceResultException>(() => capture.Snapshot())!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
        }
    }
}
