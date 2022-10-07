/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Opc.Ua.Schema;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A class that holds the configuration for a UA service.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix"), DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class DataDictionary
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public DataDictionary(ISession session)
        {
            Initialize();
            m_session = session;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_session = null;
            DataTypes = new Dictionary<NodeId, QualifiedName>();
            m_validator = null;
            TypeSystemId = null;
            TypeSystemName = null;
            DictionaryId = null;
            Name = null;
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// The node id for the dictionary.
        /// </summary>
        public NodeId DictionaryId { get; private set; }

        /// <summary>
        /// The display name for the dictionary.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The node id for the type system.
        /// </summary>
        public NodeId TypeSystemId { get; private set; }

        /// <summary>
        /// The display name for the type system.
        /// </summary>
        public string TypeSystemName { get; private set; }

        /// <summary>
        /// The type dictionary.
        /// </summary>
        public Schema.Binary.TypeDictionary TypeDictionary { get; private set; }

        /// <summary>
        /// The data type dictionary DataTypes
        /// </summary>
        public Dictionary<NodeId, QualifiedName> DataTypes { get; private set; }

        /// <summary>
        /// Loads the dictionary identified by the node id.
        /// </summary>
        public Task Load(INode dictionary)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }
            NodeId dictionaryId = ExpandedNodeId.ToNodeId(dictionary.NodeId, m_session.NamespaceUris);
            return Load(dictionaryId, dictionary.ToString());
        }

        /// <summary>
        /// Loads the dictionary identified by the node id.
        /// </summary>
        public async Task Load(NodeId dictionaryId, string name, byte[] schema = null, IDictionary<string, byte[]> imports = null)
        {
            if (dictionaryId == null)
            {
                throw new ArgumentNullException(nameof(dictionaryId));
            }

            GetTypeSystem(dictionaryId);

            if (schema == null || schema.Length == 0)
            {
                schema = ReadDictionary(dictionaryId);
            }

            if (schema == null || schema.Length == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Cannot parse empty data dictionary.");
            }

            // Interoperability: some server may return a null terminated dictionary string, adjust length
            int zeroTerminator = Array.IndexOf<byte>(schema, 0);
            if (zeroTerminator >= 0)
            {
                Array.Resize(ref schema, zeroTerminator);
            }

            await Validate(schema, imports).ConfigureAwait(false);

            ReadDataTypes(dictionaryId);

            DictionaryId = dictionaryId;
            Name = name;
        }

        /// <summary>
        /// Returns true if the dictionary contains the data type description;
        /// </summary>
        public bool Contains(NodeId descriptionId)
        {
            return DataTypes.ContainsKey(descriptionId);
        }

        /// <summary>
        /// Returns the schema for the specified type (returns the entire dictionary if null).
        /// </summary>
        public string GetSchema(NodeId descriptionId)
        {
            if (descriptionId != null)
            {
                if (!DataTypes.TryGetValue(descriptionId, out QualifiedName browseName))
                {
                    return null;
                }

                return m_validator.GetSchema(browseName.Name);
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
            var references = m_session.NodeCache.FindReferences(dictionaryId, ReferenceTypeIds.HasComponent, true, false);
            if (references.Count > 0)
            {
                TypeSystemId = ExpandedNodeId.ToNodeId(references[0].NodeId, m_session.NamespaceUris);
                TypeSystemName = references[0].ToString();
            }
        }

        /// <summary>
        /// Retrieves the data types in the dictionary.
        /// </summary>
        /// <remarks>
        /// In order to allow for fast Linq matching of dictionary
        /// QNames with the data type nodes, the BrowseName of
        /// the DataType node is replaced with Value string.
        /// </remarks>
        private void ReadDataTypes(NodeId dictionaryId)
        {
            IList<INode> references = m_session.NodeCache.FindReferences(dictionaryId, ReferenceTypeIds.HasComponent, false, false);
            IList<NodeId> nodeIdCollection = references.Select(node => ExpandedNodeId.ToNodeId(node.NodeId, m_session.NamespaceUris)).ToList();

            // read the value to get the names that are used in the dictionary
            m_session.ReadValues(nodeIdCollection, out DataValueCollection values, out IList<ServiceResult> errors);

            int ii = 0;
            foreach (var reference in references)
            {
                NodeId datatypeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                if (datatypeId != null)
                {
                    if (ServiceResult.IsGood(errors[ii]))
                    {
                        var dictName = (String)values[ii].Value;
                        DataTypes[datatypeId] = new QualifiedName(dictName, datatypeId.NamespaceIndex);
                    }
                    ii++;
                }
            }
        }

        /// <summary>
        /// Reads the contents of multiple data dictionaries.
        /// </summary>
        public static async Task<IDictionary<NodeId, byte[]>> ReadDictionaries(ISessionClientMethods session, IList<NodeId> dictionaryIds)
        {
            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();
            foreach (var nodeId in dictionaryIds)
            {
                // create item to read.
                ReadValueId itemToRead = new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = null,
                    DataEncoding = null
                };
                itemsToRead.Add(itemToRead);
            }

            // read values.
            ReadResponse readResponse = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            var result = new Dictionary<NodeId, byte[]>();

            int ii = 0;
            foreach (var nodeId in dictionaryIds)
            {
                // check for error.
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    ServiceResult sr = ClientBase.GetResult(values[ii].StatusCode, 0, diagnosticInfos, readResponse.ResponseHeader);
                    throw new ServiceResultException(sr);
                }

                // return as a byte array.
                result[nodeId] = values[ii].Value as byte[];
                ii++;
            }

            return result;
        }

        /// <summary>
        /// Reads the contents of a data dictionary.
        /// </summary>
        public byte[] ReadDictionary(NodeId dictionaryId)
        {
            // create item to read.
            ReadValueId itemToRead = new ReadValueId {
                NodeId = dictionaryId,
                AttributeId = Attributes.Value,
                IndexRange = null,
                DataEncoding = null
            };

            ReadValueIdCollection itemsToRead = new ReadValueIdCollection {
                itemToRead
            };

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
        /// <param name="dictionary">The encoded dictionary to validate.</param>
        /// <param name="throwOnError">Throw if an error occurred.</param>
        internal Task Validate(byte[] dictionary, bool throwOnError)
        {
            return Validate(dictionary, null, throwOnError);
        }

        /// <summary>
        /// Validates the type dictionary.
        /// </summary>
        /// <param name="dictionary">The encoded dictionary to validate.</param>
        /// <param name="imports">A table of imported namespace schemas.</param>
        /// <param name="throwOnError">Throw if an error occurred.</param>
        internal async Task Validate(byte[] dictionary, IDictionary<string, byte[]> imports = null, bool throwOnError = false)
        {
            MemoryStream istrm = new MemoryStream(dictionary);

            if (TypeSystemId == Objects.XmlSchema_TypeSystem)
            {
                var validator = new Schema.Xml.XmlSchemaValidator(imports);

                try
                {
                    validator.Validate(istrm);
                }
                catch (Exception e)
                {
                    if (throwOnError)
                    {
                        throw;
                    }
                    Utils.LogWarning(e, "Could not validate XML schema, error is ignored.");
                }

                m_validator = validator;
            }

            if (TypeSystemId == Objects.OPCBinarySchema_TypeSystem)
            {
                var validator = new Schema.Binary.BinarySchemaValidator(imports);
                try
                {
                    await validator.Validate(istrm).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (throwOnError)
                    {
                        throw;
                    }
                    Utils.LogWarning(e, "Could not validate binary schema, error is ignored.");
                }

                m_validator = validator;
                TypeDictionary = validator.Dictionary;
            }
        }
        #endregion

        #region Private Members
        private ISession m_session;
        private SchemaValidator m_validator;
        #endregion
    }
}
