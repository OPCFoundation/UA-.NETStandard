// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Reuses short strings within one decode without allocating a temporary string on a hit.
    /// </summary>
    internal sealed class DependencyBinaryReader : BinaryReader
    {
        /// <summary>
        /// Creates a reader over the seekable inflated payload.
        /// </summary>
        internal DependencyBinaryReader(Stream source, CancellationToken cancellationToken)
            : base(source, Encoding.UTF8, leaveOpen: true)
        {
            m_cancellationToken = cancellationToken;
        }

        /// <inheritdoc/>
        public override string ReadString()
        {
            m_cancellationToken.ThrowIfCancellationRequested();
            long start = BaseStream.Position;
            int length = Read7BitEncodedInt();
            if (length == 0)
            {
                return string.Empty;
            }
            if (length < 0 || length > m_bytes.Length)
            {
                BaseStream.Position = start;
                return base.ReadString();
            }
            int offset = 0;
            while (offset < length)
            {
                int count = Read(m_bytes, offset, length - offset);
                if (count == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += count;
            }
            int charCount = Encoding.UTF8.GetChars(m_bytes, 0, length, m_chars, 0);
            int hash = 17;
            for (int index = 0; index < charCount; index++)
            {
                hash = unchecked((hash * 31) + m_chars[index]);
            }
            bool foundBucket = m_strings.TryGetValue(hash, out List<string>? bucket);
            if (foundBucket)
            {
                foreach (string candidate in bucket!)
                {
                    if (Matches(candidate, charCount))
                    {
                        return candidate;
                    }
                }
            }
            string value = new(m_chars, 0, charCount);
            if (m_stringCount < kStringCapacity)
            {
                if (!foundBucket)
                {
                    bucket = [];
                    m_strings.Add(hash, bucket);
                }
                bucket!.Add(value);
                m_stringCount++;
            }
            return value;
        }

        /// <summary>
        /// Creates a reader for a trailer with the same cancellation scope.
        /// </summary>
        internal DependencyBinaryReader CreateNested(Stream source)
        {
            return new DependencyBinaryReader(source, m_cancellationToken);
        }

        /// <summary>
        /// Confirms equality after the hash lookup, including hash collisions.
        /// </summary>
        private bool Matches(string candidate, int charCount)
        {
            if (candidate.Length != charCount)
            {
                return false;
            }
            for (int index = 0; index < charCount; index++)
            {
                if (candidate[index] != m_chars[index])
                {
                    return false;
                }
            }
            return true;
        }

        private const int kStringCapacity = 4096;
        private readonly CancellationToken m_cancellationToken;
        private readonly byte[] m_bytes = new byte[1024];
        private readonly char[] m_chars = new char[1024];
        private readonly Dictionary<int, List<string>> m_strings = [];
        private int m_stringCount;
    }
}
