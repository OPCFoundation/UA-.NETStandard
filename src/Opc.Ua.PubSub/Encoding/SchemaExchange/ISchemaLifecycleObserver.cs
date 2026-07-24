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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Identifies the schema an encoder produced for a published DataSet: the
    /// DataSet key and the produced schema's <see cref="SchemaId"/> (the schema
    /// fingerprint) plus the encoding <see cref="Format"/> and destination.
    /// </summary>
    /// <param name="MetaDataKey">The DataSet the schema was produced for.</param>
    /// <param name="SchemaId">The produced schema's fingerprint (SchemaId).</param>
    /// <param name="Format">The encoding format, for example <c>avro</c> or <c>arrow</c>.</param>
    /// <param name="DestinationId">The destination identity used for announce-once tracking.</param>
    public readonly record struct SchemaChangeNotification(
        DataSetMetaDataKey MetaDataKey,
        ByteString SchemaId,
        string Format,
        string DestinationId);

    /// <summary>
    /// Observes the schema an encoder produces for each published DataSet so the
    /// publisher can react to a schema-fingerprint change. The canonical reaction is
    /// to advance the DataSet <c>ConfigurationVersion</c> when the produced schema
    /// grows (Avro Part 6 §6.4 / Part 14 §8.4.8) and to publish the new schema to
    /// subscribers (registry registration plus announcement, §8.4.5). The
    /// notification is raised only when the encoder produced a not-yet-seen SchemaId.
    /// </summary>
    public interface ISchemaLifecycleObserver
    {
        /// <summary>
        /// Invoked when an encoder produced a schema whose SchemaId had not yet been
        /// announced to the destination.
        /// </summary>
        /// <param name="change">The produced-schema notification.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask OnSchemaProducedAsync(
            SchemaChangeNotification change,
            CancellationToken cancellationToken = default);
    }
}
