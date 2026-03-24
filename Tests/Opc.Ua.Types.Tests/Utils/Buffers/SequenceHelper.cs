/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System.Buffers
{
    public static class SequenceHelper
    {
        /// <summary>
        /// Converts the sequence of memory blocks to <see cref="ReadOnlySequence{T}"/> data type.
        /// </summary>
        /// <param name="chunks">The sequence of memory blocks.</param>
        /// <typeparam name="T">The type of elements in the memory blocks.</typeparam>
        /// <returns>The constructed <see cref="ReadOnlySequence{T}"/> instance containing
        /// memory blocks.</returns>
        public static ReadOnlySequence<T> ToReadOnlySequence<T>(this IEnumerable<ReadOnlyMemory<T>>? chunks)
        {
            Chunk<T>? head = null;
            Chunk<T>? tail = null;

            switch (chunks)
            {
                case null:
                    break;
                case ReadOnlyMemory<T>[] array:
                    CreateChunks(array.AsSpan(), ref head, ref tail);
                    break;
#if NET8_0_OR_GREATER
                case List<ReadOnlyMemory<T>> list:
                    CreateChunks(CollectionsMarshal.AsSpan(list), ref head, ref tail);
                    break;
#endif
                case LinkedList<ReadOnlyMemory<T>> list:
                    FromLinkedList(list, ref head, ref tail);
                    break;
                default:
                    ToReadOnlySequenceSlow(chunks, ref head, ref tail);
                    break;
            }

            return Chunk<T>.CreateSequence(head, tail);

            static void ToReadOnlySequenceSlow(
                IEnumerable<ReadOnlyMemory<T>> chunks,
                ref Chunk<T>? head,
                ref Chunk<T>? tail)
            {
                foreach (ReadOnlyMemory<T> segment in chunks)
                {
                    if (!segment.IsEmpty)
                    {
                        Chunk<T>.AddChunk(segment, ref head, ref tail);
                    }
                }
            }

            static void FromLinkedList(
                LinkedList<ReadOnlyMemory<T>> chunks,
                ref Chunk<T>? head,
                ref Chunk<T>? tail)
            {
                for (LinkedListNode<ReadOnlyMemory<T>>? current = chunks.First;
                    current is not null;
                    current = current.Next)
                {
#if NET8_0_OR_GREATER
                    ref readonly ReadOnlyMemory<T> segment = ref current.ValueRef;
#else
                    ReadOnlyMemory<T> segment = current.Value;
#endif
                    if (!segment.IsEmpty)
                    {
                        Chunk<T>.AddChunk(segment, ref head, ref tail);
                    }
                }
            }
        }

        public static ReadOnlySequence<T> ToReadOnlySequence<T>(
            this ReadOnlyMemory<T> memory, int chunkSize)
        {
            if (chunkSize == 0)
            {
                return new ReadOnlySequence<T>(memory);
            }
            return Split(memory, chunkSize).ToReadOnlySequence();

            static IEnumerable<ReadOnlyMemory<T>> Split(ReadOnlyMemory<T> memory, int chunkSize)
            {
                int startIndex = 0;
                int length = Math.Min(chunkSize, memory.Length);
                do
                {
                    yield return memory.Slice(startIndex, length);
                    startIndex += chunkSize;
                    length = Math.Min(memory.Length - startIndex, chunkSize);
                }
                while (startIndex < memory.Length);
            }
        }

        public static ReadOnlySequence<T> AsMultiSegmentSequence<T>(this T[] buffer, int split = 0)
        {
            if (split == 0)
            {
                split = buffer.Length / 2;
            }
            var sequenceSegment1 = new TestSequenceSegment<T>(buffer.AsSpan()[..split].ToArray());
            var sequenceSegment2 = new TestSequenceSegment<T>(buffer.AsSpan()[split..].ToArray());
            sequenceSegment1.SetNext(sequenceSegment2);
            return new ReadOnlySequence<T>(sequenceSegment1, 0, sequenceSegment2, buffer.Length - split);
        }

        private static void CreateChunks<T>(ReadOnlySpan<ReadOnlyMemory<T>> chunks, ref Chunk<T>? head, ref Chunk<T>? tail)
        {
            foreach (ref readonly ReadOnlyMemory<T> segment in chunks)
            {
                if (!segment.IsEmpty)
                {
                    Chunk<T>.AddChunk(segment, ref head, ref tail);
                }
            }
        }

        private sealed class TestSequenceSegment<T> : ReadOnlySequenceSegment<T>
        {
            public TestSequenceSegment(T[] data)
            {
                Memory = data;
            }

            public void SetNext(TestSequenceSegment<T> next)
            {
                next.RunningIndex = RunningIndex + Memory.Length;
                Next = next;
            }
        }

        public static ReadOnlySequence<T> CreateEmpty<T>(int count)
        {
            var first = new EmptySegment<T>();
            EmptySegment<T> last = first;
            for (int i = 1; i < count; i++)
            {
                last = last.Append();
            }
            return new ReadOnlySequence<T>(first, 0, last, 0);
        }

        internal static ReadOnlySequence<T> FakeSize<T>(int size, int numSegments)
        {
            var first = new FakeSequenceSegment<T>();
            FakeSequenceSegment<T> last = first;
            for (int i = 0; i < numSegments; i++)
            {
                var next = new FakeSequenceSegment<T>(size);
                last.SetNext(next);
                last = next;
            }
            var final = new FakeSequenceSegment<T>();
            last.SetNext(final);
            last = final;
            return new ReadOnlySequence<T>(first, 0, last, 1);
        }

        private sealed class FakeSequenceSegment<T> : ReadOnlySequenceSegment<T>
        {
            private readonly int m_length;

            public FakeSequenceSegment()
            {
                Memory = new ReadOnlyMemory<T>(new T[1]);
                m_length = 1;
            }

            public FakeSequenceSegment(int length)
            {
                m_length = length;
            }

            public void SetNext(FakeSequenceSegment<T> next)
            {
                next.RunningIndex = RunningIndex + m_length;
                Next = next;
            }
        }

        internal sealed class EmptySegment<T> : ReadOnlySequenceSegment<T>
        {
            public EmptySegment<T> Append()
            {
                var segment = new EmptySegment<T>
                {
                    RunningIndex = RunningIndex
                };

                Next = segment;
                return segment;
            }
        }

        internal sealed class Chunk<T> : ReadOnlySequenceSegment<T>
        {
            private Chunk(in ReadOnlyMemory<T> segment)
            {
                Memory = segment;
            }

            private new Chunk<T> Next(in ReadOnlyMemory<T> segment)
            {
                long index = RunningIndex;
                Chunk<T> chunk;
                base.Next = chunk = new(segment)
                {
                    RunningIndex = index + Memory.Length
                };
                return chunk;
            }

            internal static void AddChunk(
                in ReadOnlyMemory<T> segment,
                [AllowNull] ref Chunk<T> first,
                [AllowNull] ref Chunk<T> last)
            {
                Debug.Assert(!segment.IsEmpty);

                last = first is null || last is null
                    ? first = new(segment) { RunningIndex = 0L }
                    : last.Next(segment);
            }

            internal static ReadOnlySequence<T> CreateSequence(Chunk<T>? head, Chunk<T>? tail)
            {
                if (head is null || tail is null)
                {
                    return new();
                }
                return ReferenceEquals(head, tail)
                    ? new(head.Memory)
                    : new(head, 0, tail, tail.Memory.Length);
            }
        }
    }
}
