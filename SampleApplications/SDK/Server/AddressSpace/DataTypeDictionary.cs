/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Xml;
using System.Runtime.Serialization;
using System.Reflection;
using System.IO;

namespace Opc.Ua.Server
{
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// A variable that contains live automation data.
    /// </summary>
    [Obsolete("The DataTypeDictionary class is obsolete and is not supported. See Opc.Ua.DataTypeDictionaryState for a replacement.")]
    public partial class DataTypeDictionary 
    {
        #region Public Methods
        /// <summary>
        /// Sets an embedded resource.
        /// </summary>
        public void SetDictionarySource(Assembly assembly, string resourcePath, string namespaceUri)
        {          
            SpecifyDataTypeVersion(Property<string>.Construct(Server)); 
            SpecifyNamespaceUri(Property<string>.Construct(Server));

            lock (DataLock)
            {
                m_assembly = assembly;
                m_filePath = resourcePath;
                MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
                UpdateValue(null, StatusCodes.BadWaitingForInitialData);
                
                DataTypeVersion.Value = Utils.Format("{0}", m_assembly.GetName().Version);
                NamespaceUri.Value = namespaceUri;
            }
        }

        /// <summary>
        /// Sets a file as the dictionary source and specifies a minimum interval between scans.
        /// </summary>
        public void SetDictionarySource(string fileName, string searchPath, int minimumScanRate)
        {
            lock (DataLock)
            {
                // construct the filepath.
                FileInfo fileInfo = new FileInfo(fileName);

                if (!fileInfo.Exists)
                {
                    if (String.IsNullOrEmpty(searchPath))
                    {
                        searchPath = GetDefaultHttpPath();
                    }
                    
                    if (!String.IsNullOrEmpty(searchPath))
                    {
                        if (searchPath.StartsWith(Uri.UriSchemeHttp))
                        {          
                            if (!searchPath.EndsWith("/"))
                            {
                                searchPath += "/";
                            }              

                            fileName = String.Format("{0}{1}", searchPath, fileName);
                        }
                        else
                        {
                            if (!searchPath.EndsWith("\\"))
                            {
                                searchPath += "\\";
                            }       

                            fileName = String.Format("{0}{1}", searchPath, fileName);
                        }
                    }
                }

                m_filePath = fileName;
                MinimumSamplingInterval = minimumScanRate;
                UpdateValue(null, StatusCodes.BadWaitingForInitialData);
            }
        }
        #endregion
               
		#region I/O Support Functions
        /// <summary>
        /// Returns the data type path for the DataTypeDescription.
        /// </summary>
        protected virtual string GetDataTypePath(string typeName)
        {
            string path = typeName;

            if (m_typeSystemId == Objects.XmlSchema_TypeSystem)
            {
                path = String.Format("//xs:element[@name='{0}']", typeName);
            }

            return path;
        }

        /// <summary>
        /// Returns the data dictionary from cache or disk (updates the data type version as well).
        /// </summary>
        protected virtual byte[] ReadDictionary(double maxAge)
        {
            byte[] dictionary = Value;

            // return cached value.
            if (dictionary != null)
            {
                if (Timestamp.AddMilliseconds(MinimumSamplingInterval) > DateTime.UtcNow)
                {
                    return dictionary;
                }
                
                if (Timestamp.AddMilliseconds(maxAge) > DateTime.UtcNow)
                {
                    return dictionary;
                }
                
                if (m_assembly != null)
                {
                    return dictionary;
                }
            }
            
            // read from source.
            try
            {
                if (m_assembly != null)
                {
                    dictionary = ReadDictionaryFromResource(m_filePath);
                }
                else if (m_filePath.StartsWith(Uri.UriSchemeHttp))
                {
                    dictionary = ReadDictionaryFromHttpFile(m_filePath);
                }
                else
                {
                    dictionary = ReadDictionaryFromDiskFile(m_filePath);
                }

                return dictionary;
            }
            catch (Exception e)
            {
                ServiceResult error = ServiceResult.Create(e, StatusCodes.BadOutOfService, "Could not access data dictionary file: {0}.", m_filePath);
                UpdateStatus(error);
                return null;
            }
        }          

        /// <summary>
        /// Returns the default HTTP path to use from the hosting process.
        /// </summary>
        protected virtual string GetDefaultHttpPath()
        {     
            foreach (Uri endpointAddress in Server.EndpointAddresses)
            {
                if (!endpointAddress.Scheme.StartsWith(Uri.UriSchemeHttp))
                {
                    continue;
                }

                string url = endpointAddress.ToString();

                int index = url.LastIndexOf('/');

                if (index != -1)
                {
                    url = url.Substring(0, index+1);
                }

                return url;
            }

            return null;
        }
                
        /// <summary>
        /// Loads a file from a HTTP URL.
        /// </summary>
        protected virtual byte[] ReadDictionaryFromHttpFile(string url)
        {                
            System.Net.WebRequest request = System.Net.WebRequest.Create(url);
            System.Net.WebResponse response = request.GetResponse();
            Stream istrm = response.GetResponseStream();
            
            byte[] dictionary = ReadDictionary(istrm);
            
            // use the current time as the dictionary version.
            m_dataTypeVersion.Value = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);

            return dictionary;
        }
        
        /// <summary>
        /// Reads the dictionary from a disk file.
        /// </summary>
        protected virtual byte[] ReadDictionaryFromDiskFile(string filePath)
        {
            Stream istrm = File.Open(filePath, FileMode.Open);
            
            byte[] dictionary = ReadDictionary(istrm);
            
            // use the file modification time as the dictionary version.
            m_dataTypeVersion.Value = XmlConvert.ToString(new FileInfo(filePath).LastWriteTimeUtc, XmlDateTimeSerializationMode.Utc);

            return dictionary;
        }
        
        /// <summary>
        /// Reads the dictionary from an embedded resource.
        /// </summary>
        protected virtual byte[] ReadDictionaryFromResource(string filePath)
        {
            Stream istrm = m_assembly.GetManifestResourceStream(filePath);
            
            byte[] dictionary = ReadDictionary(istrm);
            
            // use the assembly version as the version.
            DataTypeVersion.Value = Utils.Format("{0}", m_assembly.GetName().Version);

            return dictionary;
        }

        /// <summary>
        /// Reads the dictionary from the stream.
        /// </summary>
        protected virtual byte[] ReadDictionary(Stream istrm)
        {
            BinaryReader reader = new BinaryReader(istrm);
            MemoryStream ostrm  = new MemoryStream();

            try
            {
                byte[] buffer = new byte[4096];

                while (true)
                {
                    int bytesRead = reader.Read(buffer, 0, buffer.Length);
                    
                    if (bytesRead <= 0)
                    {
                        break;
                    }
                        
                    ostrm.Write(buffer, 0, bytesRead);
                }

                ostrm.Close();
                return ostrm.ToArray();
            }
            finally
            {
                reader.Close();
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Looks up the type system id.
        /// </summary>
        protected override void OnAfterCreate(object configuration)
        {
            base.OnAfterCreate(configuration);

            m_typeSystemId = NodeManager.FindTargetId(
                this.NodeId,
                ReferenceTypeIds.Organizes,
                true,
                null);
        }
        #endregion

        #region Private Fields
        private NodeId m_typeSystemId;
        private Assembly m_assembly;
        private string m_filePath;
        #endregion
    }
#endif
}
