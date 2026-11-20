/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Optional capability implemented by an <see cref="IPublishedDataSetSource"/>
    /// whose <see cref="DataSetMetaDataType"/> can change after construction (for
    /// example a source that resolves field data types from a remote server and
    /// re-resolves them on retry or on a model change). When a source implements
    /// this interface the owning <see cref="PublishedDataSet"/> subscribes to
    /// <see cref="MetaDataChanged"/> and refreshes its cached metadata so a new
    /// DataSetMetaData message is emitted to subscribers.
    /// </summary>
    public interface IMetaDataChangeNotifier
    {
        /// <summary>
        /// Raised by the source when its metadata has changed and the owning
        /// PublishedDataSet should rebuild and re-publish it.
        /// </summary>
        event EventHandler? MetaDataChanged;
    }
}
