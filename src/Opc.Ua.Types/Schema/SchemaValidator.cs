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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// A base class for schema validators. This class handles loading of files
    /// into a validation. Files are loaded from a file system abstraction. For
    /// backward compatibilty also via a import table (to be removed eventually).
    /// If the file is not found it is searched next to the validated file. If
    /// it is still not found, in the current directory. The constructor allows
    /// specifying an assembly that is to be used to load any resources if the
    /// file system does not contain them. If not specified the currently executing
    /// assembly is used.
    /// </summary>
    public class SchemaValidator
    {
        /// <summary>
        /// Create schema validator
        /// </summary>
        /// <param name="fileSystem">File sytem to use.</param>
        /// <param name="namespaceUriToLocationMapping">
        /// A table of known namespace uris to location mappings. A location
        /// is treated as a file path or resource path during load.
        /// </param>
        /// <param name="importFiles">Additional in memory files</param>
        public SchemaValidator(
            IFileSystem? fileSystem,
            IDictionary<string, string>? namespaceUriToLocationMapping = null,
            IReadOnlyDictionary<string, byte[]>? importFiles = null)
        {
            FileSystem = fileSystem ?? LocalFileSystem.Instance;
            m_namespaceUriToLocationMapping = namespaceUriToLocationMapping ??
                new Dictionary<string, string>();
            LoadedFiles = new Dictionary<string, object>();
            m_importFiles = importFiles ?? new Dictionary<string, byte[]>();
        }

        /// <summary>
        /// The file that is being validated.
        /// </summary>
        public string? FilePath { get; private set; }

        /// <summary>
        /// The file system the validator should use
        /// </summary>
        protected IFileSystem FileSystem { get; }

        /// <summary>
        /// A table of files which have been loaded. Concrete implementations
        /// can use this to avoid loading the same file multiple times.
        /// </summary>
        protected IDictionary<string, object> LoadedFiles { get; }

        /// <summary>
        /// Loads an input file for validation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        protected T LoadInput<T>(Stream stream)
        {
            LoadedFiles.Clear();
            T schema = LoadInternal<T>(stream);
            FilePath = null;
            return schema;
        }

        /// <summary>
        /// Loads an input file for validation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        protected T LoadInput<T>(string path)
        {
            LoadedFiles.Clear();
            T schema = LoadInternal<T>(path);
            FilePath = path;

            return schema;
        }

        /// <summary>
        /// Loads the specified type from either a namespace uri or a path.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="FileNotFoundException"></exception>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        protected T Load<T>(string? path, string? namespaceUri)
        {
            if (namespaceUri != null)
            {
                // check if already loaded.
                if (LoadedFiles.TryGetValue(namespaceUri, out object? value) &&
                    value is T result)
                {
                    return result;
                }

                // check if namespace specified in the import table.
                if (m_importFiles.TryGetValue(namespaceUri, out byte[]? schema))
                {
                    using Stream memoryStream = new MemoryStream(schema);
                    return LoadInternal<T>(memoryStream);
                }

                // check if path specified in the file table.
                if (m_namespaceUriToLocationMapping.TryGetValue(namespaceUri, out string? location) &&
                    FileSystem.Exists(location))
                {
                    return LoadInternal<T>(location);
                }
            }
            if (string.IsNullOrEmpty(path))
            {
                throw Exception(
                    "Cannot import namespace '{0}' from '{1}'.",
                    namespaceUri,
                    path);
            }
            return Load<T>(path!);
        }

        /// <summary>
        /// Load typed resource either from file or as fallback from embedded resource
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="FileNotFoundException"></exception>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        protected T Load<T>(string path)
        {
            using Stream stream = OpenRead(path);
            return LoadInternal<T>(stream);
        }

        /// <summary>
        /// Load a schema from file system or as fallback from embedded resource.
        /// Will lookup alternative path if namespace uri mapping is defined.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        protected XmlSchema Load(string? path, string? namespaceUri, ValidationEventHandler handler)
        {
            if (namespaceUri != null)
            {
                // An explicit namespace-to-location mapping (trusted
                // configuration) takes precedence and overrides any
                // supplied location.
                if (m_namespaceUriToLocationMapping.TryGetValue(namespaceUri, out string? location))
                {
                    path = location;
                }
                // Otherwise prefer an in-memory import for the namespace over a
                // (potentially untrusted) schema location before falling back
                // to the file system. This prevents a peer-supplied import
                // location from being resolved against the local file system
                // when the namespace can be satisfied from memory.
                else if (m_importFiles.TryGetValue(namespaceUri, out byte[]? schemaBuffer))
                {
                    using var istrm = new MemoryStream(schemaBuffer);
                    return Load(istrm, handler);
                }

                if (path == null)
                {
                    throw new FileNotFoundException(CoreUtils.Format(
                        "Missing schema location for namespace {0}", namespaceUri));
                }
            }

            return Load(path!, handler);
        }

        /// <summary>
        /// Load a schema from file system or as fallback from embedded resource
        /// </summary>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        private XmlSchema Load(string path, ValidationEventHandler handler)
        {
            using Stream stream = OpenRead(path);
            return Load(stream, handler);
        }

        /// <summary>
        /// Adds the embedded resources to the file table.
        /// </summary>
        protected void AddWellKnownFiles(IReadOnlyDictionary<string, string>? resources)
        {
            if (resources != null)
            {
                foreach (KeyValuePair<string, string> resource in resources)
                {
                    m_namespaceUriToLocationMapping.TryAdd(resource.Key, resource.Value);
                }
            }
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public virtual string? GetSchema(string? typeName)
        {
            return null;
        }

        /// <summary>
        /// Returns true if the QName is null.
        /// </summary>
        protected static bool IsNull([NotNullWhen(false)] XmlQualifiedName? name)
        {
            return name == null || string.IsNullOrEmpty(name.Name);
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object? arg1)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1!));
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object? arg1, object? arg2)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1!, arg2!));
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object? arg1, object? arg2, object? arg3)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1!, arg2!, arg3!));
        }

        /// <summary>
        /// Try to get a file stream from the file system with various fallbacks
        /// with regards to the path of the file pointed to by path. Will throw
        /// if the file cannot be found or path is empty
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Path empty or null</exception>
        /// <exception cref="FileNotFoundException">file was not found</exception>
        protected Stream OpenRead(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(
                    nameof(path),
                    "Path is null or empty. Cannot load.");
            }

            // 1. try to load from path
            if (FileSystem.Exists(path))
            {
                return FileSystem.OpenRead(path);
            }

            // 2. try to load as side by side to file being validated
            string file = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(FilePath))
            {
                // FilePath is the file under validation, so its directory is
                // what "side by side" means - combining with the file path
                // itself produced a path that can never exist.
                string? directory = Path.GetDirectoryName(Path.GetFullPath(FilePath!));
                if (!string.IsNullOrEmpty(directory))
                {
                    path = Path.Combine(directory!, file);
                    if (FileSystem.Exists(path))
                    {
                        return FileSystem.OpenRead(path);
                    }
                }
            }

            // 3. try load from current folder
            path = Path.Combine(Directory.GetCurrentDirectory(), file);
            if (FileSystem.Exists(path))
            {
                return FileSystem.OpenRead(path);
            }

            throw new FileNotFoundException(
                "File not found, failed to load type or schema.", path);
        }

        /// <summary>
        /// Load a schema from a stream
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        protected static XmlSchema Load(Stream stream, ValidationEventHandler handler)
        {
            try
            {
                using var reader = new StreamReader(stream);
                using var xmlReader = new DepthLimitedXmlReader(
                    XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings()),
                    MaxSchemaElementDepth);
                return XmlSchema.Read(xmlReader, handler)!;
            }
            catch (Exception e)
            {
                throw new FileNotFoundException("Could not load schema.", e);
            }
        }

        /// <summary>
        /// The deepest element nesting accepted in an XML schema. The schema
        /// compiler processes nested particles recursively, so the depth of a
        /// schema is bounded while it is read, before it is compiled.
        /// </summary>
        internal const int MaxSchemaElementDepth = 128;

        /// <summary>
        /// Reads through another reader and fails with an <see cref="XmlException"/>
        /// when a node is nested deeper than the limit.
        /// </summary>
        private sealed class DepthLimitedXmlReader : XmlReader, IXmlLineInfo
        {
            public DepthLimitedXmlReader(XmlReader inner, int maxDepth)
            {
                m_inner = inner;
                m_maxDepth = maxDepth;
            }

            public override bool Read()
            {
                if (!m_inner.Read())
                {
                    return false;
                }
                if (m_inner.Depth > m_maxDepth)
                {
                    throw new XmlException(CoreUtils.Format(
                        "The schema nests elements deeper than {0} levels.",
                        m_maxDepth));
                }
                return true;
            }

            public override int AttributeCount => m_inner.AttributeCount;
            public override string BaseURI => m_inner.BaseURI;
            public override int Depth => m_inner.Depth;
            public override bool EOF => m_inner.EOF;
            public override bool HasValue => m_inner.HasValue;
            public override bool IsDefault => m_inner.IsDefault;
            public override bool IsEmptyElement => m_inner.IsEmptyElement;
            public override string LocalName => m_inner.LocalName;
            public override string Name => m_inner.Name;
            public override string NamespaceURI => m_inner.NamespaceURI;
            public override XmlNameTable NameTable => m_inner.NameTable;
            public override XmlNodeType NodeType => m_inner.NodeType;
            public override string Prefix => m_inner.Prefix;
            public override char QuoteChar => m_inner.QuoteChar;
            public override ReadState ReadState => m_inner.ReadState;
            public override string Value => m_inner.Value;
            public override string XmlLang => m_inner.XmlLang;
            public override XmlSpace XmlSpace => m_inner.XmlSpace;
            public override XmlReaderSettings? Settings => m_inner.Settings;

            public override string GetAttribute(int i)
            {
                return m_inner.GetAttribute(i);
            }

            public override string? GetAttribute(string name)
            {
                return m_inner.GetAttribute(name);
            }

            public override string? GetAttribute(string name, string? namespaceURI)
            {
                return m_inner.GetAttribute(name, namespaceURI);
            }

            public override string? LookupNamespace(string prefix)
            {
                return m_inner.LookupNamespace(prefix);
            }

            public override void MoveToAttribute(int i)
            {
                m_inner.MoveToAttribute(i);
            }

            public override bool MoveToAttribute(string name)
            {
                return m_inner.MoveToAttribute(name);
            }

            public override bool MoveToAttribute(string name, string? ns)
            {
                return m_inner.MoveToAttribute(name, ns);
            }

            public override bool MoveToElement()
            {
                return m_inner.MoveToElement();
            }

            public override bool MoveToFirstAttribute()
            {
                return m_inner.MoveToFirstAttribute();
            }

            public override bool MoveToNextAttribute()
            {
                return m_inner.MoveToNextAttribute();
            }

            public override bool ReadAttributeValue()
            {
                return m_inner.ReadAttributeValue();
            }

            public override void ResolveEntity()
            {
                m_inner.ResolveEntity();
            }

            public bool HasLineInfo()
            {
                return m_inner is IXmlLineInfo info && info.HasLineInfo();
            }

            public int LineNumber => m_inner is IXmlLineInfo info ? info.LineNumber : 0;

            public int LinePosition => m_inner is IXmlLineInfo info ? info.LinePosition : 0;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_inner.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly XmlReader m_inner;
            private readonly int m_maxDepth;
        }

        /// <summary>
        /// Loads a type from a location
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        private T LoadInternal<T>(string path)
        {
            using Stream stream = FileSystem.OpenRead(path);
            return LoadInternal<T>(stream);
        }

        /// <summary>
        /// Deserializes a type t from a stream
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [RequiresUnreferencedCode("Uses XmlSerializer which requires unreferenced code.")]
        [RequiresDynamicCode("Uses XmlSerializer which requires unreferenced code.")]
        private static T LoadInternal<T>(Stream stream)
        {
            using var reader = new StreamReader(stream);
            using var xmlReader = XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings());
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(xmlReader)!;
        }

        private readonly IDictionary<string, string> m_namespaceUriToLocationMapping;
        private readonly IReadOnlyDictionary<string, byte[]> m_importFiles;
    }
}
