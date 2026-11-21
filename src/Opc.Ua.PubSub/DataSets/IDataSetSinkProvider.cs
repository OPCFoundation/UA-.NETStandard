// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Resolves subscriber-side data-set sinks by DataSetReader name at runtime.
    /// </summary>
    public interface IDataSetSinkProvider
    {
        /// <summary>
        /// Attempts to resolve the sink for <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <param name="sink">Resolved sink when the method returns <see langword="true"/>.</param>
        /// <returns>
        /// <see langword="true"/> when a sink was resolved; otherwise <see langword="false"/>.
        /// </returns>
        bool TryGetSink(string dataSetReaderName, out ISubscribedDataSetSink sink);
    }
}
