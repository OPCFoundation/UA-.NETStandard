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
using System.Buffers.Binary;
using System.Text;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Default key selector: the entry is unique by
    /// <c>SourceTimestamp</c> alone, which is the raw-history rule of
    /// Part 11 §5.2.4.
    /// </summary>
    /// <remarks>
    /// Use this selector for structures that hold exactly one value per
    /// timestamp — for example annotations, which the framework keys by
    /// <c>AnnotationTime</c>.
    /// </remarks>
    public sealed class TimestampStructuredDataKeySelector : IHistorianStructuredDataKeySelector
    {
        /// <summary>
        /// The shared selector instance.
        /// </summary>
        public static TimestampStructuredDataKeySelector Instance { get; } = new();

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> UniquenessFields { get; }
            = new QualifiedName[] { new(BrowseNames.SourceTimestamp) };

        /// <inheritdoc/>
        public bool TryGetUniquenessKey(in DataValue value, out ByteString uniquenessKey)
        {
            uniquenessKey = ByteString.Empty;
            return true;
        }
    }

    /// <summary>
    /// Key selector for archives of standard <see cref="KeyValuePair"/>
    /// structures: an entry is unique by <c>SourceTimestamp</c> plus the
    /// <c>Key</c> of the pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the canonical example of StructuredHistoryData that stores
    /// several entries at one instant: a device that publishes a set of
    /// named readings captures one <see cref="KeyValuePair"/> per reading,
    /// all sharing the capture timestamp.
    /// </para>
    /// <para>
    /// The key encodes a version byte, the namespace index of the
    /// qualified name and its UTF-8 name, which makes it stable, ordinal
    /// and independent of the value carried by the pair. Changing the
    /// <c>Key</c> of a stored pair changes the entry identity, so such an
    /// edit has to be expressed as a remove followed by an insert.
    /// </para>
    /// </remarks>
    public sealed class KeyValuePairStructuredDataKeySelector : IHistorianStructuredDataKeySelector
    {
        /// <summary>
        /// The shared selector instance.
        /// </summary>
        public static KeyValuePairStructuredDataKeySelector Instance { get; } = new();

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> UniquenessFields { get; }
            = new QualifiedName[]
            {
                new(BrowseNames.SourceTimestamp),
                new(kKeyFieldName)
            };

        /// <inheritdoc/>
        public bool TryGetUniquenessKey(in DataValue value, out ByteString uniquenessKey)
        {
            if (value.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out IEncodeable? body) &&
                body is KeyValuePair pair)
            {
                uniquenessKey = Encode(pair.Key);
                return true;
            }

            uniquenessKey = ByteString.Empty;
            return false;
        }

        /// <summary>
        /// Encodes a qualified name into the canonical uniqueness key
        /// used by this selector.
        /// </summary>
        public static ByteString Encode(QualifiedName key)
        {
            byte[] name = Encoding.UTF8.GetBytes(
                key.IsNull ? string.Empty : key.Name ?? string.Empty);
            byte[] buffer = new byte[kPrefixLength + name.Length];
            buffer[0] = kVersion;
            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.AsSpan(sizeof(byte)),
                key.NamespaceIndex);
            name.CopyTo(buffer, kPrefixLength);
            return ByteString.From(buffer);
        }

        private const string kKeyFieldName = "Key";
        private const byte kVersion = 1;
        private const int kPrefixLength = sizeof(byte) + sizeof(ushort);
    }
}
