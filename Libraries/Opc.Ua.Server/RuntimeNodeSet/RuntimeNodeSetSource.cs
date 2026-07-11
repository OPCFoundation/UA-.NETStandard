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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Abstract provider for a single NodeSet2 document that can be loaded
    /// into a <see cref="RuntimeNodeSetNodeManagerFactory"/> at server startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each source declares the set of model namespace URIs it owns (those
    /// listed in the <c>Models</c> section of the NodeSet2 XML). This
    /// information is used before parsing to build the factory's
    /// <see cref="IAsyncNodeManagerFactory.NamespacesUris"/> list and to
    /// resolve the dependency order in which sources are imported.
    /// </para>
    /// <para>
    /// Concrete subtypes are <see cref="FileRuntimeNodeSetSource"/> for
    /// file-backed NodeSet2 documents and
    /// <see cref="StreamRuntimeNodeSetSource"/> for documents vended by a
    /// caller-supplied async delegate.
    /// </para>
    /// </remarks>
    public abstract class RuntimeNodeSetSource
    {
        /// <summary>
        /// Gets a human-readable name used in diagnostics.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// The model namespace URIs declared as <em>owned</em> by this
        /// source, i.e. the <c>Models/Model/@ModelUri</c> values in the
        /// NodeSet2 XML. These are the namespaces the node manager will
        /// claim and serve.
        /// </summary>
        public abstract ArrayOf<string> ModelNamespaceUris { get; }

        /// <summary>
        /// Opens a fresh stream positioned at the beginning
        /// of the NodeSet2 document. Called once during
        /// <see cref="RuntimeNodeSetNodeManagerFactory.CreateAsync"/> to
        /// perform the full parse.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancellation token forwarded from the factory's
        /// <c>CreateAsync</c> call.
        /// </param>
        /// <returns>
        /// A readable stream whose content is a valid NodeSet2 XML
        /// document. The runtime NodeSet factory closes it after parsing.
        /// </returns>
        public abstract ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a <see cref="FileRuntimeNodeSetSource"/> for the
        /// NodeSet2 file at <paramref name="filePath"/>. The file is
        /// opened once during construction to extract its
        /// <c>Models</c> metadata; it is reopened at server startup for
        /// the full import.
        /// </summary>
        /// <param name="filePath">
        /// Absolute or relative path to a NodeSet2 XML file.
        /// </param>
        /// <returns>
        /// A <see cref="FileRuntimeNodeSetSource"/> whose
        /// <see cref="ModelNamespaceUris"/> reflect the models declared
        /// in the file.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="filePath"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file cannot be read or does not contain a valid NodeSet2
        /// document.
        /// </exception>
        public static FileRuntimeNodeSetSource FromFile(string filePath)
        {
            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            return new FileRuntimeNodeSetSource(filePath);
        }

        /// <summary>
        /// Creates a <see cref="FileRuntimeNodeSetSource"/> for an older
        /// NodeSet2 file that does not declare a <c>Models</c> section.
        /// </summary>
        public static FileRuntimeNodeSetSource FromFile(
            string filePath,
            ArrayOf<string> modelNamespaceUris)
        {
            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (modelNamespaceUris.IsNull)
            {
                throw new ArgumentNullException(nameof(modelNamespaceUris));
            }

            return new FileRuntimeNodeSetSource(filePath, modelNamespaceUris);
        }

        /// <summary>
        /// Creates a <see cref="StreamRuntimeNodeSetSource"/> backed by
        /// the supplied async delegate.
        /// </summary>
        /// <param name="name">
        /// Human-readable source name used in diagnostics.
        /// </param>
        /// <param name="openStream">
        /// Delegate that opens and returns a fresh, positioned stream
        /// containing a NodeSet2 XML document. Called once at server
        /// startup; the returned stream is closed by the factory after
        /// parsing.
        /// </param>
        /// <param name="modelNamespaceUris">
        /// The model namespace URIs owned by this source. Because the
        /// stream cannot be pre-scanned without consuming it, the caller
        /// must declare the owned URIs explicitly here.
        /// </param>
        /// <returns>
        /// A <see cref="StreamRuntimeNodeSetSource"/> configured with the
        /// supplied delegate and declared URIs.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="openStream"/> or
        /// <paramref name="modelNamespaceUris"/> is <c>null</c>.
        /// </exception>
        public static StreamRuntimeNodeSetSource FromStream(
            string name,
            Func<CancellationToken, ValueTask<Stream>> openStream,
            ArrayOf<string> modelNamespaceUris)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A source name is required.", nameof(name));
            }

            if (openStream is null)
            {
                throw new ArgumentNullException(nameof(openStream));
            }

            if (modelNamespaceUris.IsNull)
            {
                throw new ArgumentNullException(nameof(modelNamespaceUris));
            }
            if (modelNamespaceUris.Count == 0)
            {
                throw new ArgumentException(
                    "At least one owned model namespace URI is required.",
                    nameof(modelNamespaceUris));
            }

            return new StreamRuntimeNodeSetSource(name, openStream, modelNamespaceUris);
        }

        /// <summary>
        /// Performs an early scan of a file to extract the model namespace
        /// URIs without retaining the full parsed object graph.
        /// </summary>
        /// <param name="filePath">
        /// Path to the NodeSet2 XML file.
        /// </param>
        /// <returns>
        /// The model namespace URIs declared by the file.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// A model declaration does not contain a ModelUri.
        /// </exception>
        internal static ArrayOf<string> ScanModelUris(string filePath)
        {
            using var reader = XmlReader.Create(filePath, CoreUtils.DefaultXmlReaderSettings());
            var modelUris = new List<string>();

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "Model", StringComparison.Ordinal))
                {
                    continue;
                }

                string? modelUri = reader.GetAttribute("ModelUri");
                if (string.IsNullOrWhiteSpace(modelUri))
                {
                    throw new InvalidOperationException(
                        $"The NodeSet2 file '{filePath}' contains a Model without a ModelUri.");
                }

                modelUris.Add(modelUri);
            }

            return [.. modelUris];
        }

        /// <summary>
        /// Extracts owned model namespace URIs from a parsed
        /// <see cref="UANodeSet"/>. Uses <c>Models[].ModelUri</c> when
        /// present; falls back to <c>NamespaceUris</c> for older NodeSets
        /// that omit the <c>Models</c> element.
        /// </summary>
        /// <param name="nodeSet">The parsed NodeSet2 document.</param>
        /// <param name="declaredUris">
        /// Caller-declared namespace URIs used as a secondary fallback
        /// when both <c>Models</c> and <c>NamespaceUris</c> are absent.
        /// May be an empty collection.
        /// </param>
        /// <returns>
        /// An <see cref="ArrayOf{T}"/> of owned model namespace URIs.
        /// </returns>
        internal static ArrayOf<string> ExtractModelUris(
            UANodeSet nodeSet,
            ArrayOf<string> declaredUris)
        {
            if (nodeSet.Models is { Length: > 0 })
            {
                var uris = new string[nodeSet.Models.Length];

                for (int i = 0; i < nodeSet.Models.Length; i++)
                {
                    string? uri = nodeSet.Models[i].ModelUri;

                    if (string.IsNullOrEmpty(uri))
                    {
                        throw new InvalidOperationException(
                            "A Models/Model element is missing its ModelUri attribute.");
                    }

                    uris[i] = uri!;
                }

                return uris;
            }

            return declaredUris;
        }
    }

    /// <summary>
    /// A <see cref="RuntimeNodeSetSource"/> backed by a NodeSet2 XML file.
    /// </summary>
    /// <remarks>
    /// The file is opened once during construction to extract model
    /// metadata; it is reopened at server startup for the full import.
    /// </remarks>
    public sealed class FileRuntimeNodeSetSource : RuntimeNodeSetSource
    {
        /// <summary>
        /// Initializes the source for the NodeSet2 file at
        /// <paramref name="filePath"/>. The file is opened immediately to
        /// extract model metadata.
        /// </summary>
        /// <param name="filePath">
        /// Absolute or relative path to a NodeSet2 XML file.
        /// </param>
        /// <param name="declaredModelNamespaceUris">
        /// Namespace URIs supplied for an older NodeSet without a Models section.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="filePath"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file cannot be read or is not a valid NodeSet2 document.
        /// </exception>
        internal FileRuntimeNodeSetSource(
            string filePath,
            ArrayOf<string> declaredModelNamespaceUris = default)
        {
            m_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            ArrayOf<string> scannedModelUris = ScanModelUris(filePath);
            m_modelNamespaceUris = scannedModelUris.Count > 0
                ? scannedModelUris
                : declaredModelNamespaceUris;

            if (m_modelNamespaceUris.IsNull || m_modelNamespaceUris.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The NodeSet2 file '{filePath}' does not declare a Models section. " +
                    "Use RuntimeNodeSetSource.FromFile(filePath, modelNamespaceUris) " +
                    "to declare the namespaces owned by this legacy NodeSet.");
            }
        }

        /// <inheritdoc/>
        public override string Name => m_filePath;

        /// <inheritdoc/>
        public override ArrayOf<string> ModelNamespaceUris => m_modelNamespaceUris;

        /// <summary>
        /// The path to the NodeSet2 XML file.
        /// </summary>
        public string FilePath => m_filePath;

        /// <inheritdoc/>
        public override ValueTask<Stream> OpenReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = File.OpenRead(m_filePath);
            return new ValueTask<Stream>(stream);
        }

        private readonly string m_filePath;
        private readonly ArrayOf<string> m_modelNamespaceUris;
    }

    /// <summary>
    /// A <see cref="RuntimeNodeSetSource"/> backed by a caller-supplied
    /// async stream factory.
    /// </summary>
    /// <remarks>
    /// Because the stream is not available until server startup, the caller
    /// must declare the owned model namespace URIs explicitly in the
    /// constructor.
    /// </remarks>
    public sealed class StreamRuntimeNodeSetSource : RuntimeNodeSetSource
    {
        /// <summary>
        /// Initializes the source with the supplied stream factory and
        /// declared model namespace URIs.
        /// </summary>
        /// <param name="openStream">
        /// Delegate that opens and returns a fresh, positioned stream
        /// containing a NodeSet2 XML document.
        /// </param>
        /// <param name="name">
        /// Human-readable source name used in diagnostics.
        /// </param>
        /// <param name="modelNamespaceUris">
        /// The model namespace URIs owned by this source.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="openStream"/> or
        /// <paramref name="modelNamespaceUris"/> is <c>null</c>.
        /// </exception>
        internal StreamRuntimeNodeSetSource(
            string name,
            Func<CancellationToken, ValueTask<Stream>> openStream,
            ArrayOf<string> modelNamespaceUris)
        {
            m_name = name;
            m_openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));

            if (modelNamespaceUris.IsNull)
            {
                throw new ArgumentNullException(nameof(modelNamespaceUris));
            }

            m_modelNamespaceUris = modelNamespaceUris;
        }

        /// <inheritdoc/>
        public override string Name => m_name;

        /// <inheritdoc/>
        public override ArrayOf<string> ModelNamespaceUris => m_modelNamespaceUris;

        /// <inheritdoc/>
        public override ValueTask<Stream> OpenReadAsync(
            CancellationToken cancellationToken = default)
        {
            return m_openStream(cancellationToken);
        }

        private readonly string m_name;
        private readonly Func<CancellationToken, ValueTask<Stream>> m_openStream;
        private readonly ArrayOf<string> m_modelNamespaceUris;
    }
}
