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

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the kind of mutation reported by a <see cref="KeyValueChange"/>.
    /// </summary>
    public enum KeyValueChangeKind
    {
        /// <summary>
        /// The key was created or updated to a new value.
        /// </summary>
        Set,

        /// <summary>
        /// The key was removed.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a single change observed on an <see cref="ISharedKeyValueStore"/>
    /// change-feed (<see cref="ISharedKeyValueStore.WatchAsync"/>).
    /// </summary>
    public sealed record KeyValueChange
    {
        /// <summary>
        /// The kind of mutation.
        /// </summary>
        public KeyValueChangeKind Kind { get; init; }

        /// <summary>
        /// The affected key.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// The new value for <see cref="KeyValueChangeKind.Set"/> changes;
        /// a null <see cref="ByteString"/> for deletions.
        /// </summary>
        public ByteString Value { get; init; }
    }
}
