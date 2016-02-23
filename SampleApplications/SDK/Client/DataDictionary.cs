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
using System.Runtime.Serialization;
using System.IO;

using Opc.Ua.Schema;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A class that holds the configuration for a UA service.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix"), DataContract(Namespace = Namespaces.OpcUaXsd)]
	public class DataDictionary : ApplicationConfiguration
	{
		#region Constructors
		/// <summary>
		/// The default constructor.
		/// </summary>
		public DataDictionary(Session session)
		{
			Initialize();

            m_session = session;
		}

		/// <summary>
		/// Sets private members to default values.
		/// </summary>
		private void Initialize()
		{
            m_session        = null;
            m_datatypes      = new Dictionary<NodeId,ReferenceDescription>();
            m_validator      = null;
            m_typeSystemId   = null;
            m_typeSystemName = null;
            m_dictionaryId   = null;
            m_name           = null;
		}
		#endregion

		#region Public Interface
        /// <summary>
        /// The node id for the dictionary.
        /// </summary>
        public NodeId DictionaryId
        {
            get 
            { 
                return m_dictionaryId;
            }
        }

        /// <summary>
        /// The display name for the dictionary.
        /// </summary>
        public string Name
        {
            get 
            { 
                return m_name;
            }
        }

        /// <summary>
        /// The node id for the type system.
        /// </summary>
        public NodeId TypeSystemId
        {
            get 
            { 
                return m_typeSystemId;
            }
        }

        /// <summary>
        /// The display name for the type system.
        /// </summary>
        public string TypeSystemName
        {
            get 
            { 
                return m_typeSystemName;
            }
        }

        /// <summary>
        /// Loads the dictionary idetified by the node id.
        /// </summary>
        public async Task Load(ReferenceDescription dictionary)
        {
            if (dictionary == null) throw new ArgumentNullException("dictionary");

            NodeId dictionaryId = ExpandedNodeId.ToNodeId(dictionary.NodeId, m_session.NamespaceUris);

            GetTypeSystem(dictionaryId);

            byte[] schema = ReadDictionary(dictionaryId);

            if (schema == null || schema.Length == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Cannot parse empty data dictionary.");
            }

            await Validate(schema);

            ReadDataTypes(dictionaryId);

            m_dictionaryId = dictionaryId;
            m_name = dictionary.ToString();
        }

        /// <summary>
        /// Returns true if the dictionary contains the data type description;
        /// </summary>
        public bool Contains(NodeId descriptionId)
        {
            return m_datatypes.ContainsKey(descriptionId);
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire dictionary if null).
        /// </summary>
        public string GetSchema(NodeId descriptionId)
        {
            ReferenceDescription description = null;

            if (descriptionId != null)
            {
                if (!m_datatypes.TryGetValue(descriptionId, out description))
                {
                    return null;
                }

                return m_validator.GetSchema(description.BrowseName.Name);
            }

            return m_validator.GetSchema(null);
        }
		#endregion
        
		#region Private Members
        /// <summary>
        /// Retrieves the type system for the dictionary.
        /// </summary>
        private void GetTypeSystem(NodeId dictionaryId)
        {                    
            Browser browser = new Browser(m_session);
            
            browser.BrowseDirection = BrowseDirection.Inverse;
            browser.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            browser.IncludeSubtypes = false;
            browser.NodeClassMask   = 0;

            ReferenceDescriptionCollection references = browser.Browse(dictionaryId);
                                   
            if (references.Count > 0)
            {   
                m_typeSystemId = ExpandedNodeId.ToNodeId(references[0].NodeId, m_session.NamespaceUris);
                m_typeSystemName = references[0].ToString();
            }
        }
        
        /// <summary>
        /// Retrieves the data types in the dictionary.
        /// </summary>
        private void ReadDataTypes(NodeId dictionaryId)
        {                    
            Browser browser = new Browser(m_session);
            
            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            browser.IncludeSubtypes = false;
            browser.NodeClassMask   = 0;

            ReferenceDescriptionCollection references = browser.Browse(dictionaryId);
                                   
            foreach (ReferenceDescription reference in references)
            {
                NodeId datatypeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                
                if (datatypeId != null)
                {
                    m_datatypes[datatypeId] = reference;
                }
            }
        }

        /// <summary>
        /// Reads the contents of a data dictionary.
        /// </summary>
        private byte[] ReadDictionary(NodeId dictionaryId)
        {
            // create item to read.
            ReadValueId itemToRead = new ReadValueId();

            itemToRead.NodeId       = dictionaryId;
            itemToRead.AttributeId  = Attributes.Value;
            itemToRead.IndexRange   = null;
            itemToRead.DataEncoding = null;
            
            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();
            itemsToRead.Add(itemToRead);
                        
            // read value.
            DataValueCollection values;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);      
      
            // check for error.
            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = ClientBase.GetResult(values[0].StatusCode, 0, diagnosticInfos, responseHeader);
                throw new ServiceResultException(result);
            }

            // return as a byte array.
            return values[0].Value as byte[];
        }

        /// <summary>
        /// Validates the type dictionary.
        /// </summary>
        /// <param name="dictionary"></param>
        private async Task Validate(byte[] dictionary)
        {
            MemoryStream istrm = new MemoryStream(dictionary);

            if (m_typeSystemId == Objects.XmlSchema_TypeSystem)
            {
                Schema.Xml.XmlSchemaValidator validator = new Schema.Xml.XmlSchemaValidator();

                try
                {
                    validator.Validate(istrm);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not validate schema.");
                }

                m_validator = validator;
            }

            if (m_typeSystemId == Objects.OPCBinarySchema_TypeSystem)
            {
                Schema.Binary.BinarySchemaValidator validator = new Schema.Binary.BinarySchemaValidator();

                try
                {
                    await validator.Validate(istrm);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not validate schema.");
                }

                m_validator = validator;
            }
        }
        #endregion

        #region Private Members
        private Session m_session;
        private NodeId m_dictionaryId;
        private string m_name;
        private NodeId m_typeSystemId;
        private string m_typeSystemName;
        private Dictionary<NodeId,ReferenceDescription> m_datatypes;
        private SchemaValidator m_validator;
		#endregion
	}
}
