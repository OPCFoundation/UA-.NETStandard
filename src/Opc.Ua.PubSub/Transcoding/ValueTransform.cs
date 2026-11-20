/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Applies a caller-supplied value transformation to every
    /// DataSetField value. Typical uses include unit conversion, scaling,
    /// type coercion, or redaction. The field name and value are passed to
    /// the delegate; the returned <see cref="Variant"/> replaces the
    /// field value.
    /// </summary>
    /// <remarks>
    /// Operates on the DataSetMessage payload values (Part 14 §5.3.2). The
    /// delegate must be pure and non-blocking; it is invoked once per
    /// field on the receive path.
    /// </remarks>
    public sealed class ValueTransform : IPubSubMessageTransform
    {
        private readonly Func<string, Variant, Variant> m_transform;

        /// <summary>
        /// Initializes a new <see cref="ValueTransform"/>.
        /// </summary>
        /// <param name="transform">
        /// Value transformation invoked with the field name and current
        /// value, returning the replacement value.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="transform"/> is
        /// <see langword="null"/>.
        /// </exception>
        public ValueTransform(Func<string, Variant, Variant> transform)
        {
            m_transform = transform ?? throw new ArgumentNullException(nameof(transform));
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
            if (message.DataSetMessages.Count == 0)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }

            var mapped = new List<PubSubDataSetMessage>(message.DataSetMessages.Count);
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                PubSubDataSetMessage dsm = message.DataSetMessages[i];
                mapped.Add(dsm with { Fields = Apply(dsm.Fields) });
            }
            return new ValueTask<PubSubNetworkMessage?>(
                message with { DataSetMessages = mapped });
        }

        private ArrayOf<DataSetField> Apply(ArrayOf<DataSetField> fields)
        {
            if (fields.Count == 0)
            {
                return fields;
            }
            var mapped = new List<DataSetField>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                DataSetField field = fields[i];
                Variant transformed = m_transform(field.Name, field.Value);
                mapped.Add(field with { Value = transformed });
            }
            return mapped;
        }
    }
}
