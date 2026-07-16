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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Renames DataSetFields by a source-to-target name map. Fields not
    /// present in the map keep their original name; field order and
    /// values are unchanged.
    /// </summary>
    /// <remarks>
    /// Renaming re-labels the DataSet field names carried in the
    /// DataSetMessage payload (Part 14 §5.3.2). Downstream readers match
    /// on the renamed names; keep the target metadata aligned when
    /// renaming for a metadata-driven RawData consumer.
    /// </remarks>
    public sealed class FieldRenameTransform : IPubSubMessageTransform
    {
        private readonly IReadOnlyDictionary<string, string> m_map;

        /// <summary>
        /// Initializes a new <see cref="FieldRenameTransform"/>.
        /// </summary>
        /// <param name="renameMap">
        /// Map of source field name to target field name (ordinal).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="renameMap"/> is
        /// <see langword="null"/>.
        /// </exception>
        public FieldRenameTransform(IReadOnlyDictionary<string, string> renameMap)
        {
            m_map = renameMap ?? throw new ArgumentNullException(nameof(renameMap));
        }

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (m_map.Count == 0 || message.DataSetMessages.Count == 0)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }

            var mapped = new List<PubSubDataSetMessage>(message.DataSetMessages.Count);
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                PubSubDataSetMessage dsm = message.DataSetMessages[i];
                mapped.Add(dsm with { Fields = Rename(dsm.Fields) });
            }
            return new ValueTask<PubSubNetworkMessage?>(
                message with { DataSetMessages = mapped });
        }

        private ArrayOf<DataSetField> Rename(ArrayOf<DataSetField> fields)
        {
            if (fields.Count == 0)
            {
                return fields;
            }
            var mapped = new List<DataSetField>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                DataSetField field = fields[i];
                mapped.Add(m_map.TryGetValue(field.Name, out string? newName)
                    ? field with { Name = newName }
                    : field);
            }
            return mapped;
        }
    }
}
