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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a snapshot created by <see cref="IRaftSnapshotManager.CreateSnapshotAsync"/>.
    /// </summary>
    public sealed class RaftKeyValueSnapshot
    {
        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="lastIncludedCommand">The position of the last committed command the snapshot holds.</param>
        /// <param name="data">The encoded snapshot.</param>
        public RaftKeyValueSnapshot(long lastIncludedCommand, ByteString data)
        {
            LastIncludedCommand = lastIncludedCommand;
            Data = data;
        }

        /// <summary>
        /// The 1-based position, in the committed command stream of the consensus, of the last command whose effect
        /// the snapshot holds: the barrier the snapshot was captured at. Every replica observes the same command
        /// stream, so the position identifies the same prefix of the log on every replica. It counts the commands
        /// delivered on <see cref="IRaftConsensus.Committed"/>, not the entries of the consensus engine's own log.
        /// </summary>
        public long LastIncludedCommand { get; }

        /// <summary>
        /// The encoded snapshot, which also carries <see cref="LastIncludedCommand"/>. Persist it and pass it to
        /// <see cref="IRaftSnapshotManager.RestoreSnapshotAsync"/>. The format is versioned and owned by the store.
        /// </summary>
        public ByteString Data { get; }
    }
}
