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

#if NET10_0
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpRuntime
{
    /// <summary>
    /// Pins direct MemoryStream-compatible behavior and atomic budget refusals for every supported write path.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpBoundedMemoryStreamTests
    {
        /// <summary>
        /// Invalid long limits are rejected before any caller-sized buffer can be allocated.
        /// </summary>
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase((long)int.MaxValue + 1)]
        [TestCase(long.MaxValue)]
        public void ConstructorRejectsInvalidLimits(long limit)
        {
            Assert.That(() => new McpBoundedMemoryStream(limit),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("limit"));
        }

        /// <summary>
        /// Zero and both valid integer boundaries remain lazy, usable streams rather than eager allocations.
        /// </summary>
        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase((long)int.MaxValue)]
        public void ValidLimitsStartWithoutAllocation(long limit)
        {
            using var buffer = new McpBoundedMemoryStream(limit);
            Assert.That(buffer.Length, Is.Zero);
            Assert.That(buffer.Position, Is.Zero);
            Assert.That(buffer.Capacity, Is.Zero);
            Assert.That(buffer.ToArray(), Is.Empty);
            Assert.That(buffer.CanRead, Is.True);
            Assert.That(buffer.CanWrite, Is.True);
            Assert.That(buffer.CanSeek, Is.True);
        }

        /// <summary>
        /// A large configured ceiling is not permission to allocate that entire ceiling for one byte.
        /// </summary>
        [Test]
        public void SmallWriteUnderMaximumLimitAllocatesOnlyBoundedCapacity()
        {
            using var buffer = new McpBoundedMemoryStream(int.MaxValue);
            buffer.WriteByte(0xAB);
            AssertState(buffer, [0xAB], 1, 8192);
            Assert.That(buffer.Capacity, Is.InRange(1, 8192));
        }

        /// <summary>
        /// Geometric growth is clamped when doubling the current allocation would exceed the exact budget.
        /// </summary>
        [Test]
        public void CapacityGrowthIsClampedWhenDoublingWouldExceedLimit()
        {
            using var buffer = new McpBoundedMemoryStream(8193);
            buffer.SetLength(8192);
            Assert.That(buffer.Capacity, Is.EqualTo(8192));
            buffer.Position = 8191;
            buffer.WriteByte(0xA5);
            buffer.WriteByte(0xB6);
            byte[] expected = new byte[8193];
            expected[8191] = 0xA5;
            expected[8192] = 0xB6;
            AssertState(buffer, expected, 8193, 8193);
            Assert.That(buffer.Capacity, Is.EqualTo(8193));
            Assert.That(() => buffer.WriteByte(0xC7), Throws.TypeOf<IOException>());
            AssertState(buffer, expected, 8193, 8193);
            Assert.That(buffer.Capacity, Is.EqualTo(8193));
        }

        /// <summary>
        /// Every public write route can fill the exact budget and refuses the next byte without changing state.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task EveryWriteShapeAcceptsExactLimitAndRejectsAdjacentOverflowAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            await WriteAsync(buffer, "Array", [11, 22, 33]).ConfigureAwait(false);
            await WriteAsync(buffer, shape, [44]).ConfigureAwait(false);
            AssertState(buffer, [11, 22, 33, 44], 4, 4);
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [55])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [11, 22, 33, 44], 4, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// A zero-byte budget accepts no nonempty write, including the one-byte overload.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task ZeroBudgetRefusesNonEmptyWritesWithoutAllocationAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(0);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [1])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [], 0, 0);
            Assert.That(buffer.GetBuffer(), Is.Empty);
        }

        /// <summary>
        /// Bulk refusal reserves the whole write before overwriting even the otherwise valid prefix.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task BulkRefusalPreservesBytesPositionLengthAndCapacityAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(5);
            await WriteAsync(buffer, "Array", [1, 2, 3, 4]).ConfigureAwait(false);
            buffer.Position = 2;
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [9, 8, 7, 6])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [1, 2, 3, 4], 2, 5);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));

            await WriteAsync(buffer, shape, [9, 8, 7]).ConfigureAwait(false);
            AssertState(buffer, [1, 2, 9, 8, 7], 5, 5);
        }

        /// <summary>
        /// Direct capacity changes permit exact boundaries and reject negative, below-length, and oversize values.
        /// </summary>
        [TestCase(-1)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(int.MaxValue)]
        public void CapacitySetterEnforcesCeilingAndLengthFloor(int refusedCapacity)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            buffer.Write([1, 2], 0, 2);
            buffer.Position = 1;
            buffer.Capacity = 2;
            Assert.That(buffer.Capacity, Is.EqualTo(2));
            AssertState(buffer, [1, 2], 1, 2);
            buffer.Capacity = 3;
            Assert.That(buffer.Capacity, Is.EqualTo(3));
            AssertState(buffer, [1, 2], 1, 3);
            buffer.Capacity = 4;
            Assert.That(buffer.Capacity, Is.EqualTo(4));
            Assert.That(() => buffer.Capacity = refusedCapacity, Throws.TypeOf<ArgumentOutOfRangeException>());
            AssertState(buffer, [1, 2], 1, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(4));
        }

        /// <summary>
        /// Reserving or releasing empty capacity does not create bytes or move the logical position.
        /// </summary>
        [Test]
        public void CapacityPreallocationDoesNotChangeLogicalContents()
        {
            using var buffer = new McpBoundedMemoryStream(16);
            buffer.Capacity = 16;
            Assert.That(buffer.Capacity, Is.EqualTo(16));
            AssertState(buffer, [], 0, 16);
            buffer.Capacity = 0;
            AssertState(buffer, [], 0, 0);
            buffer.Capacity = 8;
            buffer.Write([2, 4, 6], 0, 3);
            Assert.That(buffer.Capacity, Is.EqualTo(8));
            AssertState(buffer, [2, 4, 6], 3, 8);
        }

        /// <summary>
        /// Empty writes are valid at both the zero budget and the exact end of a filled budget.
        /// </summary>
        [TestCase("Array", 0)]
        [TestCase("Span", 0)]
        [TestCase("ArrayAsync", 0)]
        [TestCase("MemoryAsync", 0)]
        [TestCase("Array", 4)]
        [TestCase("Span", 4)]
        [TestCase("ArrayAsync", 4)]
        [TestCase("MemoryAsync", 4)]
        public async Task EmptyWritesAtZeroAndAtLimitDoNotGrowAsync(string shape, int limit)
        {
            using var buffer = new McpBoundedMemoryStream(limit);
            byte[] expected = limit == 0 ? [] : [1, 2, 3, 4];
            await WriteAsync(buffer, "Array", expected).ConfigureAwait(false);
            int capacity = buffer.Capacity;
            await WriteAsync(buffer, shape, []).ConfigureAwait(false);
            AssertState(buffer, expected, limit, limit);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// Seeking beyond the ceiling cannot bypass the budget with a zero-count write.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task EmptyWritesBeyondBudgetAreRefusedAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            buffer.WriteByte(7);
            buffer.Position = 5;
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [7], 5, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// MemoryStream's zero-count write at an in-budget seeked position still materializes its zero-filled gap.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task EmptyWriteAtSeekedPositionZeroFillsGapAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            buffer.WriteByte(7);
            buffer.Position = 3;
            await WriteAsync(buffer, shape, []).ConfigureAwait(false);
            AssertState(buffer, [7, 0, 0], 3, 4);
        }

        /// <summary>
        /// Overwrites can exceed the budget cumulatively while logical growth beyond the budget is still refused.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task SeekAndOverwriteUsePositionRatherThanCumulativeBytesAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(5);
            await WriteAsync(buffer, "Array", [1, 2, 3, 4, 5]).ConfigureAwait(false);
            Assert.That(buffer.Seek(-3, SeekOrigin.End), Is.EqualTo(2));
            await WriteAsync(buffer, shape, [9]).ConfigureAwait(false);
            AssertState(buffer, [1, 2, 9, 4, 5], 3, 5);
            Assert.That(buffer.Seek(1, SeekOrigin.Current), Is.EqualTo(4));
            await WriteAsync(buffer, shape, [7]).ConfigureAwait(false);
            AssertState(buffer, [1, 2, 9, 4, 7], 5, 5);
            Assert.That(buffer.Seek(1, SeekOrigin.End), Is.EqualTo(6));
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [8])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [1, 2, 9, 4, 7], 6, 5);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// Writing at a seeked position includes the zero-filled gap in the exact byte budget.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task WriteAfterSeekZeroFillsGapAtExactLimitAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(5);
            buffer.WriteByte(1);
            Assert.That(buffer.Seek(4, SeekOrigin.Begin), Is.EqualTo(4));
            await WriteAsync(buffer, shape, [9]).ConfigureAwait(false);
            AssertState(buffer, [1, 0, 0, 0, 9], 5, 5);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [6])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [1, 0, 0, 0, 9], 5, 5);
        }

        /// <summary>
        /// Length growth zero-fills, truncation clamps position, and later growth cannot resurrect truncated bytes.
        /// </summary>
        [Test]
        public void SetLengthTruncatesAndRegrowsWithoutResurrectingBytes()
        {
            using var buffer = new McpBoundedMemoryStream(6);
            buffer.Write([1, 2, 3, 4], 0, 4);
            buffer.SetLength(6);
            AssertState(buffer, [1, 2, 3, 4, 0, 0], 4, 6);
            int capacity = buffer.Capacity;
            buffer.Position = 5;
            buffer.SetLength(2);
            AssertState(buffer, [1, 2], 2, 6);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
            buffer.SetLength(6);
            AssertState(buffer, [1, 2, 0, 0, 0, 0], 2, 6);
            buffer.SetLength(0);
            AssertState(buffer, [], 0, 6);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
            buffer.Capacity = 0;
            AssertState(buffer, [], 0, 0);
        }

        /// <summary>
        /// Invalid length values are rejected without changing data, logical state, or physical allocation.
        /// </summary>
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase(5L)]
        [TestCase((long)int.MaxValue)]
        [TestCase(long.MaxValue)]
        public void SetLengthRejectsInvalidValuesBeforeAllocation(long length)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            buffer.Write([2, 4, 6], 0, 3);
            buffer.Position = 1;
            int capacity = buffer.Capacity;
            if (length < 0)
            {
                Assert.That(() => buffer.SetLength(length), Throws.TypeOf<ArgumentOutOfRangeException>());
            }
            else
            {
                Assert.That(() => buffer.SetLength(length), Throws.TypeOf<IOException>());
            }
            AssertState(buffer, [2, 4, 6], 1, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// Disposal blocks every write route and resizing, even when the requested operation is empty.
        /// </summary>
        [TestCase("Array", false)]
        [TestCase("Span", false)]
        [TestCase("Byte", false)]
        [TestCase("ArrayAsync", false)]
        [TestCase("MemoryAsync", false)]
        [TestCase("SetLength", false)]
        [TestCase("Capacity", false)]
        [TestCase("Array", true)]
        [TestCase("Span", true)]
        [TestCase("ArrayAsync", true)]
        [TestCase("MemoryAsync", true)]
        [TestCase("SetLength", true)]
        [TestCase("Capacity", true)]
        public async Task DisposedBufferRejectsWritesAndResizingAsync(string operation, bool empty)
        {
            using var buffer = new McpBoundedMemoryStream(8);
            await WriteAsync(buffer, "Array", [1, 2, 3]).ConfigureAwait(false);
            byte[] backing = buffer.GetBuffer();
            // Exercise synchronous disposal independently of the asynchronous mutation being checked.
            Action dispose = buffer.Dispose;
            dispose();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                MutateAsync(buffer, operation, empty)).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(buffer.GetBuffer(), Is.SameAs(backing));
            Assert.That(buffer.CanWrite, Is.False);
        }

        /// <summary>
        /// The constructor token protects synchronous, asynchronous, length, and direct-capacity mutations.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        [TestCase("SetLength")]
        [TestCase("Capacity")]
        public async Task ConstructorCancellationRefusesEveryMutationWithoutChangingBytesAsync(string operation)
        {
            using var cancellation = new CancellationTokenSource();
            using var buffer = new McpBoundedMemoryStream(8, cancellation.Token);
            await WriteAsync(buffer, "Array", [1, 2, 3]).ConfigureAwait(false);
            buffer.Position = 1;
            int capacity = buffer.Capacity;
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                MutateAsync(buffer, operation, false)).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            AssertState(buffer, [1, 2, 3], 1, 8);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// A token cancelled before construction still prevents otherwise no-op empty mutations.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        [TestCase("SetLength")]
        [TestCase("Capacity")]
        public async Task PreCancelledEmptyOperationsStayAllocationFreeAsync(string operation)
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            using var buffer = new McpBoundedMemoryStream(0, cancellation.Token);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                MutateAsync(buffer, operation, true)).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            AssertState(buffer, [], 0, 0);
        }

        /// <summary>
        /// A per-write token cancels only that asynchronous write; a subsequent uncancelled write remains usable.
        /// </summary>
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task AsyncCallerCancellationIsIndependentOfConstructorTokenAsync(string shape)
        {
            using var lifetime = new CancellationTokenSource();
            using var cancellation = new CancellationTokenSource();
            using var buffer = new McpBoundedMemoryStream(4, lifetime.Token);
            await WriteAsync(buffer, "Array", [1, 2, 3]).ConfigureAwait(false);
            buffer.Position = 1;
            int capacity = buffer.Capacity;
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [9], cancellationToken: cancellation.Token)).ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            AssertState(buffer, [1, 2, 3], 1, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
            Assert.That(lifetime.IsCancellationRequested, Is.False);
            await WriteAsync(buffer, shape, [9]).ConfigureAwait(false);
            AssertState(buffer, [1, 9, 3], 2, 4);
        }

        /// <summary>
        /// Array overloads use the requested slice rather than copying sentinel prefix or suffix bytes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ArrayWritesHonorOffsetAndCountAsync(bool asynchronous)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            await WriteAsync(buffer, "Array", [1, 2, 3, 4]).ConfigureAwait(false);
            buffer.Position = 1;
            await WriteAsync(buffer, asynchronous ? "ArrayAsync" : "Array", [90, 7, 8, 91], 1, 2)
                .ConfigureAwait(false);
            AssertState(buffer, [1, 7, 8, 4], 3, 4);
        }

        /// <summary>
        /// Invalid source slices fail before reserving space or overwriting existing contents.
        /// </summary>
        [TestCase(false, -1, 1)]
        [TestCase(true, -1, 1)]
        [TestCase(false, 0, -1)]
        [TestCase(true, 0, -1)]
        [TestCase(false, 4, 0)]
        [TestCase(true, 4, 0)]
        [TestCase(false, 3, 1)]
        [TestCase(true, 3, 1)]
        [TestCase(false, int.MaxValue, 0)]
        [TestCase(true, int.MaxValue, 0)]
        [TestCase(false, 1, int.MaxValue)]
        [TestCase(true, 1, int.MaxValue)]
        public async Task InvalidArraySlicesDoNotMutateAsync(bool asynchronous, int offset, int count)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            await WriteAsync(buffer, "Array", [1, 2, 3]).ConfigureAwait(false);
            buffer.Position = 1;
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, asynchronous ? "ArrayAsync" : "Array", [9, 8, 7], offset, count))
                .ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<ArgumentException>());
            AssertState(buffer, [1, 2, 3], 1, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// A null array is an argument refusal, not a partial write or accidental null dereference.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NullArrayWritesDoNotMutateAsync(bool asynchronous)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            await WriteAsync(buffer, "Array", [1, 2, 3]).ConfigureAwait(false);
            buffer.Position = 1;
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(WriteNullAsync()).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("buffer"));
            AssertState(buffer, [1, 2, 3], 1, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));

            async Task WriteNullAsync()
            {
                if (asynchronous)
                {
                    // Bind the array overload explicitly: memory cannot represent a null array argument.
                    Func<byte[], int, int, CancellationToken, Task> writeArray = buffer.WriteAsync;
                    await writeArray(null!, 0, 1, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    Action<byte[], int, int> writeArray = buffer.Write;
                    writeArray(null!, 0, 1);
                }
            }
        }

        /// <summary>
        /// Overlapping source storage is copied correctly both in place and across a backing-buffer growth.
        /// </summary>
        [TestCase("Array", false)]
        [TestCase("Span", false)]
        [TestCase("ArrayAsync", false)]
        [TestCase("MemoryAsync", false)]
        [TestCase("Array", true)]
        [TestCase("Span", true)]
        [TestCase("ArrayAsync", true)]
        [TestCase("MemoryAsync", true)]
        public async Task OverlappingWritesPreserveMemoryStreamCopySemanticsAsync(string shape, bool grows)
        {
            using var buffer = new McpBoundedMemoryStream(8);
            await WriteAsync(buffer, "Array", [1, 2, 3, 4]).ConfigureAwait(false);
            buffer.Capacity = 4;
            byte[] source = buffer.GetBuffer();
            buffer.Position = grows ? 3 : 1;
            await WriteAsync(buffer, shape, source, 0, 3).ConfigureAwait(false);
            if (grows)
            {
                AssertState(buffer, [1, 2, 3, 1, 2, 3], 6, 8);
                Assert.That(buffer.GetBuffer(), Is.Not.SameAs(source));
            }
            else
            {
                AssertState(buffer, [1, 1, 2, 3], 4, 4);
                Assert.That(buffer.GetBuffer(), Is.SameAs(source));
            }
        }

        /// <summary>
        /// Source bytes staged in exposed capacity must be consumed before extension clears that same storage.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task WritesFromExposedCapacityPreserveSourceBytesWhenExtendingLengthAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(8);
            buffer.Capacity = 8;
            await WriteAsync(buffer, "Array", [1, 2]).ConfigureAwait(false);
            byte[] source = buffer.GetBuffer();
            source[2] = 7;
            source[3] = 8;
            await WriteAsync(buffer, shape, source, 2, 2).ConfigureAwait(false);
            AssertState(buffer, [1, 2, 7, 8], 4, 8);
            Assert.That(buffer.Capacity, Is.EqualTo(8));
            Assert.That(buffer.GetBuffer(), Is.SameAs(source));
        }

        /// <summary>
        /// The maximum seekable position plus a write must not overflow into an in-budget allocation request.
        /// </summary>
        [TestCase("Array")]
        [TestCase("Span")]
        [TestCase("Byte")]
        [TestCase("ArrayAsync")]
        [TestCase("MemoryAsync")]
        public async Task HugeSeekCannotBypassWriteBudgetAsync(string shape)
        {
            using var buffer = new McpBoundedMemoryStream(4);
            await WriteAsync(buffer, "Array", [2, 4]).ConfigureAwait(false);
            buffer.Position = int.MaxValue;
            int capacity = buffer.Capacity;
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                WriteAsync(buffer, shape, [6])).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<IOException>());
            AssertState(buffer, [2, 4], int.MaxValue, 4);
            Assert.That(buffer.Capacity, Is.EqualTo(capacity));
        }

        /// <summary>
        /// Asserts fixed expected bytes together with all publicly readable logical and allocation bounds.
        /// </summary>
        private static void AssertState(
            McpBoundedMemoryStream buffer, byte[] expected, long position, int capacityLimit)
        {
            Assert.That(buffer.ToArray(), Is.EqualTo(expected));
            Assert.That(buffer.Length, Is.EqualTo(expected.Length));
            Assert.That(buffer.Position, Is.EqualTo(position));
            Assert.That(buffer.Capacity, Is.InRange(expected.Length, capacityLimit));
        }

        /// <summary>
        /// Calls each exact public write overload; empty-byte cases intentionally exclude WriteByte.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static async Task WriteAsync(
            McpBoundedMemoryStream buffer,
            string shape,
            byte[] bytes,
            int offset = 0,
            int? count = null,
            CancellationToken cancellationToken = default)
        {
            int length = count ?? (bytes.Length - offset);
            switch (shape)
            {
                case "Array":
                    Action<byte[], int, int> writeSynchronousArray = buffer.Write;
                    writeSynchronousArray(bytes, offset, length);
                    break;
                case "Span":
                    buffer.Write(bytes.AsSpan(offset, length));
                    break;
                case "Byte":
                    buffer.WriteByte(bytes[offset]);
                    break;
                case "ArrayAsync":
                    // This case must exercise the array overload independently of the memory overload.
                    Func<byte[], int, int, CancellationToken, Task> writeArray = buffer.WriteAsync;
                    await writeArray(bytes, offset, length, cancellationToken).ConfigureAwait(false);
                    break;
                case "MemoryAsync":
                    await buffer.WriteAsync(bytes.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        /// <summary>
        /// Uses otherwise valid resize arguments so cancellation and disposal cannot be confused with budget refusal.
        /// </summary>
        private static async Task MutateAsync(McpBoundedMemoryStream buffer, string operation, bool empty)
        {
            if (operation == "SetLength")
            {
                buffer.SetLength(empty ? 0 : 6);
            }
            else if (operation == "Capacity")
            {
                buffer.Capacity = empty ? 0 : 7;
            }
            else
            {
                await WriteAsync(buffer, operation, empty ? [] : [9]).ConfigureAwait(false);
            }
        }
    }
}
#endif
