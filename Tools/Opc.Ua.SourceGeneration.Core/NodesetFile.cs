/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// An entry in the collection
    /// </summary>
    public sealed class NodesetFile
    {
        /// <summary>
        /// Nodeset information
        /// </summary>
        public NodesetFileOptions Info { get; init; }

        /// <summary>
        /// File name
        /// </summary>
        public string FileName { get; init; }

        /// <summary>
        /// The nodeset parsed
        /// </summary>
        public UANodeSet NodeSet { get; init; }

        /// <summary>
        /// Previous versions
        /// </summary>
        internal List<NodesetFile> PreviousVersions { get; set; }
    }

    /// <summary>
    /// Nodeset information
    /// </summary>
    public sealed record class NodesetFileOptions
    {
        /// <summary>
        /// Model uri
        /// </summary>
        public string ModelUri { get; init; }

        /// <summary>
        /// Name
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Prefix to use
        /// </summary>
        public string Prefix { get; init; }

        /// <summary>
        /// Should be ignored
        /// </summary>
        public bool Ignore { get; init; }

        /// <summary>
        /// Version
        /// </summary>
        public string Version { get; init; }
    }

    /// <summary>
    /// Nodeset compiler
    /// </summary>
    public sealed class NodesetFileCollection
    {
        /// <summary>
        /// The files in the collection
        /// </summary>
        public Dictionary<string, string> Files => m_nodesets
            .ToDictionary(x => x.Key, x => x.Value.FileName);

        /// <summary>
        /// The models in the collection
        /// </summary>
        public IEnumerable<string> ModelUris => m_nodesets.Values
            .Where(x => !x.Info.Ignore)
            .Select(x => x.Info.ModelUri)
            .Where(x => !string.IsNullOrEmpty(x));

        /// <summary>
        /// Create collection
        /// </summary>
        public NodesetFileCollection(
            ImmutableArray<(string, NodesetFileOptions)> nodeset2Files,
            IFileSystem fileSystem,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<NodesetFileCollection>();
            foreach ((string file, NodesetFileOptions options) in nodeset2Files)
            {
                try
                {
                    if (!NodeSetToModelDesign.IsNodeSet(fileSystem, file))
                    {
                        continue;
                    }

                    using Stream istrm = fileSystem.OpenRead(file);
                    SystemContext systemContext = new(telemetry)
                    {
                        NamespaceUris = new NamespaceTable(),
                        ServerUris = new StringTable()
                    };
                    var nodeset = UANodeSet.Read(istrm);
                    var collection = new NodeStateCollection();
                    try
                    {
                        nodeset.Import(systemContext, collection);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "NodeSet could not be loaded ({File})", file);
                        return;
                    }

                    if (nodeset.Models == null ||
                        nodeset.Models.Length == 0 ||
                        string.IsNullOrEmpty(nodeset.Models[0].ModelUri))
                    {
                        m_logger.LogError("NodeSet is missing model definition ({File}).", file);
                        continue;
                    }

                    ModelTableEntry model = nodeset.Models[0];
                    if (!Uri.IsWellFormedUriString(model.ModelUri, UriKind.Absolute))
                    {
                        m_logger.LogError(
                           "NodeSet ModelURI is not valid ({ModelUri}).", model.ModelUri);
                        continue;
                    }

                    string name = GetNameFromUri(model.ModelUri); // Get a sane name and prefix
                    var info = new NodesetFile
                    {
                        FileName = file,
                        NodeSet = nodeset,
                        Info = new NodesetFileOptions // Set reasonable defaults if not provided
                        {
                            ModelUri = !string.IsNullOrEmpty(options.ModelUri) ?
                                options.ModelUri : model.ModelUri,
                            Version = !string.IsNullOrEmpty(options.Version) ?
                                options.Version :
                                model.PublicationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            Name = !string.IsNullOrEmpty(options.Name) ?
                                options.Name : name,
                            Prefix = !string.IsNullOrEmpty(options.Prefix) ?
                                options.Prefix : name,
                            Ignore = options.Ignore
                        }
                    };

                    if (m_nodesets.TryGetValue(model.ModelUri, out NodesetFile existing) &&
                        existing.Info.Version.CompareTo(info.Info.Version, StringComparison.Ordinal) < 0)
                    {
                        info.PreviousVersions = [];

                        if (existing.PreviousVersions != null)
                        {
                            info.PreviousVersions.AddRange(existing.PreviousVersions);
                        }

                        existing.PreviousVersions = null;
                        info.PreviousVersions.Add(existing);
                    }
                    m_nodesets[model.ModelUri] = info;
                }
                catch (Exception ex)
                {
                    m_logger.LogCritical(ex, "Could not parse NodeSet ({File}).", file);
                }
            }
        }

        /// <summary>
        /// Get nodeset and dependencies for the model uri
        /// </summary>
        /// <returns></returns>
        public List<string> GetDesignFileListForModel(
            string modelUri,
            out NodesetFile nodeset)
        {
            if (!m_nodesets.TryGetValue(modelUri, out nodeset))
            {
                return null;
            }

            Dictionary<string, NodesetFile> dependencies = [];
            if (!CollectDependencies(nodeset, dependencies))
            {
                return null;
            }

            List<string> files = [$"{nodeset.FileName},{nodeset.Info.Prefix},{nodeset.Info.Name}"];
            foreach (NodesetFile dependency in dependencies.Values
                .Where(x => x.Info.ModelUri != Namespaces.OpcUa))
            {
                files.Add($"{dependency.FileName},{dependency.Info.Prefix},{dependency.Info.Name}");
            }
            return files;
        }

        /// <summary>
        /// Collect dependencies
        /// </summary>
        private bool CollectDependencies(
            NodesetFile target,
            Dictionary<string, NodesetFile> dependencies)
        {
            if (target.NodeSet.NamespaceUris == null)
            {
                return true;
            }

            foreach (string ns in target.NodeSet.NamespaceUris)
            {
                if (dependencies.ContainsKey(ns) || ns == target.Info.ModelUri)
                {
                    continue;
                }

                if (!m_nodesets.TryGetValue(ns, out NodesetFile nodeset))
                {
                    m_logger.LogError(
                        "NodeSet ({ModelUri}) dependency is missing ({Namespace}).",
                        target.Info.ModelUri,
                        ns);
                    return false;
                }

                // favour the version in the same directory as the target.
                if (nodeset.PreviousVersions != null &&
                    Path.GetDirectoryName(nodeset.FileName) !=
                        Path.GetDirectoryName(target.FileName))
                {
                    foreach (NodesetFile ii in nodeset.PreviousVersions)
                    {
                        if (Path.GetDirectoryName(ii.FileName) ==
                                Path.GetDirectoryName(target.FileName))
                        {
                            nodeset = ii;
                            break;
                        }
                    }
                }

                dependencies[ns] = nodeset;
                if (!CollectDependencies(nodeset, dependencies))
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetNameFromUri(string uri)
        {
            var builder = new Uri(uri);
            string path = builder.LocalPath.TrimEnd('/');

            if (path.StartsWith("/UA/", StringComparison.OrdinalIgnoreCase))
            {
                path = path[4..];
            }

            if (path.StartsWith("/OpcUa/", StringComparison.OrdinalIgnoreCase))
            {
                path = path[7..];
            }

            path = path.Trim('/')
                .Replace("/", string.Empty, StringComparison.Ordinal)
                .Replace('-', '_')
                .Replace('+', '_');

            int colon = path.LastIndexOf(':');
            if (colon != -1)
            {
                path = path[(colon + 1)..];
            }

            // Remove invalid path characters
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                path = path.Replace(c, '_');
            }
            return path;
        }

        private readonly ILogger m_logger;
        private readonly Dictionary<string, NodesetFile> m_nodesets = [];
    }
}
