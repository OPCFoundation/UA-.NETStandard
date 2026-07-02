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

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Event payload raised by an <see cref="IDataSetMetaDataRegistry"/>
    /// whenever a metadata description is added, replaced, or refreshed
    /// for a given <see cref="DataSetMetaDataKey"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
    /// Part 14 §7.2.4.6.4 DataSetMetaData message</see> change-notification
    /// semantics — subscribers attach to the registry event so they can
    /// drop in-flight reception state bound to the previous metadata
    /// version.
    /// </remarks>
    public sealed class DataSetMetaDataChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="DataSetMetaDataChangedEventArgs"/>.
        /// </summary>
        /// <param name="key">Identity tuple of the affected metadata.</param>
        /// <param name="previous">
        /// The metadata that was registered before the change, or
        /// <see langword="null"/> when the key is newly registered.
        /// </param>
        /// <param name="current">
        /// The metadata description that is now registered for
        /// <paramref name="key"/>. Must not be <see langword="null"/>.
        /// </param>
        public DataSetMetaDataChangedEventArgs(
            DataSetMetaDataKey key,
            DataSetMetaDataType? previous,
            DataSetMetaDataType current)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            Key = key;
            Previous = previous;
            Current = current;
        }

        /// <summary>
        /// Identity tuple of the metadata that changed.
        /// </summary>
        public DataSetMetaDataKey Key { get; }

        /// <summary>
        /// Metadata description as it was registered prior to the change,
        /// or <see langword="null"/> when no prior entry existed.
        /// </summary>
        public DataSetMetaDataType? Previous { get; }

        /// <summary>
        /// Metadata description that is now registered for
        /// <see cref="Key"/>.
        /// </summary>
        public DataSetMetaDataType Current { get; }
    }
}
