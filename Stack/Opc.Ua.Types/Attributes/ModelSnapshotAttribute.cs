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

namespace Opc.Ua
{
    /// <summary>
    /// Carries a compact binary snapshot of a model the assembly emits, so
    /// downstream source generators can resolve cross-assembly type
    /// references without the consumer having to re-add the upstream
    /// NodeSet2 / ModelDesign XML to <c>AdditionalFiles</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted by the OPC UA source generator alongside
    /// <see cref="ModelDependencyAttribute"/>: one entry per generated
    /// model. The <see cref="Payload"/> is a base64-encoded
    /// Deflate-compressed serialisation of the model's exported type
    /// table (types, base-type chain, NodeIds, DataType fields, method
    /// signatures, and child placeholders) — enough information for a
    /// downstream generator to walk the dependency tree without re-parsing
    /// the upstream NodeSet2.
    /// </para>
    /// <para>
    /// The wire format is <c>ModelSnapshotV1</c> (magic <c>0xAA 0xC7</c>,
    /// version byte <c>1</c>, Deflate-compressed). Readers reject unknown
    /// versions cleanly and fall back to explicit <c>AdditionalFiles</c>
    /// resolution.
    /// </para>
    /// <para>
    /// The attribute has no runtime use — it is purely compile-time
    /// metadata consumed by the source generator.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class ModelSnapshotAttribute : Attribute
    {
        /// <summary>
        /// Initialises a new instance.
        /// </summary>
        /// <param name="modelUri">The OPC UA model URI (matches the
        /// <see cref="ModelDependencyAttribute.ModelUri"/> entry on the
        /// same assembly).</param>
        /// <param name="payload">Base64-encoded Deflate-compressed
        /// <c>ModelSnapshotV1</c> payload.</param>
        public ModelSnapshotAttribute(string modelUri, string payload)
        {
            ModelUri = modelUri;
            Payload = payload;
        }

        /// <summary>
        /// The OPC UA model URI this snapshot is for.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// Base64-encoded Deflate-compressed <c>ModelSnapshotV1</c> payload.
        /// </summary>
        public string Payload { get; }
    }
}
