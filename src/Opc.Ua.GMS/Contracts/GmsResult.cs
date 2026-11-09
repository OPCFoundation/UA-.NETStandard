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
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// An immutable acquired GMS result, retaining the standard Machinery envelope and raw quality.
    /// </summary>
    public sealed class GmsResult
    {
        /// <summary>
        /// Captures a structured result sample and its optional used-tool identifiers.
        /// </summary>
        /// <param name="value">The acquired ResultDataType sample, including quality and timestamps.</param>
        /// <param name="usedTools">Tool identifiers; null means the optional property is not supplied.</param>
        /// <param name="characteristics">The characteristics of an intermediate result.</param>
        /// <param name="programName">The optional originating program name.</param>
        /// <param name="isIntermediate">Whether this sample is an intermediate, rather than final, result.</param>
        /// <param name="fileContent">Optional transferable file content.</param>
        /// <param name="mimeType">The content's MIME type.</param>
        public GmsResult(
            in DataValue value,
            ArrayOf<string> usedTools = default,
            ArrayOf<GmsCharacteristic> characteristics = default,
            string? programName = null,
            bool isIntermediate = false,
            ByteString fileContent = default,
            string mimeType = "application/octet-stream")
        {
            if (!value.WrappedValue.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out ResultDataType? data) ||
                data.ResultMetaData == null ||
                string.IsNullOrWhiteSpace(data.ResultMetaData.ResultId))
            {
                throw new ArgumentException("A ResultDataType with a non-empty ResultId is required.", nameof(value));
            }
            if (isIntermediate && characteristics.Count == 0)
            {
                throw new ArgumentException("An intermediate result requires at least one characteristic.",
                    nameof(characteristics));
            }
            if (!isIntermediate && (characteristics.Count != 0 || programName != null))
            {
                throw new ArgumentException(
                    "Characteristics and ProgramName belong to an intermediate result event.", nameof(isIntermediate));
            }
            foreach (GmsCharacteristic characteristic in characteristics)
            {
                if (characteristic == null)
                {
                    throw new ArgumentException("Characteristics must not contain null entries.", nameof(characteristics));
                }
            }
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("A MIME type is required.", nameof(mimeType));
            }
            m_data = (ResultDataType)data.Clone();
            if (isIntermediate)
            {
                m_data.ResultMetaData!.IsPartial = true;
                m_data.ResultMetaData.EncodingMask |= (uint)ResultMetaDataTypeFields.IsPartial;
            }
            if (!fileContent.IsNull && !fileContent.IsEmpty)
            {
                m_data.ResultMetaData!.HasTransferableDataOnFile = true;
                m_data.ResultMetaData.EncodingMask |= (uint)ResultMetaDataTypeFields.HasTransferableDataOnFile;
            }
            m_value = value.WithWrappedValue(Variant.FromStructure(m_data)).Copy();
            m_usedTools = usedTools.IsNull ? default : new ArrayOf<string>(usedTools.ToArray()!);
            m_characteristics = characteristics.IsNull ? [] : new ArrayOf<GmsCharacteristic>(characteristics.ToArray()!);
            m_fileContent = fileContent.IsNull ? default : new ByteString(fileContent.Span.ToArray());
            ProgramName = programName;
            IsIntermediate = isIntermediate;
            MimeType = mimeType;
        }

        /// <summary>
        /// Gets the stable result identifier.
        /// </summary>
        public string ResultId => m_data.ResultMetaData!.ResultId!;

        /// <summary>
        /// Gets a detached copy of the standard Machinery result envelope.
        /// </summary>
        public ResultDataType Data => (ResultDataType)m_data.Clone();

        /// <summary>
        /// Gets a detached copy of the raw acquired sample.
        /// </summary>
        public DataValue Value => m_value.Copy();

        /// <summary>
        /// Gets the optional identifiers of the tools used to acquire this result.
        /// </summary>
        public ArrayOf<string> UsedTools => m_usedTools.IsNull ? default : new ArrayOf<string>(m_usedTools.ToArray()!);

        /// <summary>
        /// Gets the immutable characteristic snapshots for an intermediate event.
        /// </summary>
        public ArrayOf<GmsCharacteristic> Characteristics => new(m_characteristics.ToArray()!);

        /// <summary>
        /// Gets the optional originating program.
        /// </summary>
        public string? ProgramName { get; }

        /// <summary>
        /// Gets whether this result uses IntermediateResultEventType instead of ResultReadyEventType.
        /// </summary>
        public bool IsIntermediate { get; }

        /// <summary>
        /// Gets a detached copy of the optional file payload.
        /// </summary>
        public ByteString FileContent => m_fileContent.IsNull ? default : new ByteString(m_fileContent.Span.ToArray());

        /// <summary>
        /// Gets the MIME type of the file payload.
        /// </summary>
        public string MimeType { get; }

        private readonly ResultDataType m_data;
        private readonly DataValue m_value;
        private readonly ArrayOf<string> m_usedTools;
        private readonly ArrayOf<GmsCharacteristic> m_characteristics;
        private readonly ByteString m_fileContent;
    }
}
