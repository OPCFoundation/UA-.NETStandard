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

#nullable enable

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Records the OPC UA model(s) an assembly was compiled against.
    /// Emitted automatically by the OPC UA source generator, one occurrence per
    /// model consumed (dependency) and one for each model the assembly emits
    /// (self-declaration).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator in a downstream project reads these attributes from
    /// every referenced assembly (via <c>System.Reflection.Metadata.MetadataReader</c>)
    /// to resolve cross-assembly model dependencies without forcing every
    /// consumer to re-declare the same <c>&lt;AdditionalFiles&gt;</c>.
    /// </para>
    /// <para>
    /// When a downstream project overrides a model via <c>&lt;AdditionalFiles&gt;</c>,
    /// the generator compares the override's resolved <see cref="Prefix"/> against
    /// every recorded <see cref="Prefix"/> for the same <see cref="ModelUri"/> across
    /// referenced assemblies. A match is an error (symbol collision); a difference
    /// is expected (override types live in their own C# namespace).
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class ModelDependencyAttribute : Attribute
    {
        /// <summary>
        /// Initialises a new instance.
        /// </summary>
        /// <param name="modelUri">The OPC UA model URI (matches the NodeSet2
        /// <c>ModelUri</c> or ModelDesign <c>TargetNamespace</c>).</param>
        /// <param name="prefix">The C# namespace the assembly used for generated
        /// types in this model.</param>
        /// <param name="version">The model version string as declared in the
        /// source file (e.g. <c>"1.05.07"</c>). May be null if the source
        /// declared none.</param>
        /// <param name="publicationDate">The model publication date string
        /// (ISO-8601 preferred). May be null.</param>
        public ModelDependencyAttribute(
            string modelUri,
            string prefix,
            string? version = null,
            string? publicationDate = null)
        {
            ModelUri = modelUri;
            Prefix = prefix;
            Version = version;
            PublicationDate = publicationDate;
        }

        /// <summary>
        /// The OPC UA model URI.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// The C# namespace in which generated types for this model live
        /// inside the annotated assembly.
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// The model version, if declared. Null when the source did not
        /// specify one.
        /// </summary>
        public string? Version { get; }

        /// <summary>
        /// The model publication date (ISO-8601 string), if declared.
        /// Null when absent.
        /// Used as the tie-break when multiple referenced assemblies
        /// provide the same model
        /// at the same <see cref="Version"/>.
        /// </summary>
        public string? PublicationDate { get; }
    }
}
