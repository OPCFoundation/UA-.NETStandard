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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Entity responsible to configure a PubSub Application
    /// 
    /// It has methods for adding/removing configuration objects to a root <see cref="PubSubConfigurationDataType"/> object.
    /// When the root <see cref="PubSubConfigurationDataType"/> object is modified there are various events raised to allow reaction to configuration changes.
    /// Each child object from parent <see cref="PubSubConfigurationDataType"/> object has a configurationId associated to it and it can be used to alter configuration. 
    /// The configurationId can be obtained using the <see cref="UaPubSubConfigurator.FindIdForObject(object)"/> method.
    /// </summary>
    public class UaPubSubConfigurator
    {
        #region Private Fields
        /// <summary>
        /// Value of an uninitialized identifier.
        /// </summary>
        internal static uint InvalidId = 0;

        private object m_lock = new object();
        private PubSubConfigurationDataType m_pubSubConfiguration;
        private Dictionary<uint, object> m_idsToObjects;
        private Dictionary<object, uint> m_objectsToIds;
        private Dictionary<uint, PubSubState> m_idsToPubSubState;
        private Dictionary<uint, uint> m_idsToParentId;
        private uint m_nextId = 1;
        #endregion

        #region Public Events
        /// <summary>
        /// Event that is triggered when a published data set is added to the configurator
        /// </summary>
        public event EventHandler<PublishedDataSetEventArgs> PublishedDataSetAdded;

        /// <summary>
        /// Event that is triggered when a published data set is removed from the configurator
        /// </summary>
        public event EventHandler<PublishedDataSetEventArgs> PublishedDataSetRemoved;

        /// <summary>
        /// Event that is triggered when an extension field is added to a published data set
        /// </summary>
        public event EventHandler<ExtensionFieldEventArgs> ExtensionFieldAdded;

        /// <summary>
        /// Event that is triggered when an extension field is removed from a published data set
        /// </summary>
        public event EventHandler<ExtensionFieldEventArgs> ExtensionFieldRemoved;

        /// <summary>
        /// Event that is triggered when a connection is added to the configurator
        /// </summary>
        public event EventHandler<ConnectionEventArgs> ConnectionAdded;

        /// <summary>
        /// Event that is triggered when a connection is removed from the configurator
        /// </summary>
        public event EventHandler<ConnectionEventArgs> ConnectionRemoved;

        /// <summary>
        /// Event that is triggered when a WriterGroup is added to a connection
        /// </summary>
        public event EventHandler<WriterGroupEventArgs> WriterGroupAdded;

        /// <summary>
        /// Event that is triggered when a WriterGroup is removed from a connection
        /// </summary>
        public event EventHandler<WriterGroupEventArgs> WriterGroupRemoved;

        /// <summary>
        /// Event that is triggered when a ReaderGroup is added to a connection
        /// </summary>
        public event EventHandler<ReaderGroupEventArgs> ReaderGroupAdded;

        /// <summary>
        /// Event that is triggered when a ReaderGroup is removed from a connection
        /// </summary>
        public event EventHandler<ReaderGroupEventArgs> ReaderGroupRemoved;

        /// <summary>
        /// Event that is triggered when a DataSetWriter is added to a WriterGroup
        /// </summary>
        public event EventHandler<DataSetWriterEventArgs> DataSetWriterAdded;

        /// <summary>
        /// Event that is triggered when a DataSetWriter is removed from a WriterGroup
        /// </summary>
        public event EventHandler<DataSetWriterEventArgs> DataSetWriterRemoved;

        /// <summary>
        /// Event that is triggered when a DataSetreader is added to a ReaderGroup
        /// </summary>
        public event EventHandler<DataSetReaderEventArgs> DataSetReaderAdded;

        /// <summary>
        /// Event that is triggered when a DataSetreader is removed from a ReaderGroup
        /// </summary>
        public event EventHandler<DataSetReaderEventArgs> DataSetReaderRemoved;

        /// <summary>
        /// Event raised when the state of a configuration object is changed
        /// </summary>
        public event EventHandler<PubSubStateChangedEventArgs> PubSubStateChanged;

        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UaPubSubConfigurator"/>.
        /// </summary>
        public UaPubSubConfigurator()
        {
            m_idsToObjects = new Dictionary<uint, object>();
            m_objectsToIds = new Dictionary<object, uint>();
            m_idsToPubSubState = new Dictionary<uint, PubSubState>();
            m_idsToParentId = new Dictionary<uint, uint>();

            m_pubSubConfiguration = new PubSubConfigurationDataType();
            m_pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection();
            m_pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection();

            //remember configuration id 
            uint id = m_nextId++;
            m_objectsToIds.Add(m_pubSubConfiguration, id);
            m_idsToObjects.Add(id, m_pubSubConfiguration);
            m_idsToPubSubState.Add(id, GetInitialPubSubState(m_pubSubConfiguration));
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get reference to <see cref="PubSubConfigurationDataType"/> instance that maintains the configuration for this <see cref="UaPubSubConfigurator"/>.
        /// </summary>
        public PubSubConfigurationDataType PubSubConfiguration { get { return m_pubSubConfiguration; } }
        #endregion

        #region Public Methods - Find

        /// <summary>
        /// Search a configured <see cref="PublishedDataSetDataType"/> with the specified name and return it
        /// </summary>
        /// <param name="name">Name of the object to be found.
        /// Returns null if name was not found.</param>
        /// <returns></returns>
        public PublishedDataSetDataType FindPublishedDataSetByName(string name)
        {
            foreach(PublishedDataSetDataType publishedDataSet in m_pubSubConfiguration.PublishedDataSets)
            {
                if (name == publishedDataSet.Name)
                {
                    return publishedDataSet;
                }
            }
            return null;
        }


        /// <summary>
        /// Search objects in current configuration and return them
        /// </summary>
        /// <param name="id">Id of the object to be found.
        /// Returns null if id was not found.</param>
        /// <returns></returns>
        public object FindObjectById(uint id)
        {
            if (m_idsToObjects.ContainsKey(id))
            {
                return m_idsToObjects[id];
            }
            return null;
        }

        /// <summary>
        /// Search id for specified configuration object.
        /// </summary>
        /// <param name="configurationObject">The object whose id is searched.</param>
        /// <returns>Returns <see cref="UaPubSubConfigurator.InvalidId"/> if object was not found.</returns>
        public uint FindIdForObject(object configurationObject)
        {
            if (m_objectsToIds.ContainsKey(configurationObject))
            {
                return m_objectsToIds[configurationObject];
            }
            return InvalidId;
        }

        /// <summary>
        /// Search <see cref="PubSubState"/> for specified configuration object.
        /// </summary>
        /// <param name="configurationObject">The object whose <see cref="PubSubState"/> is searched.</param>
        /// <returns>Returns <see cref="PubSubState"/> if the object.</returns>
        public PubSubState FindStateForObject(object configurationObject)
        {
            uint id = FindIdForObject(configurationObject);
            if (m_idsToPubSubState.ContainsKey(id))
            {
                return m_idsToPubSubState[id];
            }
            return PubSubState.Error;
        }

        /// <summary>
        /// Search <see cref="PubSubState"/> for specified configuration object.
        /// </summary>
        /// <param name="id">The id  of the object which <see cref="PubSubState"/> is searched.</param>
        /// <returns>Returns <see cref="PubSubState"/> if the object.</returns>
        public PubSubState FindStateForId(uint id)
        {
            if (m_idsToPubSubState.ContainsKey(id))
            {
                return m_idsToPubSubState[id];
            }
            return PubSubState.Error;
        }
        /// <summary>
        /// Find the parent configuration object for a configuration object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <returns></returns>
        public object FindParentForObject(object configurationObject)
        {
            uint id = FindIdForObject(configurationObject);
            if (id != InvalidId && m_idsToParentId.ContainsKey(id))
            {
                uint parentId = m_idsToParentId[id];
                return FindObjectById(parentId);
            }
            return null;
        }

        /// <summary>
        /// Find children ids for specified object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <returns></returns>
        public List<uint> FindChildrenIdsForObject(object configurationObject)
        {
            uint parentId = FindIdForObject(configurationObject);

            List<uint> childrenIds = new List<uint>();
            if (parentId != InvalidId && m_idsToParentId.ContainsValue(parentId))
            {
                foreach (uint key in m_idsToParentId.Keys)
                {
                    if (m_idsToParentId[key] == parentId)
                    {
                        childrenIds.Add(key);
                    }
                }
            }
            return childrenIds;
        }
        #endregion

        #region Public Methods - LoadConfiguration
        /// <summary>
        /// Load the specified configuration 
        /// </summary>
        /// <param name="configFilePath"></param>
        /// <param name="replaceExisting"> flag that indicates if current configuration is overwritten</param>
        public void LoadConfiguration(string configFilePath, bool replaceExisting = true)
        {
            // validate input argument 
            if (configFilePath == null)
            {
                throw new ArgumentException(nameof(configFilePath));
            }
            if (!File.Exists(configFilePath))
            {
                throw new ArgumentException("The specified file {0} does not exist", configFilePath);
            }
            PubSubConfigurationDataType pubSubConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configFilePath);

            LoadConfiguration(pubSubConfiguration, replaceExisting);
        }

        /// <summary>
        /// Load the specified configuration 
        /// </summary>
        /// <param name="pubSubConfiguration"></param>
        /// <param name="replaceExisting"> flag that indicates if current configuration is overwritten</param>
        public void LoadConfiguration(PubSubConfigurationDataType pubSubConfiguration, bool replaceExisting = true)
        {
            lock (m_lock)
            {
                if (replaceExisting)
                {
                    //remove previous configured published data sets
                    if (m_pubSubConfiguration != null && m_pubSubConfiguration.PublishedDataSets.Count > 0)
                    {
                        foreach (PublishedDataSetDataType publishedDataSet in pubSubConfiguration.PublishedDataSets)
                        {
                            RemovePublishedDataSet(publishedDataSet);
                        }
                    }

                    //remove previous configured connections
                    if (m_pubSubConfiguration != null && m_pubSubConfiguration.Connections.Count > 0)
                    {
                        foreach (var connection in m_pubSubConfiguration.Connections.ToArray())
                        {
                            RemoveConnection(connection);
                        }
                    }

                    m_pubSubConfiguration.Connections.Clear();
                    m_pubSubConfiguration.PublishedDataSets.Clear();
                }

                //first load Published DataSet information
                foreach (PublishedDataSetDataType publishedDataSet in pubSubConfiguration.PublishedDataSets)
                {
                    AddPublishedDataSet(publishedDataSet);
                }

                foreach (PubSubConnectionDataType pubSubConnectionDataType in pubSubConfiguration.Connections)
                {
                    // handle empty names 
                    if (string.IsNullOrEmpty(pubSubConnectionDataType.Name))
                    {
                        //set default name 
                        pubSubConnectionDataType.Name = "Connection_" + (m_nextId + 1);
                    }
                    AddConnection(pubSubConnectionDataType);
                }
            }
        }
        #endregion

        #region Public Methods - PublishedDataSet
        /// <summary>
        /// Add a published data set to current configuration.
        /// </summary>
        /// <param name="publishedDataSetDataType">The <see cref="PublishedDataSetDataType"/> object to be added to configuration.</param>
        /// <returns></returns>
        public StatusCode AddPublishedDataSet(PublishedDataSetDataType publishedDataSetDataType)
        {
            if (m_objectsToIds.ContainsKey(publishedDataSetDataType))
            {
                throw new ArgumentException("This PublishedDataSetDataType instance is already added to the configuration.");
            }
            try
            {
                lock (m_lock)
                {
                    //validate duplicate name 
                    bool duplicateName = false;
                    foreach (var publishedDataSet in m_pubSubConfiguration.PublishedDataSets)
                    {
                        if (publishedDataSetDataType.Name == publishedDataSet.Name)
                        {
                            duplicateName = true;
                            break;
                        }
                    }
                    if (duplicateName)
                    {
                        Utils.Trace(Utils.TraceMasks.Error, "Attempted to add PublishedDataSetDataType with duplicate name = {0}", publishedDataSetDataType.Name);
                        return StatusCodes.BadBrowseNameDuplicated;
                    }

                    uint newPublishedDataSetId = m_nextId++;
                    //remember connection 
                    m_idsToObjects.Add(newPublishedDataSetId, publishedDataSetDataType);
                    m_objectsToIds.Add(publishedDataSetDataType, newPublishedDataSetId);
                    m_pubSubConfiguration.PublishedDataSets.Add(publishedDataSetDataType);

                    // raise PublishedDataSetAdded event
                    if (PublishedDataSetAdded != null)
                    {
                        PublishedDataSetAdded(this,
                            new PublishedDataSetEventArgs() { PublishedDataSetId = newPublishedDataSetId, PublishedDataSetDataType = publishedDataSetDataType });
                    }

                    if (publishedDataSetDataType.ExtensionFields == null)
                    {
                        publishedDataSetDataType.ExtensionFields = new KeyValuePairCollection();
                    }
                    KeyValuePairCollection extensionFields = new KeyValuePairCollection(publishedDataSetDataType.ExtensionFields);
                    publishedDataSetDataType.ExtensionFields.Clear();
                    foreach (KeyValuePair extensionField in extensionFields)
                    {
                        AddExtensionField(newPublishedDataSetId, extensionField);
                    }
                    return StatusCodes.Good;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddPublishedDataSet: Exception");
            }
            //todo implement state validation
            return StatusCodes.Bad; 
        }

        /// <summary>
        /// Removes a published data set from current configuration.
        /// </summary>
        /// <param name="publishedDataSetId">Id of the published data set to be removed.</param>
        /// <returns> 
        /// - <see cref="StatusCodes.Good"/> if operation is successful, 
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> otherwise.
        /// </returns>
        public StatusCode RemovePublishedDataSet(uint publishedDataSetId)
        {
            lock (m_lock)
            {
                PublishedDataSetDataType publishedDataSetDataType = FindObjectById(publishedDataSetId) as PublishedDataSetDataType;
                if (publishedDataSetDataType == null)
                {
                    // Unexpected exception 
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain PublishedDataSetDataType with ConfigId = {0}", publishedDataSetId);
                    return StatusCodes.Good;
                }
                return RemovePublishedDataSet(publishedDataSetDataType);
            }
        }

        /// <summary>
        /// Removes a published data set from current configuration.
        /// </summary>
        /// <param name="publishedDataSetDataType">The published data set to be removed.</param>
        /// <returns> 
        /// - <see cref="StatusCodes.Good"/> if operation is successful, 
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> otherwise.
        /// </returns>
        public StatusCode RemovePublishedDataSet(PublishedDataSetDataType publishedDataSetDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint publishedDataSetId = FindIdForObject(publishedDataSetDataType);
                    if (publishedDataSetDataType != null && publishedDataSetId != InvalidId)
                    {
                        /*A successful removal of the PublishedDataSetType Object removes all associated DataSetWriter Objects. 
                         * Before the Objects are removed, their state is changed to Disabled_0*/

                        // Find all associated DataSetWriter objects
                        foreach(var connection in m_pubSubConfiguration.Connections)
                        {
                            foreach(var writerGroup in connection.WriterGroups)
                            {
                                foreach(var dataSetWriter in writerGroup.DataSetWriters.ToArray())
                                {
                                    if (dataSetWriter.DataSetName == publishedDataSetDataType.Name)
                                    {
                                        RemoveDataSetWriter(dataSetWriter);
                                    }
                                }
                            }
                        }

                        m_pubSubConfiguration.PublishedDataSets.Remove(publishedDataSetDataType);

                        //remove all references from dictionaries
                        m_idsToObjects.Remove(publishedDataSetId);
                        m_objectsToIds.Remove(publishedDataSetDataType);
                        m_idsToParentId.Remove(publishedDataSetId);
                        m_idsToPubSubState.Remove(publishedDataSetId);

                        if (PublishedDataSetRemoved != null)
                        {
                            PublishedDataSetRemoved(this, new PublishedDataSetEventArgs()
                            {
                                PublishedDataSetId = publishedDataSetId,
                                PublishedDataSetDataType = publishedDataSetDataType
                            });
                        }
                        return StatusCodes.Good;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemovePublishedDataSet: Exception");
            }

            return StatusCodes.BadNodeIdUnknown;
        }

        /// <summary>
        /// Add Extension field to the specified publishedDataset
        /// </summary>
        /// <param name="publishedDataSetConfigId"></param>
        /// <param name="extensionField"></param>
        /// <returns></returns>
        public StatusCode AddExtensionField(uint publishedDataSetConfigId, KeyValuePair extensionField)
        {
            lock (m_lock)
            {
                PublishedDataSetDataType publishedDataSetDataType = FindObjectById(publishedDataSetConfigId) as PublishedDataSetDataType;
                if (publishedDataSetDataType == null)
                {
                    return StatusCodes.BadNodeIdInvalid;
                }
                if (publishedDataSetDataType.ExtensionFields == null)
                {
                    publishedDataSetDataType.ExtensionFields = new KeyValuePairCollection();
                }
                else
                {
                    //validate duplicate name 
                    bool duplicateName = false;
                    foreach (KeyValuePair element in publishedDataSetDataType.ExtensionFields)
                    {
                        if (element.Key == extensionField.Key)
                        {
                            duplicateName = true;
                            break;
                        }
                    }
                    if (duplicateName)
                    {
                        Utils.Trace(Utils.TraceMasks.Error, "AddExtensionField -  A field with the name already exists. Duplicate name = {0}", extensionField.Key);
                        return StatusCodes.BadNodeIdExists;
                    }
                }
                uint newextensionFieldId = m_nextId++;
                //remember connection 
                m_idsToObjects.Add(newextensionFieldId, extensionField);
                m_objectsToIds.Add(extensionField, newextensionFieldId);     
                publishedDataSetDataType.ExtensionFields.Add(extensionField);

                // raise ExtensionFieldAdded event
                if (ExtensionFieldAdded != null)
                {
                    ExtensionFieldAdded(this,
                        new ExtensionFieldEventArgs() { PublishedDataSetId = publishedDataSetConfigId, ExtensionFieldId = newextensionFieldId, ExtensionField = extensionField });
                }

                return StatusCodes.Good;
            }
        }            

        /// <summary>
        /// Removes an extension field from a published data set
        /// </summary>
        /// <param name="publishedDataSetConfigId"></param>
        /// <param name="extensionFieldConfigId"></param>
        /// <returns></returns>
        public StatusCode RemoveExtensionField(uint publishedDataSetConfigId, uint extensionFieldConfigId)
        {
            lock (m_lock)
            {
                PublishedDataSetDataType publishedDataSetDataType = FindObjectById(publishedDataSetConfigId) as PublishedDataSetDataType;
                KeyValuePair extensionFieldToRemove = FindObjectById(extensionFieldConfigId) as KeyValuePair;
                if (publishedDataSetDataType == null || extensionFieldToRemove == null)
                {
                    return StatusCodes.BadNodeIdInvalid;
                }
                if (publishedDataSetDataType.ExtensionFields == null)
                {
                    publishedDataSetDataType.ExtensionFields = new KeyValuePairCollection();
                    return StatusCodes.BadNodeIdInvalid;
                }
                // locate the extension field 
                foreach(KeyValuePair extensionField in publishedDataSetDataType.ExtensionFields.ToArray())
                {
                    if (extensionField.Equals(extensionFieldToRemove))
                    {
                        publishedDataSetDataType.ExtensionFields.Remove(extensionFieldToRemove);

                        // raise ExtensionFieldRemoved event
                        if (ExtensionFieldRemoved != null)
                        {
                            ExtensionFieldRemoved(this,
                                new ExtensionFieldEventArgs() { PublishedDataSetId = publishedDataSetConfigId, ExtensionFieldId = extensionFieldConfigId, ExtensionField = extensionField });
                        }
                        return StatusCodes.Good;
                    }
                }                
            }
            return StatusCodes.BadNodeIdInvalid;
        }
        #endregion

        #region Public Methods - Connection
        /// <summary>
        /// Add a connection to current configuration.
        /// </summary>
        /// <param name="pubSubConnectionDataType">The <see cref="PubSubConnectionDataType"/> object that configures the new connection.</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The connection was added with success.
        /// - <see cref="StatusCodes.BadBrowseNameDuplicated"/> An Object with the name already exists.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error adding the connection.
        /// </returns>
        public StatusCode AddConnection(PubSubConnectionDataType pubSubConnectionDataType)
        {
            if (m_objectsToIds.ContainsKey(pubSubConnectionDataType))
            {
                throw new ArgumentException("This PubSubConnectionDataType instance is already added to the configuration.");
            }
            try
            {
                lock (m_lock)
                {
                    //validate connection name 
                    bool duplicateName = false;
                    foreach(var connection in m_pubSubConfiguration.Connections)
                    {
                        if (connection.Name == pubSubConnectionDataType.Name)
                        {
                            duplicateName = true;
                            break;
                        }
                    }
                    if (duplicateName)
                    {
                        Utils.Trace(Utils.TraceMasks.Error, "Attempted to add PubSubConnectionDataType with duplicate name = {0}", pubSubConnectionDataType.Name);
                        return StatusCodes.BadBrowseNameDuplicated;
                    }

                    // remember collections 
                    WriterGroupDataTypeCollection writerGroups = new WriterGroupDataTypeCollection(pubSubConnectionDataType.WriterGroups);
                    pubSubConnectionDataType.WriterGroups.Clear();
                    ReaderGroupDataTypeCollection readerGroups = new ReaderGroupDataTypeCollection(pubSubConnectionDataType.ReaderGroups);
                    pubSubConnectionDataType.ReaderGroups.Clear();

                    uint newConnectionId = m_nextId++;
                    //remember connection 
                    m_idsToObjects.Add(newConnectionId, pubSubConnectionDataType);
                    m_objectsToIds.Add(pubSubConnectionDataType, newConnectionId);
                    // remember parent id
                    m_idsToParentId.Add(newConnectionId, FindIdForObject(m_pubSubConfiguration));
                    //remember initial state
                    m_idsToPubSubState.Add(newConnectionId, GetInitialPubSubState(pubSubConnectionDataType));

                    m_pubSubConfiguration.Connections.Add(pubSubConnectionDataType);

                    // raise ConnectionAdded event
                    if (ConnectionAdded != null)
                    {
                        ConnectionAdded(this,
                            new ConnectionEventArgs() { ConnectionId = newConnectionId, PubSubConnectionDataType = pubSubConnectionDataType });
                    }
                    //handler reader & writer groups 
                    foreach (WriterGroupDataType writerGroup in writerGroups)
                    {
                        // handle empty names 
                        if (string.IsNullOrEmpty(writerGroup.Name))
                        {
                            //set default name 
                            writerGroup.Name = "WriterGroup_" + (m_nextId + 1);
                        }
                        AddWriterGroup(newConnectionId, writerGroup);
                    }
                    foreach (ReaderGroupDataType readerGroup in readerGroups)
                    {
                        // handle empty names 
                        if (string.IsNullOrEmpty(readerGroup.Name))
                        {
                            //set default name 
                            readerGroup.Name = "ReaderGroup_" + (m_nextId + 1);
                        }
                        AddReaderGroup(newConnectionId, readerGroup);
                    }

                    return StatusCodes.Good;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddConnection: Exception");
            }
            return StatusCodes.BadInvalidArgument;
        }

        /// <summary>
        /// Removes a connection from current configuration.
        /// </summary>
        /// <param name="connectionId">Id of the connection to be removed.</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The Connection was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the Connection.
        /// </returns>
        public StatusCode RemoveConnection(uint connectionId)
        {
            lock (m_lock)
            {
                PubSubConnectionDataType pubSubConnectionDataType = FindObjectById(connectionId) as PubSubConnectionDataType;
                if (pubSubConnectionDataType == null)
                {
                    // Unexpected exception 
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain PubSubConnectionDataType with ConfigId = {0}", connectionId);
                    return StatusCodes.BadNodeIdUnknown;
                }
                return RemoveConnection(pubSubConnectionDataType);
            }
        }

        /// <summary>
        /// Removes a connection from current configuration.
        /// </summary>
        /// <param name="pubSubConnectionDataType">The connection to be removed.</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The Connection was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the Connection.
        /// </returns>
        public StatusCode RemoveConnection(PubSubConnectionDataType pubSubConnectionDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint connectionId = FindIdForObject(pubSubConnectionDataType);
                    if (pubSubConnectionDataType != null && connectionId != InvalidId)
                    {
                        // remove children
                        WriterGroupDataTypeCollection writerGroups = new WriterGroupDataTypeCollection(pubSubConnectionDataType.WriterGroups);
                        foreach (var writerGroup in writerGroups)
                        {
                            RemoveWriterGroup(writerGroup);
                        }
                        ReaderGroupDataTypeCollection readerGroups = new ReaderGroupDataTypeCollection(pubSubConnectionDataType.ReaderGroups);
                        foreach (var readerGroup in readerGroups)
                        {
                            RemoveReaderGroup(readerGroup);
                        }
                        m_pubSubConfiguration.Connections.Remove(pubSubConnectionDataType);

                        //remove all references from dictionaries
                        m_idsToObjects.Remove(connectionId);
                        m_objectsToIds.Remove(pubSubConnectionDataType);
                        m_idsToParentId.Remove(connectionId);
                        m_idsToPubSubState.Remove(connectionId);

                        if (ConnectionRemoved != null)
                        {
                            ConnectionRemoved(this, new ConnectionEventArgs()
                            {
                                ConnectionId = connectionId,
                                PubSubConnectionDataType = pubSubConnectionDataType
                            });
                        }
                        return StatusCodes.Good;
                    }
                    return StatusCodes.BadNodeIdUnknown;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemoveConnection: Exception");
            }

            return StatusCodes.BadInvalidArgument;
        }
        #endregion

        #region Public Methods - WriterGroup
        /// <summary>
        /// Adds a writerGroup to the specified connection
        /// </summary>
        /// <param name="parentConnectionId"></param>
        /// <param name="writerGroupDataType"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The WriterGroup was added with success.
        /// - <see cref="StatusCodes.BadBrowseNameDuplicated"/> An Object with the name already exists.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error adding the WriterGroup.
        /// </returns>
        public StatusCode AddWriterGroup(uint parentConnectionId, WriterGroupDataType writerGroupDataType)
        {
            if (m_objectsToIds.ContainsKey(writerGroupDataType))
            {
                throw new ArgumentException("This WriterGroupDataType instance is already added to the configuration.");
            }
            if (!m_idsToObjects.ContainsKey(parentConnectionId))
            {
                throw new ArgumentException(String.Format("There is no connection with configurationId = {0} in current configuration.", parentConnectionId));
            }
            try
            {
                lock (m_lock)
                {
                    // remember collections 
                    DataSetWriterDataTypeCollection dataSetWriters = new DataSetWriterDataTypeCollection(writerGroupDataType.DataSetWriters);
                    writerGroupDataType.DataSetWriters.Clear();
                    PubSubConnectionDataType parentConnection = m_idsToObjects[parentConnectionId] as PubSubConnectionDataType;
                    if (parentConnection != null)
                    {
                        //validate duplicate name 
                        bool duplicateName = false;
                        foreach (var writerGroup in parentConnection.WriterGroups)
                        {
                            if (writerGroup.Name == writerGroupDataType.Name)
                            {
                                duplicateName = true;
                                break;
                            }
                        }
                        if (duplicateName)
                        {
                            Utils.Trace(Utils.TraceMasks.Error, "Attempted to add WriterGroupDataType with duplicate name = {0}", writerGroupDataType.Name);
                            return StatusCodes.BadBrowseNameDuplicated;
                        }

                        uint newWriterGroupId = m_nextId++;
                        //remember writer group 
                        m_idsToObjects.Add(newWriterGroupId, writerGroupDataType);
                        m_objectsToIds.Add(writerGroupDataType, newWriterGroupId);
                        parentConnection.WriterGroups.Add(writerGroupDataType);

                        // remember parent id
                        m_idsToParentId.Add(newWriterGroupId, parentConnectionId);
                        //remember initial state
                        m_idsToPubSubState.Add(newWriterGroupId, GetInitialPubSubState(writerGroupDataType));

                        // raise WriterGroupAdded event
                        if (WriterGroupAdded != null)
                        {
                            WriterGroupAdded(this,
                                new WriterGroupEventArgs() { ConnectionId = parentConnectionId, WriterGroupId = newWriterGroupId, WriterGroupDataType = writerGroupDataType });
                        }

                        //handler datasetWriters
                        foreach (DataSetWriterDataType datasetWriter in dataSetWriters)
                        {
                            // handle empty names 
                            if (string.IsNullOrEmpty(datasetWriter.Name))
                            {
                                //set default name 
                                datasetWriter.Name = "DataSetWriter_" + (m_nextId + 1);
                            }
                            AddDataSetWriter(newWriterGroupId, datasetWriter);
                        }

                        return StatusCodes.Good;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddWriterGroup: Exception");
            }
            return StatusCodes.BadInvalidArgument;
        }

        /// <summary>
        /// Removes a WriterGroupDataType instance from current configuration specified by confgiId
        /// </summary>
        /// <param name="writerGroupId"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The WriterGroup was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the WriterGroup.
        /// </returns>
        public StatusCode RemoveWriterGroup(uint writerGroupId)
        {
            lock (m_lock)
            {
                WriterGroupDataType writerGroupDataType = FindObjectById(writerGroupId) as WriterGroupDataType;
                if (writerGroupDataType == null)
                {
                    // Unexpected exception 
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain WriterGroupDataType with ConfigId = {0}", writerGroupId);
                    return StatusCodes.BadNodeIdUnknown;
                }
                return RemoveWriterGroup(writerGroupDataType);
            }
        }

        /// <summary>
        /// Removes a WriterGroupDataType instance from current configuration
        /// </summary>
        /// <param name="writerGroupDataType">Instance to remove</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The WriterGroup was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the WriterGroup.
        /// </returns>
        public StatusCode RemoveWriterGroup(WriterGroupDataType writerGroupDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint writerGroupId = FindIdForObject(writerGroupDataType);
                    if (writerGroupDataType != null && writerGroupId != InvalidId)
                    {
                        // remove children
                        DataSetWriterDataTypeCollection dataSetWriters = new DataSetWriterDataTypeCollection(writerGroupDataType.DataSetWriters);
                        foreach (var dataSetWriter in dataSetWriters)
                        {
                            RemoveDataSetWriter(dataSetWriter);
                        }
                        // find parent connection
                        PubSubConnectionDataType parentConnection = FindParentForObject(writerGroupDataType) as PubSubConnectionDataType;
                        uint parentConnectionId = FindIdForObject(parentConnection);
                        if (parentConnection != null && parentConnectionId != InvalidId)
                        {
                            parentConnection.WriterGroups.Remove(writerGroupDataType);

                            //remove all references from dictionaries
                            m_idsToObjects.Remove(writerGroupId);
                            m_objectsToIds.Remove(writerGroupDataType);
                            m_idsToParentId.Remove(writerGroupId);
                            m_idsToPubSubState.Remove(writerGroupId);

                            if (WriterGroupRemoved != null)
                            {
                                WriterGroupRemoved(this, new WriterGroupEventArgs()
                                {
                                    WriterGroupId = writerGroupId,
                                    WriterGroupDataType = writerGroupDataType,
                                    ConnectionId = parentConnectionId
                                });
                            }
                            return StatusCodes.Good;
                        }
                    }

                    return StatusCodes.BadNodeIdUnknown;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemoveWriterGroup: Exception");
            }

            return StatusCodes.BadInvalidArgument;
        }
        #endregion

        #region Public Methods - DataSetWriter
        /// <summary>
        /// Adds a DataSetWriter to the specified writer group
        /// </summary>
        /// <param name="parentWriterGroupId"></param>
        /// <param name="dataSetWriterDataType"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetWriter was added with success.
        /// - <see cref="StatusCodes.BadBrowseNameDuplicated"/> An Object with the name already exists.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error adding the DataSetWriter.
        /// </returns>
        public StatusCode AddDataSetWriter(uint parentWriterGroupId, DataSetWriterDataType dataSetWriterDataType)
        {
            if (m_objectsToIds.ContainsKey(dataSetWriterDataType))
            {
                throw new ArgumentException("This DataSetWriterDataType instance is already added to the configuration.");
            }
            if (!m_idsToObjects.ContainsKey(parentWriterGroupId))
            {
                throw new ArgumentException(String.Format("There is no WriterGroup with configurationId = {0} in current configuration.", parentWriterGroupId));
            }
            try
            {
                lock (m_lock)
                {
                    WriterGroupDataType parentWriterGroup = m_idsToObjects[parentWriterGroupId] as WriterGroupDataType;
                    if (parentWriterGroup != null)
                    {
                        //validate duplicate name 
                        bool duplicateName = false;
                        foreach (var writer in parentWriterGroup.DataSetWriters)
                        {
                            if (writer.Name == dataSetWriterDataType.Name)
                            {
                                duplicateName = true;
                                break;
                            }
                        }
                        if (duplicateName)
                        {
                            Utils.Trace(Utils.TraceMasks.Error, "Attempted to add DataSetWriterDataType with duplicate name = {0}", dataSetWriterDataType.Name);
                            return StatusCodes.BadBrowseNameDuplicated;
                        }

                        uint newDataSetWriterId = m_nextId++;
                        //remember connection 
                        m_idsToObjects.Add(newDataSetWriterId, dataSetWriterDataType);
                        m_objectsToIds.Add(dataSetWriterDataType, newDataSetWriterId);
                        parentWriterGroup.DataSetWriters.Add(dataSetWriterDataType);

                        // remember parent id
                        m_idsToParentId.Add(newDataSetWriterId, parentWriterGroupId);

                        //remember initial state
                        m_idsToPubSubState.Add(newDataSetWriterId, GetInitialPubSubState(dataSetWriterDataType));

                        // raise DataSetWriterAdded event
                        if (DataSetWriterAdded != null)
                        {
                            DataSetWriterAdded(this,
                                new DataSetWriterEventArgs() { WriterGroupId = parentWriterGroupId, DataSetWriterId = newDataSetWriterId, DataSetWriterDataType = dataSetWriterDataType });
                        }

                        return StatusCodes.Good;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddDataSetWriter: Exception");
            }
             return StatusCodes.BadInvalidArgument;
        }

        /// <summary>
        /// Removes a DataSetWriterDataType instance from current configuration specified by confgiId
        /// </summary>
        /// <param name="dataSetWriterId"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetWriter was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the DataSetWriter.
        /// </returns>
        public StatusCode RemoveDataSetWriter(uint dataSetWriterId)
        {
            lock (m_lock)
            {
                DataSetWriterDataType dataSetWriterDataType = FindObjectById(dataSetWriterId) as DataSetWriterDataType;
                if (dataSetWriterDataType == null)
                {
                    // Unexpected exception 
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain DataSetWriterDataType with ConfigId = {0}", dataSetWriterId);
                    return StatusCodes.BadNodeIdUnknown;
                }
                return RemoveDataSetWriter(dataSetWriterDataType);
            }
        }

        /// <summary>
        /// Removes a DataSetWriterDataType instance from current configuration
        /// </summary>
        /// <param name="dataSetWriterDataType">Instance to remove</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetWriter was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the DataSetWriter.
        /// </returns>
        public StatusCode RemoveDataSetWriter(DataSetWriterDataType dataSetWriterDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint dataSetWriterId = FindIdForObject(dataSetWriterDataType);
                    if (dataSetWriterDataType != null && dataSetWriterId != InvalidId)
                    {
                        // find parent writerGroup
                        WriterGroupDataType parentWriterGroup = FindParentForObject(dataSetWriterDataType) as WriterGroupDataType;
                        uint parentWriterGroupId = FindIdForObject(parentWriterGroup);
                        if (parentWriterGroup != null && parentWriterGroupId != InvalidId)
                        {
                            parentWriterGroup.DataSetWriters.Remove(dataSetWriterDataType);

                            //remove all references from dictionaries
                            m_idsToObjects.Remove(dataSetWriterId);
                            m_objectsToIds.Remove(dataSetWriterDataType);
                            m_idsToParentId.Remove(dataSetWriterId);
                            m_idsToPubSubState.Remove(dataSetWriterId);

                            if (DataSetWriterRemoved != null)
                            {
                                DataSetWriterRemoved(this, new DataSetWriterEventArgs()
                                {
                                    WriterGroupId = parentWriterGroupId,
                                    DataSetWriterDataType = dataSetWriterDataType,
                                    DataSetWriterId = dataSetWriterId
                                });
                            }
                            return StatusCodes.Good;
                        }
                    }
                    return StatusCodes.BadNodeIdUnknown;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemoveDataSetWriter: Exception");
            }

            return StatusCodes.BadInvalidArgument;
        }
        #endregion

        #region Public Methods - ReaderGroup
        /// <summary>
        /// Adds a readerGroup to the specified connection
        /// </summary>
        /// <param name="parentConnectionId"></param>
        /// <param name="readerGroupDataType"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The ReaderGroup was added with success.
        /// - <see cref="StatusCodes.BadBrowseNameDuplicated"/> An Object with the name already exists.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error adding the ReaderGroup.
        /// </returns>
        public StatusCode AddReaderGroup(uint parentConnectionId, ReaderGroupDataType readerGroupDataType)
        {
            if (m_objectsToIds.ContainsKey(readerGroupDataType))
            {
                throw new ArgumentException("This ReaderGroupDataType instance is already added to the configuration.");
            }
            if (!m_idsToObjects.ContainsKey(parentConnectionId))
            {
                throw new ArgumentException(String.Format("There is no connection with configurationId = {0} in current configuration.", parentConnectionId));
            }
            try
            {
                lock (m_lock)
                {
                    // remember collections 
                    DataSetReaderDataTypeCollection dataSetReaders = new DataSetReaderDataTypeCollection(readerGroupDataType.DataSetReaders);
                    readerGroupDataType.DataSetReaders.Clear();
                    PubSubConnectionDataType parentConnection = m_idsToObjects[parentConnectionId] as PubSubConnectionDataType;
                    if (parentConnection != null)
                    {
                        //validate duplicate name 
                        bool duplicateName = false;
                        foreach (var readerGroup in parentConnection.ReaderGroups)
                        {
                            if (readerGroup.Name == readerGroupDataType.Name)
                            {
                                duplicateName = true;
                                break;
                            }
                        }
                        if (duplicateName)
                        {
                            Utils.Trace(Utils.TraceMasks.Error, "Attempted to add ReaderGroupDataType with duplicate name = {0}", readerGroupDataType.Name);
                            return StatusCodes.BadBrowseNameDuplicated;
                        }

                        uint newReaderGroupId = m_nextId++;
                        //remember reader group 
                        m_idsToObjects.Add(newReaderGroupId, readerGroupDataType);
                        m_objectsToIds.Add(readerGroupDataType, newReaderGroupId);
                        parentConnection.ReaderGroups.Add(readerGroupDataType);

                        // remember parent id
                        m_idsToParentId.Add(newReaderGroupId, parentConnectionId);

                        //remember initial state
                        m_idsToPubSubState.Add(newReaderGroupId, GetInitialPubSubState(readerGroupDataType));

                        // raise ReaderGroupAdded event
                        if (ReaderGroupAdded != null)
                        {
                            ReaderGroupAdded(this,
                                new ReaderGroupEventArgs() { ConnectionId = parentConnectionId, ReaderGroupId = newReaderGroupId, ReaderGroupDataType = readerGroupDataType });
                        }

                        //handler datasetWriters
                        foreach (DataSetReaderDataType datasetReader in dataSetReaders)
                        {
                            // handle empty names 
                            if (string.IsNullOrEmpty(datasetReader.Name))
                            {
                                //set default name 
                                datasetReader.Name = "DataSetReader_" + (m_nextId + 1);
                            }
                            AddDataSetReader(newReaderGroupId, datasetReader);
                        }

                        return StatusCodes.Good;
                    }
                   
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddReaderGroup: Exception");
            }
            return StatusCodes.BadInvalidArgument;
        }

        /// <summary>
        /// Removes a ReaderGroupDataType instance from current configuration specified by confgiId
        /// </summary>
        /// <param name="readerGroupId"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The ReaderGroup was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the ReaderGroup.
        /// </returns>
        public StatusCode RemoveReaderGroup(uint readerGroupId)
        {
            lock (m_lock)
            {
                ReaderGroupDataType readerGroupDataType = FindObjectById(readerGroupId) as ReaderGroupDataType;
                if (readerGroupDataType == null)
                {
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain ReaderGroupDataType with ConfigId = {0}", readerGroupId);
                    return StatusCodes.BadInvalidArgument;
                }
                return RemoveReaderGroup(readerGroupDataType);
            }
        }

        /// <summary>
        /// Removes a ReaderGroupDataType instance from current configuration
        /// </summary>
        /// <param name="readerGroupDataType">Instance to remove</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The ReaderGroup was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the ReaderGroup.
        /// </returns>
        public StatusCode RemoveReaderGroup(ReaderGroupDataType readerGroupDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint readerGroupId = FindIdForObject(readerGroupDataType);
                    if (readerGroupDataType != null && readerGroupId != InvalidId)
                    {
                        // remove children
                        DataSetReaderDataTypeCollection dataSetReaders = new DataSetReaderDataTypeCollection(readerGroupDataType.DataSetReaders);
                        foreach (var dataSetReader in dataSetReaders)
                        {
                            RemoveDataSetReader(dataSetReader);
                        }
                        // find parent connection
                        PubSubConnectionDataType parentConnection = FindParentForObject(readerGroupDataType) as PubSubConnectionDataType;
                        uint parentConnectionId = FindIdForObject(parentConnection);
                        if (parentConnection != null && parentConnectionId != InvalidId)
                        {
                            parentConnection.ReaderGroups.Remove(readerGroupDataType);

                            //remove all references from dictionaries
                            m_idsToObjects.Remove(readerGroupId);
                            m_objectsToIds.Remove(readerGroupDataType);
                            m_idsToParentId.Remove(readerGroupId);
                            m_idsToPubSubState.Remove(readerGroupId);

                            if (ReaderGroupRemoved != null)
                            {
                                ReaderGroupRemoved(this, new ReaderGroupEventArgs()
                                {
                                    ReaderGroupId = readerGroupId,
                                    ReaderGroupDataType = readerGroupDataType,
                                    ConnectionId = parentConnectionId
                                });
                            }
                            return StatusCodes.Good;
                        }
                    }

                    return StatusCodes.BadNodeIdUnknown;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemoveReaderGroup: Exception");
            }

            return StatusCodes.BadInvalidArgument;
        }
        #endregion

        #region Public Methods - DataSetReader
        /// <summary>
        /// Adds a DataSetReader to the specified reader group
        /// </summary>
        /// <param name="parentReaderGroupId"></param>
        /// <param name="dataSetReaderDataType"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetReader was added with success.
        /// - <see cref="StatusCodes.BadBrowseNameDuplicated"/> An Object with the name already exists.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error adding the DataSetReader.
        /// </returns>
        public StatusCode AddDataSetReader(uint parentReaderGroupId, DataSetReaderDataType dataSetReaderDataType)
        {
            if (m_objectsToIds.ContainsKey(dataSetReaderDataType))
            {
                throw new ArgumentException("This DataSetReaderDataType instance is already added to the configuration.");
            }
            if (!m_idsToObjects.ContainsKey(parentReaderGroupId))
            {
                throw new ArgumentException(String.Format("There is no ReaderGroup with configurationId = {0} in current configuration.", parentReaderGroupId));
            }
            try
            {
                lock (m_lock)
                {
                    ReaderGroupDataType parentReaderGroup = m_idsToObjects[parentReaderGroupId] as ReaderGroupDataType;
                    if (parentReaderGroup != null)
                    {
                        //validate duplicate name 
                        bool duplicateName = false;
                        foreach (var reader in parentReaderGroup.DataSetReaders)
                        {
                            if (reader.Name == dataSetReaderDataType.Name)
                            {
                                duplicateName = true;
                                break;
                            }
                        }
                        if (duplicateName)
                        {
                            Utils.Trace(Utils.TraceMasks.Error, "Attempted to add DataSetReaderDataType with duplicate name = {0}", dataSetReaderDataType.Name);
                            return StatusCodes.BadBrowseNameDuplicated;
                        }

                        uint newDataSetReaderId = m_nextId++;
                        //remember connection 
                        m_idsToObjects.Add(newDataSetReaderId, dataSetReaderDataType);
                        m_objectsToIds.Add(dataSetReaderDataType, newDataSetReaderId);
                        parentReaderGroup.DataSetReaders.Add(dataSetReaderDataType);

                        // remember parent id
                        m_idsToParentId.Add(newDataSetReaderId, parentReaderGroupId);

                        //remember initial state
                        m_idsToPubSubState.Add(newDataSetReaderId, GetInitialPubSubState(dataSetReaderDataType));

                        // raise WriterGroupAdded event
                        if (DataSetReaderAdded != null)
                        {
                            DataSetReaderAdded(this,
                                new DataSetReaderEventArgs() { ReaderGroupId = parentReaderGroupId, DataSetReaderId = newDataSetReaderId, DataSetReaderDataType = dataSetReaderDataType });
                        }

                        return StatusCodes.Good;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.AddDataSetReader: Exception");
            }
            return StatusCodes.BadInvalidArgument;
        }

        /// <summary>
        /// Removes a DataSetReaderDataType instance from current configuration specified by confgiId
        /// </summary>
        /// <param name="dataSetReaderId"></param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetWriter was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the DataSetWriter.
        /// </returns>
        public StatusCode RemoveDataSetReader(uint dataSetReaderId)
        {
            lock (m_lock)
            {
                DataSetReaderDataType dataSetReaderDataType = FindObjectById(dataSetReaderId) as DataSetReaderDataType;
                if (dataSetReaderDataType == null)
                {
                    // Unexpected exception 
                    Utils.Trace(Utils.TraceMasks.Information, "Current configuration does not contain DataSetReaderDataType with ConfigId = {0}", dataSetReaderId);
                    return StatusCodes.BadNodeIdUnknown;
                }
                return RemoveDataSetReader(dataSetReaderDataType);
            }
        }

        /// <summary>
        /// Removes a DataSetReaderDataType instance from current configuration
        /// </summary>
        /// <param name="dataSetReaderDataType">Instance to remove</param>
        /// <returns>
        /// - <see cref="StatusCodes.Good"/> The DataSetWriter was removed with success.
        /// - <see cref="StatusCodes.BadNodeIdUnknown"/> The GroupId is unknown.
        /// - <see cref="StatusCodes.BadInvalidArgument"/> There was an error removing the DataSetWriter.
        /// </returns>
        public StatusCode RemoveDataSetReader(DataSetReaderDataType dataSetReaderDataType)
        {
            try
            {
                lock (m_lock)
                {
                    uint dataSetReaderId = FindIdForObject(dataSetReaderDataType);
                    if (dataSetReaderDataType != null && dataSetReaderId != InvalidId)
                    {
                        // find parent readerGroup
                        ReaderGroupDataType parentWriterGroup = FindParentForObject(dataSetReaderDataType) as ReaderGroupDataType;
                        uint parenReaderGroupId = FindIdForObject(parentWriterGroup);
                        if (parentWriterGroup != null && parenReaderGroupId != InvalidId)
                        {
                            parentWriterGroup.DataSetReaders.Remove(dataSetReaderDataType);

                            //remove all references from dictionaries
                            m_idsToObjects.Remove(dataSetReaderId);
                            m_objectsToIds.Remove(dataSetReaderDataType);
                            m_idsToParentId.Remove(dataSetReaderId);
                            m_idsToPubSubState.Remove(dataSetReaderId);

                            if (DataSetReaderRemoved != null)
                            {
                                DataSetReaderRemoved(this, new DataSetReaderEventArgs()
                                {
                                    ReaderGroupId = parenReaderGroupId,
                                    DataSetReaderDataType = dataSetReaderDataType,
                                    DataSetReaderId = dataSetReaderId
                                });
                            }
                            return StatusCodes.Good;
                        }
                    }
                    return StatusCodes.BadNodeIdUnknown;
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception 
                Utils.Trace(ex, "UaPubSubConfigurator.RemoveDataSetReader: Exception");
            }

            return StatusCodes.BadInvalidArgument;
        }
        #endregion

        #region Public Methods - Enable/Disable
        /// <summary> 
        /// Enable the specified configuration object specified by Id
        /// </summary>
        /// <param name="configurationId"></param>
        /// <returns></returns>
        public StatusCode Enable(uint configurationId)
        {
            return Enable(FindObjectById(configurationId));
        }

        /// <summary>
        /// Enable the specified configuration object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <returns></returns>
        public StatusCode Enable(object configurationObject)
        {
            if (configurationObject == null)
            {
                throw new ArgumentException("The parameter cannot be null.", nameof(configurationObject));
            }
            if (!m_objectsToIds.ContainsKey(configurationObject))
            {
                throw new ArgumentException("This {0} instance is not part of current configuration.", configurationObject.GetType().Name);
            }
            PubSubState currentState = FindStateForObject(configurationObject);
            if (currentState != PubSubState.Disabled)
            {
                Utils.Trace(Utils.TraceMasks.Information, "Attempted to call Enable() on an object that is not in Disabled state");
                return StatusCodes.BadInvalidState;
            }
            PubSubState parentState = PubSubState.Operational;
            if (configurationObject != m_pubSubConfiguration)
            {
                parentState = FindStateForObject(FindParentForObject(configurationObject));
            }

            if (parentState == PubSubState.Operational)
            {
                // Enabled and parent Operational
                SetStateForObject(configurationObject, PubSubState.Operational);
            }
            else
            {
                // Enabled but parent not Operational
                SetStateForObject(configurationObject, PubSubState.Paused);
            }
            UpdateChildrenState(configurationObject);
            return StatusCodes.Good;
        }


        /// <summary> 
        /// Disable the specified configuration object specified by Id
        /// </summary>
        /// <param name="configurationId"></param>
        /// <returns></returns>
        public StatusCode Disable(uint configurationId)
        {
            return Disable(FindObjectById(configurationId));
        }

        /// <summary>
        /// Disable the specified configuration object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <returns></returns>
        public StatusCode Disable(object configurationObject)
        {
            if (configurationObject == null)
            {
                throw new ArgumentException("The parameter cannot be null.", nameof(configurationObject));
            }
            if (!m_objectsToIds.ContainsKey(configurationObject))
            {
                throw new ArgumentException("This {0} instance is not part of current configuration.", configurationObject.GetType().Name);
            }
            PubSubState currentState = FindStateForObject(configurationObject);
            if (currentState == PubSubState.Disabled)
            {
                Utils.Trace(Utils.TraceMasks.Information, "Attempted to call Disable() on an object that is already in Disabled state");
                return StatusCodes.BadInvalidState;
            }

            SetStateForObject(configurationObject, PubSubState.Disabled);

            UpdateChildrenState(configurationObject);
            return StatusCodes.Good;
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Change state for the specified configuration object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <param name="newState"></param>
        private void SetStateForObject(object configurationObject, PubSubState newState)
        {
            uint id = FindIdForObject(configurationObject);
            if (id != InvalidId && m_idsToPubSubState.ContainsKey(id))
            {
                PubSubState oldState = m_idsToPubSubState[id];
                m_idsToPubSubState[id] = newState;
                if (PubSubStateChanged != null)
                {
                    PubSubStateChanged(this, new PubSubStateChangedEventArgs()
                    {
                        ConfigurationObject = configurationObject,
                        ConfigurationObjectId = id,
                        NewState = newState,
                        OldState = oldState
                    });
                }
                bool configurationObjectEnabled = (newState == PubSubState.Operational || newState == PubSubState.Paused);
                //update the Enabled flag in config object
                if (configurationObject is PubSubConfigurationDataType)
                {
                    ((PubSubConfigurationDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
                else if (configurationObject is PubSubConnectionDataType)
                {
                    ((PubSubConnectionDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
                else if (configurationObject is WriterGroupDataType)
                {
                    ((WriterGroupDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
                else if (configurationObject is DataSetWriterDataType)
                {
                    ((DataSetWriterDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
                else if (configurationObject is ReaderGroupDataType)
                {
                    ((ReaderGroupDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
                else if (configurationObject is DataSetReaderDataType)
                {
                    ((DataSetReaderDataType)configurationObject).Enabled = configurationObjectEnabled;
                }
            }
        }

        /// <summary>
        /// Calculate and update the state for child objects of a configuration object (StATE MACHINE)
        /// </summary>
        /// <param name="configurationObject"></param>
        private void UpdateChildrenState(object configurationObject)
        {
            PubSubState parentState = FindStateForObject(configurationObject);
            //find child ids
            var childrenIds = FindChildrenIdsForObject(configurationObject);
            if (parentState == PubSubState.Operational)
            {
                // Enabled and parent Operational
                foreach (uint childId in childrenIds)
                {
                    PubSubState childState = FindStateForId(childId);
                    if (childState == PubSubState.Paused)
                    {
                        // become Operational if Parent changed to Operational
                        object childObject = FindObjectById(childId);
                        SetStateForObject(childObject, PubSubState.Operational);

                        UpdateChildrenState(childObject);
                    }
                }
            }
            else if (parentState == PubSubState.Disabled || parentState == PubSubState.Paused)
            {
                // Parent changed to Disabled or Paused
                foreach (uint childId in childrenIds)
                {
                    PubSubState childState = FindStateForId(childId);
                    if (childState == PubSubState.Operational || childState == PubSubState.Error)
                    {
                        // become Operational if Parent changed to Operational
                        object childObject = FindObjectById(childId);
                        SetStateForObject(childObject, PubSubState.Paused);

                        UpdateChildrenState(childObject);
                    }
                }
            }
        }

        /// <summary>
        /// Get <see cref="PubSubState"/> for an item depending on enabled flag and parent's <see cref="PubSubState"/>.
        /// </summary>
        /// <param name="enabled">Configured Enabled flag. </param>
        /// <param name="parentPubSubState"><see cref="PubSubState"/> of the parent configured object.</param>
        /// <returns></returns>
        private PubSubState GetInitialPubSubState(bool enabled, PubSubState parentPubSubState)
        {
            if (enabled)
            {
                if (parentPubSubState == PubSubState.Operational)
                {
                    // The PubSub component is operational.
                    return PubSubState.Operational;
                }
                else
                {
                    // The PubSub component is enabled but currently paused by a parent component. The
                    // parent component is either Disabled_0 or Paused_1.
                    return PubSubState.Paused;
                }
            }
            else
            {
                // PubSub component is configured but currently disabled.
                return PubSubState.Disabled;
            }
        }

        /// <summary>
        /// Calculate and return the initial state of a pub sub data type configuration object
        /// </summary>
        /// <param name="configurationObject"></param>
        /// <returns></returns>
        private PubSubState GetInitialPubSubState(object configurationObject)
        {
            bool configurationObjectEnabled = false;
            PubSubState parentPubSubState = PubSubState.Operational;
            uint parentId = InvalidId;

            if (configurationObject is PubSubConfigurationDataType)
            {
                configurationObjectEnabled = ((PubSubConfigurationDataType)configurationObject).Enabled;
            }
            else if (configurationObject is PubSubConnectionDataType)
            {
                configurationObjectEnabled = ((PubSubConnectionDataType)configurationObject).Enabled;
                //find parent state 
                parentPubSubState = FindStateForObject(m_pubSubConfiguration);
            }
            else if (configurationObject is WriterGroupDataType)
            {
                configurationObjectEnabled = ((WriterGroupDataType)configurationObject).Enabled;
                //find parent connection
                object parentConnection = FindParentForObject(configurationObject);
                //find parent state 
                parentPubSubState = FindStateForObject(parentConnection);
            }
            else if (configurationObject is DataSetWriterDataType)
            {
                configurationObjectEnabled = ((DataSetWriterDataType)configurationObject).Enabled;
                //find parent 
                object parentWriterGroup = FindParentForObject(configurationObject);
                //find parent state 
                parentPubSubState = FindStateForObject(parentWriterGroup);
            }
            else if (configurationObject is ReaderGroupDataType)
            {
                configurationObjectEnabled = ((ReaderGroupDataType)configurationObject).Enabled;
                //find parent connection
                object parentConnection = FindParentForObject(configurationObject);
                //find parent state 
                parentPubSubState = FindStateForObject(parentConnection);
            }
            else if (configurationObject is DataSetReaderDataType)
            {
                configurationObjectEnabled = ((DataSetReaderDataType)configurationObject).Enabled;
                //find parent 
                object parentReaderGroup = FindParentForObject(configurationObject);
                //find parent state 
                parentPubSubState = FindStateForObject(parentReaderGroup);
            }
            else
            {
                return PubSubState.Error;
            }
            return GetInitialPubSubState(configurationObjectEnabled, parentPubSubState);
        }
        #endregion
    }
}
