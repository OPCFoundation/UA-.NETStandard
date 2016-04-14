/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Reflection;
using System.Globalization;
using Windows.Storage;
using System.Threading.Tasks;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// A base class for schema validators.
    /// </summary>
    public class SchemaValidator
    {       
        #region Constructors
		/// <summary>
		/// Intializes the object with default values.
		/// </summary>
		public SchemaValidator()
		{
            m_knownFiles  = new Dictionary<string,string>();
            m_loadedFiles = new Dictionary<string,object>();
		}

		/// <summary>
		/// Intializes the object with a file table.
		/// </summary>
		public SchemaValidator(Dictionary<string,string> knownFiles)
		{
            m_knownFiles  = knownFiles;
            m_loadedFiles = new Dictionary<string,object>();

            if (m_knownFiles == null)
            {
                m_knownFiles = new Dictionary<string,string>();
            }
		}
        #endregion      
        
        #region Public Properties
        /// <summary>
        /// The file that was validated.
        /// </summary>
        public string FilePath
        {
            get { return m_inputPath; }
        }         
        
        /// <summary>
        /// A table of known files.
        /// </summary>
        public IDictionary<string,string> KnownFiles
        {
            get { return m_knownFiles; }
        }         
        
        /// <summary>
        /// A table of files which have been loaded.
        /// </summary>
        public IDictionary<string,object> LoadedFiles
        {
            get { return m_loadedFiles; }
        }         
        #endregion

        #region Protected Methods
        /// <summary>
        /// Returns true if the QName is null,
        /// </summary>
        protected static bool IsNull(XmlQualifiedName name)
        {
            if (name != null && !String.IsNullOrEmpty(name.Name))
            {
                return false;
            }

            return true;
        }
                
        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format)
        {
            throw new FormatException(format);
        }
        
        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1)
        {
            return new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, format, arg1));
        }
        
        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1, object arg2)
        {
            return new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, format, arg1, arg2));
        }
        
        /// <summary>
        /// Formats a string and throws an exception.
        /// </summary>
        protected static Exception Exception(string format, object arg1, object arg2, object arg3)
        {
            return new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, format, arg1, arg2, arg3));
        }
        
        /// <summary>
        /// Loads an input file for validation.
        /// </summary>
        protected object LoadInput(System.Type type, Stream stream)
        {
            m_loadedFiles.Clear();

            object schema = LoadFile(type, stream);

            m_inputPath = null;

            return schema;
        }
        
        /// <summary>
        /// Loads an input file for validation.
        /// </summary>
        protected object LoadInput(System.Type type, string path)
        {
            m_loadedFiles.Clear();

            object schema = LoadFile(type, path);

            m_inputPath = path;

            return schema;
        }

        /// <summary>
        /// Loads the dictionary from a file.
        /// </summary>
        protected async Task<object> Load(System.Type type, string namespaceUri, string path)
        {
            // check if already loaded.
            if (m_loadedFiles.ContainsKey(namespaceUri))
            {
                return m_loadedFiles[namespaceUri];
            }

            // check if a valid path provided.
            StorageFile fileInfo = null;

            if (!String.IsNullOrEmpty(path))
            {
                fileInfo = await StorageFile.GetFileFromPathAsync(path);
                return LoadFile(type, path);
            }

            // check if path specified in the file table.
            string location = null;

            if (m_knownFiles.TryGetValue(namespaceUri, out location))
            {
                try
                {
                    fileInfo = await StorageFile.GetFileFromPathAsync(location);
                    return LoadFile(type, location);
                }
                catch (Exception)
                {
                    // load embedded resource.
                    return LoadResource(type, location, null);
                }
            }

            //check for file in the same directory as the input file.
            try
            {
                fileInfo = await StorageFile.GetFileFromPathAsync(m_inputPath + "\\" + fileInfo.Name);
                return LoadFile(type, fileInfo.Path);
            }
            catch (Exception)
            {
                // check for file in the process directory.
                fileInfo = await StorageFile.GetFileFromPathAsync(Windows.Storage.ApplicationData.Current.LocalFolder + "\\" + fileInfo.Name);
                return LoadFile(type, fileInfo.Path);
            }            
            
            throw Exception("Cannot import file '{0}' from '{1}'.", namespaceUri, path);    
        }

        /// <summary>
        /// Loads a schema from a file.
        /// </summary>
        protected static async Task<object> LoadFile(System.Type type, string path)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            StreamReader reader = new StreamReader(await file.OpenStreamForReadAsync());

            try
            {
                XmlSerializer serializer = new XmlSerializer(type);
                return serializer.Deserialize(reader);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Loads a schema from a file.
        /// </summary>
        protected static object LoadFile(System.Type type, Stream stream)
        {
	        StreamReader reader = new StreamReader(stream);

            try
            {
                XmlSerializer serializer = new XmlSerializer(type);
                return serializer.Deserialize(reader);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Loads a schema from an embedded resource.
        /// </summary>
        protected static object LoadResource(System.Type type, string path, Assembly assembly)
        {
            try
            {
                StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(path));

                try
                {
                    XmlSerializer serializer = new XmlSerializer(type);
                    return serializer.Deserialize(reader);
                }
                finally
                {
                    reader.Dispose();
                }
            }
            catch (Exception e)
            {
                throw new FileNotFoundException(String.Format(CultureInfo.InvariantCulture, "Could not load resource '{0}'.", path), e);
            }
        }

        /// <summary>
        /// Adds the embedded resources to the file table.
        /// </summary>
        protected void SetResourcePaths(string[][] resources)
        {
            if (resources != null)
            {
                for (int ii = 0; ii < resources.Length; ii++)
                {
                    if (!m_knownFiles.ContainsKey(resources[ii][0]))
                    {
                        m_knownFiles.Add(resources[ii][0], resources[ii][1]);
                    }
                }
            }
        }
        #endregion
                
        #region Public Methods
        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public virtual string GetSchema(string typeName)
        {
            return null;
        } 
        #endregion

        #region Private Fields
        private string m_inputPath;
        private Dictionary<string,string> m_knownFiles; 
        private Dictionary<string,object> m_loadedFiles; 
        #endregion
    }
}
