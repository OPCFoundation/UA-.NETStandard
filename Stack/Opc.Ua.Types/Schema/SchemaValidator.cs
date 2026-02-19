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
            IFileSystem fileSystem,
            IDictionary<string, string> namespaceUriToLocationMapping = null,
            IReadOnlyDictionary<string, byte[]> importFiles = null)
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
        public string FilePath { get; private set; }

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
        protected T Load<T>(string path, string namespaceUri)
        {
            if (namespaceUri != null)
            {
                // check if already loaded.
                if (LoadedFiles.TryGetValue(namespaceUri, out object value) &&
                    value is T result)
                {
                    return result;
                }

                // check if namespace specified in the import table.
                if (m_importFiles.TryGetValue(namespaceUri, out byte[] schema))
                {
                    using Stream memoryStream = new MemoryStream(schema);
                    return LoadInternal<T>(memoryStream);
                }

                // check if path specified in the file table.
                if (m_namespaceUriToLocationMapping.TryGetValue(namespaceUri, out string location) &&
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
            return Load<T>(path);
        }

        /// <summary>
        /// Load typed resource either from file or as fallback from embedded resource
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="FileNotFoundException"></exception>
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
        protected XmlSchema Load(string path, string namespaceUri, ValidationEventHandler handler)
        {
            // check if path specified in the file table.
            if (namespaceUri != null)
            {
                // check if path specified in the file table.
                if (m_namespaceUriToLocationMapping.TryGetValue(namespaceUri, out string location))
                {
                    path = location;
                }

                if (path == null)
                {
                    if (m_importFiles.TryGetValue(namespaceUri, out byte[] schemaBuffer))
                    {
                        using var istrm = new MemoryStream(schemaBuffer);
                        return Load(istrm, handler);
                    }
                    throw new FileNotFoundException(CoreUtils.Format(
                        "Missing schema location for namespace {0}", namespaceUri));
                }
            }

            return Load(path, handler);
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
        protected void AddWellKnownFiles(IReadOnlyDictionary<string, string> resources)
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
        public virtual string GetSchema(string typeName)
        {
            return null;
        }

        /// <summary>
        /// Returns true if the QName is null.
        /// </summary>
        protected static bool IsNull([NotNullWhen(false)] XmlQualifiedName name)
        {
            return name == null || string.IsNullOrEmpty(name.Name);
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1));
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1, object arg2)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1, arg2));
        }

        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1, object arg2, object arg3)
        {
            return new InvalidOperationException(CoreUtils.Format(format, arg1, arg2, arg3));
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
                path = Path.Combine(Path.GetFullPath(FilePath), file);
                if (FileSystem.Exists(path))
                {
                    return FileSystem.OpenRead(path);
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
                using var xmlReader = XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings());
                return XmlSchema.Read(xmlReader, handler);
            }
            catch (Exception e)
            {
                throw new FileNotFoundException("Could not load schema.", e);
            }
        }

        /// <summary>
        /// Loads a type from a location
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private T LoadInternal<T>(string path)
        {
            using Stream stream = FileSystem.OpenRead(path);
            return LoadInternal<T>(stream);
        }

        /// <summary>
        /// Deserializes a type t from a stream
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static T LoadInternal<T>(Stream stream)
        {
            using var reader = new StreamReader(stream);
            using var xmlReader = XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings());
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(xmlReader);
        }

        private readonly IDictionary<string, string> m_namespaceUriToLocationMapping;
        private readonly IReadOnlyDictionary<string, byte[]> m_importFiles;
    }
}
