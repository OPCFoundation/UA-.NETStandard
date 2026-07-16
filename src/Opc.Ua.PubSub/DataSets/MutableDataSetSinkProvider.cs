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
using System.Collections.Concurrent;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Thread-safe mutable <see cref="IDataSetSinkProvider"/> backed by a name map.
    /// </summary>
    public sealed class MutableDataSetSinkProvider : IDataSetSinkProvider
    {
        private readonly ConcurrentDictionary<string, ISubscribedDataSetSink> m_sinks =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Registers or replaces the sink for <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <param name="sink">Sink implementation.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void Register(string dataSetReaderName, ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            m_sinks[dataSetReaderName] = sink;
        }

        /// <summary>
        /// Removes the sink for <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <returns>
        /// <see langword="true"/> when a sink was removed; otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentException"></exception>
        public bool Remove(string dataSetReaderName)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }

            return m_sinks.TryRemove(dataSetReaderName, out _);
        }

        /// <inheritdoc/>
        public bool TryGetSink(string dataSetReaderName, out ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                sink = null!;
                return false;
            }

            return m_sinks.TryGetValue(dataSetReaderName, out sink!);
        }
    }
}
