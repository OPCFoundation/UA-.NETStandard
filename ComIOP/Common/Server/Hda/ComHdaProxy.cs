/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComHdaProxy : ComProxy
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaProxy"/> class.
        /// </summary>
        public ComHdaProxy()
		{
            m_mapper = new ComNamespaceMapper();
            m_browseCacheManager = new ComDaBrowseCache(m_mapper);
            m_browseManager = new ComDaBrowseManager(m_mapper, m_browseCacheManager);
            m_itemManager = new ComHdaItemManager(m_mapper);
            m_transactions = new Dictionary<int,Transaction>();
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {  
            if (disposing)
            {
                lock (Lock)
                {
                    // TBD
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        protected override void OnSessionCreated()
        {
            lock (Lock)
            {
                // fetch the configuration.
                m_configuration = Endpoint.ParseExtension<ComHdaProxyConfiguration>(null);

                if (m_configuration == null)
                {
                    m_configuration = new ComHdaProxyConfiguration();
                }

                Session session = Session;

                // update the mapping and pass the new session to other objects.
                m_mapper.Initialize(session, m_configuration);
                
                // update the aggregate mappings.
                UpdateAggregateMappings(session);

                // save the configuration.
                Endpoint.UpdateExtension<ComHdaProxyConfiguration>(null, m_configuration);
                SaveConfiguration();
            }
        }

        /// <summary>
        /// Called when a session is reconnected.
        /// </summary>
        protected override void OnSessionReconected()
        {
            // TBD
        }
        #endregion

        #region HDA Aggregate Support
        /// <summary>
        /// Gets the supported aggregates.
        /// </summary>
        /// <returns></returns>
        public List<HdaAggregate> GetSupportedAggregates()
        {
            return m_aggregates;
        }

        /// <summary>
        /// Updates the aggregate mappings.
        /// </summary>
        /// <param name="session">The session.</param>
        private void UpdateAggregateMappings(Session session)
        {
            // get the list of supported aggregates.
            NodeId objectId = GetAggregateFunctionsObjectId(session);

            // create the updated mapping set.
            NodeIdMappingSet mappingSet = new NodeIdMappingSet();
            mappingSet.MappingType = Opc.Ua.BrowseNames.AggregateFunctions;
            mappingSet.Mappings = new NodeIdMappingCollection();

            List<HdaAggregate> aggregates = GetAggregateFunctions(session, objectId);

            // check for unassigned aggregate ids.
            uint maxId = 0x80000000;

            for (int ii = 0; ii < aggregates.Count; ii++)
            {
                if (aggregates[ii].LocalId != 0)
                {
                    if (maxId < aggregates[ii].LocalId)
                    {
                        maxId = aggregates[ii].LocalId;
                    }

                    continue;
                }
            }

            // assign aggregate ids.
            for (int ii = 0; ii < aggregates.Count; ii++)
            {
                // assign a new id.
                if (aggregates[ii].LocalId == 0)
                {
                    aggregates[ii].LocalId = maxId++;
                }

                // do not add mapping for built-in annotations.
                if (aggregates[ii].LocalId <= (uint)OpcRcw.Hda.OPCHDA_AGGREGATE.OPCHDA_ANNOTATIONS)
                {
                    continue;
                }

                NodeIdMapping mapping = new NodeIdMapping();

                mapping.NodeId = m_mapper.GetLocalItemId(aggregates[ii].RemoteId);
                mapping.IntegerId = aggregates[ii].LocalId;

                mappingSet.Mappings.Add(mapping);
            }

            // update descriptions.
            UpdateAggregateDescriptions(session, aggregates);

            // update configuration.
            if (m_configuration.MappingSets == null)
            {
                m_configuration.MappingSets = new NodeIdMappingSetCollection();
            }

            // replace existing set.
            for (int ii = 0; ii <  m_configuration.MappingSets.Count; ii++)
            {
                if (m_configuration.MappingSets[ii].MappingType == Opc.Ua.BrowseNames.AggregateFunctions)
                {
                    m_configuration.MappingSets[ii] = mappingSet;
                    mappingSet = null;
                    break;
                }
            }

            // add a new set.
            if (mappingSet != null)
            {
                // update the configuration.
                m_configuration.MappingSets.Add(mappingSet);

                // update the mapping table.
                m_mapper.UpdateMappingSet(mappingSet);
            }

            m_aggregates = aggregates;
        }

        /// <summary>
        /// Gets the aggregate functions object id.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The node id.</returns>
        private NodeId GetAggregateFunctionsObjectId(Session session)
        {
            // build the list of browse paths to follow by parsing the relative paths.
            BrowsePathCollection browsePaths = m_mapper.GetRemoteBrowsePaths(
                Opc.Ua.ObjectIds.Server_ServerCapabilities,
                "/HistoryServerCapabilities/AggregateFunctions");

            // make the call to the server.
            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            NodeId aggregateFunctionsNodeId = Opc.Ua.ObjectIds.Server_ServerCapabilities_AggregateFunctions; 

            for (int ii = 0; ii < results.Count; ii++)
            {
                // check if the start node actually exists.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    Utils.Trace("HistoryServer does not provide the ServerCapabilities object.");
                    continue;
                }

                // an empty list is returned if no node was found.
                if (results[ii].Targets.Count == 0)
                {
                    Utils.Trace("HistoryServer does not provide the AggregateFunctions folder.");
                    continue;
                }

                // Multiple matches are possible, however, the node that matches the type model is the
                // one we are interested in here. The rest can be ignored.
                BrowsePathTarget target = results[ii].Targets[0];

                if (target.RemainingPathIndex != UInt32.MaxValue || target.TargetId.IsAbsolute)
                {
                    Utils.Trace("HistoryServer does not provide the AggregateFunctions folder.");
                    continue;
                }

                // convert to a local node.
                aggregateFunctionsNodeId = ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris);
            }

            return aggregateFunctionsNodeId;
        }

        /// <summary>
        /// Gets the aggregate functions object id.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="objectId">The object id.</param>
        /// <returns>The node id.</returns>
        private List<HdaAggregate> GetAggregateFunctions(Session session, NodeId objectId)
        {
            Browser browser = new Browser(session);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences;
            browser.IncludeSubtypes = true;
            browser.NodeClassMask = (int)NodeClass.Object;
            browser.ResultMask = (uint)(BrowseResultMask.DisplayName | BrowseResultMask.BrowseName | BrowseResultMask.TypeDefinition);
            browser.ContinueUntilDone = true;

            ReferenceDescriptionCollection references = browser.Browse(objectId);

            List<HdaAggregate> aggregates = new List<HdaAggregate>();

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                if (reference.TypeDefinition != Opc.Ua.ObjectTypeIds.AggregateFunctionType)
                {
                    continue;
                }

                HdaAggregate aggregate = new HdaAggregate();

                NodeId remoteId = (NodeId)reference.NodeId;

                aggregate.RemoteId = remoteId;
                aggregate.LocalId = ComUtils.GetHdaAggregateId(remoteId);                
                aggregate.Name = reference.ToString();
                aggregate.Description = null;

                // check for previously mapped ids.
                if (aggregate.LocalId == 0)
                {
                    aggregate.LocalId = m_mapper.GetLocalIntegerIdMapping(Opc.Ua.BrowseNames.AggregateFunctions, remoteId);
                }
                
                aggregates.Add(aggregate);
            }

            return aggregates;
        }

        /// <summary>
        /// Updates the aggregate descriptions.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="aggregates">The aggregates.</param>
        private void UpdateAggregateDescriptions(Session session, List<HdaAggregate> aggregates)
        {
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection(); ;

            for (int ii = 0; ii < aggregates.Count; ii++)
            {
                HdaAggregate aggregate = aggregates[ii];

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = aggregate.RemoteId;
                nodeToRead.AttributeId = Attributes.Description;
                nodesToRead.Add(nodeToRead);
            }

            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            // read values from the UA server.
            ResponseHeader responseHeader = session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out values,
                out diagnosticInfos);

            // validate response from the UA server.
            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            for (int ii = 0; ii < aggregates.Count; ii++)
            {
                HdaAggregate aggregate = aggregates[ii];

                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    aggregate.Description = null;
                    continue;
                }

                aggregate.Description = values[ii].WrappedValue.ToString();
            }
        }
        #endregion

        #region HDA Server Support
        /// <summary>
        /// Gets the max return values.
        /// </summary>
        /// <value>The max return values.</value>
        public int MaxReturnValues
        {
            get
            {
                if (m_configuration == null)
                {
                    return 0;
                }

                return m_configuration.MaxReturnValues;
            }
        }

        /// <summary>
        /// Creates a new browser.
        /// </summary>
        public ComHdaBrowser CreateBrowser()
        {
            ThrowIfNotConnected();
            return new ComHdaBrowser(this, m_browseManager);
        }

        /// <summary>
        /// Sets the callback.
        /// </summary>
        /// <param name="callback">The callback.</param>
        public void SetCallback(IComHdaDataCallback callback)
        {
            ThrowIfNotConnected();

            lock (Lock)
            {
                if (m_callback != null)
                {
                    m_callback.Dispose();
                    m_callback = null;
                }

                m_callback = callback;
            }
        }

        /// <summary>
        /// Determines whether the server supports the specified HDA attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>
        /// 	<c>true</c> if the server supports the specified HDA attribute; otherwise, <c>false</c>.
        /// </returns>
        public bool IsSupportedAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case OpcRcw.Hda.Constants.OPCHDA_DATA_TYPE:
                case OpcRcw.Hda.Constants.OPCHDA_DESCRIPTION:
                case OpcRcw.Hda.Constants.OPCHDA_ARCHIVING:
                case OpcRcw.Hda.Constants.OPCHDA_ITEMID:
                case OpcRcw.Hda.Constants.OPCHDA_ENG_UNITS:
                case OpcRcw.Hda.Constants.OPCHDA_DERIVE_EQUATION:
                case OpcRcw.Hda.Constants.OPCHDA_NORMAL_MAXIMUM:
                case OpcRcw.Hda.Constants.OPCHDA_NORMAL_MINIMUM:
                case OpcRcw.Hda.Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                case OpcRcw.Hda.Constants.OPCHDA_LOW_ENTRY_LIMIT:
                case OpcRcw.Hda.Constants.OPCHDA_STEPPED:
                case OpcRcw.Hda.Constants.OPCHDA_MAX_TIME_INT:
                case OpcRcw.Hda.Constants.OPCHDA_MIN_TIME_INT:
                case OpcRcw.Hda.Constants.OPCHDA_EXCEPTION_DEV:
                case OpcRcw.Hda.Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates handles for the items.
        /// </summary>
        /// <param name="itemIds">The item ids.</param>
        /// <param name="clientHandles">The client handles.</param>
        /// <returns>The handle.</returns>
        public HdaItemHandle[] GetItemHandles(string[] itemIds, int[] clientHandles)
        {
            Session session = ThrowIfNotConnected();
            return m_itemManager.GetItemHandles(session, itemIds, clientHandles, false);
        }

        /// <summary>
        /// Releases the item handles.
        /// </summary>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns>The error associated with each handle.</returns>
        public int[] ReleaseItemHandles(int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();
            return m_itemManager.ReleaseItemHandles(session, serverHandles);
        }

        /// <summary>
        /// Validates the item ids.
        /// </summary>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>The error associated with each item id.</returns>
        public int[] ValidateItemIds(string[] itemIds)
        {
            Session session = ThrowIfNotConnected();

            HdaItemHandle[] handles = m_itemManager.GetItemHandles(session, itemIds, null, true);

            int[] errors = new int[handles.Length];

            for (int ii = 0; ii < handles.Length; ii++)
            {
                errors[ii] = handles[ii].Error;
            }

            return errors;
        }
        #endregion

        #region Read Raw
        /// <summary>
        /// Reads the raw data.
        /// </summary>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="numValues">The num values.</param>
        /// <param name="returnBounds">if set to <c>true</c> the bounds should be returned.</param>
        /// <param name="serverHandles">The server handles.</param>
        /// <returns>The results.</returns>
        public List<HdaReadRequest> ReadRaw(
            DateTime startTime, 
            DateTime endTime, 
            uint numValues, 
            bool returnBounds, 
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();

            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            // create the read requests.
            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                numValues,
                returnBounds,
                serverHandles,
                details);

            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadNext(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }
            
            return requests;
        }

        /// <summary>
        /// Reads the raw data.
        /// </summary>
        public int[] ReadRaw(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            uint numValues,
            bool returnBounds,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();
            
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            // create the read requests.
            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                numValues,
                returnBounds,
                serverHandles,
                details);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.Read,
                transactionId, 
                new ExtensionObject(details), 
                requests,
                false,
                out cancelId);

            // return the initial results.
            return errors;
        }

        /// <summary>
        /// Cancels the asynchronous operation.
        /// </summary>
        public int Cancel(int cancelId)
        {
            lock (Lock)
            {
                Transaction transaction = null;

                if (!m_transactions.TryGetValue(cancelId, out transaction))
                {
                    return ResultIds.E_FAIL;
                }

                m_transactions.Remove(cancelId);
                ThreadPool.QueueUserWorkItem(DoCancel, transaction);
                return ResultIds.S_OK;
            }
        }

        /// <summary>
        /// Executes an asynchronous cancel operation.
        /// </summary>
        private void DoCancel(object state)
        {
            Transaction transaction = (Transaction)state;

            try
            {
                IComHdaDataCallback callback = m_callback;

                if (callback != null)
                {
                    callback.OnCancelComplete(transaction.TransactionId);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error sending cancel callback.");
            }
        }

        /// <summary>
        /// Creates the read requests.
        /// </summary>
        private List<HdaReadRequest> CreateReadRequests(
            Session session,
            DateTime startTime,
            DateTime endTime,
            uint numValues,
            bool returnBounds,
            int[] serverHandles,
            ReadRawModifiedDetails details)
        {
            // start time or end time must be specified.
            if (startTime == DateTime.MinValue && endTime == DateTime.MinValue)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // num values must be specified if start or end time is missing.
            if (numValues == 0 && (startTime == DateTime.MinValue || endTime == DateTime.MinValue))
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // can't exceed the limits imposed by the server configuration.
            if (m_configuration.MaxReturnValues > 0 && numValues > m_configuration.MaxReturnValues)
            {
                throw ComUtils.CreateComException(ResultIds.E_MAXEXCEEDED);
            }
            
            details.StartTime = startTime;
            details.EndTime = endTime;
            details.IsReadModified = false;
            details.NumValuesPerNode = numValues;
            details.ReturnBounds = returnBounds;

            // build the list of requests.
            List<HdaReadRequest> requests = new List<HdaReadRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaReadRequest request = new HdaReadRequest();
                requests.Add(request);

                // look up server handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                // initialize request.
                request.NodeId = request.Handle.NodeId;
                request.ClientHandle = request.Handle.ClientHandle;
            }

            return requests;
        }

        /// <summary>
        /// Creates and queues a new transaction.
        /// </summary>
        private int[] CreateTransaction(
            TransationType transationType,
            int transactionId,
            ExtensionObject details,
            List<HdaReadRequest> requests,
            bool asyncReportErrors,
            out int cancelId)
        {

            lock (Lock)
            {
                cancelId = ++m_lastCancelId;

                // create the transaction.
                ReadRequestTransaction transaction = new ReadRequestTransaction();

                transaction.TransationType = transationType;
                transaction.TransactionId = transactionId;
                transaction.CancelId = cancelId;
                transaction.Details = details;
                transaction.Requests = new List<HdaReadRequest>();

                // keep only the valid requests.
                int[] errors = new int[requests.Count];

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    if (!asyncReportErrors)
                    {
                        errors[ii] = requests[ii].Error;

                        if (errors[ii] < 0)
                        {
                            continue;
                        }
                    }

                    transaction.Requests.Add(requests[ii]);
                }
                
                // queue the transaction.
                if (transaction.Requests.Count > 0)
                {
                    m_transactions.Add(transaction.CancelId, transaction);
                    ThreadPool.QueueUserWorkItem(DoRead, transaction);
                }

                // return the error list.
                return errors;
            }
        }

        /// <summary>
        /// Executes an asynchronous read operation.
        /// </summary>
        private void DoRead(object state)
        {
            Session session = null;
            ReadRequestTransaction transaction = (ReadRequestTransaction)state;

            try
            {
                bool done = false;

                while (!done)
                {
                    // check if the transaction is still active.
                    lock (Lock)
                    {
                        if (!m_transactions.ContainsKey(transaction.CancelId))
                        {
                            break;
                        }

                        session = Session;
                    }

                    // check for session error.
                    if (session == null)
                    {
                        for (int ii = 0; ii < transaction.Requests.Count; ii++)
                        {
                            transaction.Requests[ii].Error = ResultIds.E_FAIL;
                        }

                        SendCallback(transaction.TransactionId, transaction.TransationType, transaction.Requests);
                        break;
                    }

                    // fetch next batch.
                    if (transaction.TransationType == TransationType.ReadAttribute)
                    {
                        done = !ReadAttributes(session, transaction.Details, transaction.Requests, false);
                    }
                    else
                    {
                        done = !ReadNext(session, transaction.Details, transaction.Requests, false);
                    }

                    // send data to client.
                    SendCallback(transaction.TransactionId, transaction.TransationType, transaction.Requests);

                    // remove values that were sent.
                    for (int ii = 0; ii < transaction.Requests.Count; ii++)
                    {
                        transaction.Requests[ii].Values.Clear();

                        if (transaction.Requests[ii].ModificationInfos != null)
                        {
                            transaction.Requests[ii].ModificationInfos.Clear();
                        }
                    }
                }

                // release continuation points.
                if (!done && session != null)
                {
                    ReadNext(session, transaction.Details, transaction.Requests, true);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error reading history.");
            }
            finally
            {
                // remove transaction.
                lock (Lock)
                {
                    m_transactions.Remove(transaction.CancelId);
                }
            }
        }

        /// <summary>
        /// Sends the callback for a read request.
        /// </summary>
        private bool SendCallback(
            int transactionId,
            TransationType transactionType,
            List<HdaReadRequest> requests)
        {
            try
            {
                IComHdaDataCallback callback = m_callback;

                if (callback == null)
                {
                    return false;
                }

                switch (transactionType)
                {
                    case TransationType.Read:
                    {
                        callback.OnReadComplete(transactionId, requests);
                        break;
                    }

                    case TransationType.ReadModified:
                    {
                        callback.OnReadModifiedComplete(transactionId, requests);
                        break;
                    }

                    case TransationType.ReadAttribute:
                    {
                        callback.OnReadAttributeComplete(transactionId, requests);
                        break;
                    }

                    case TransationType.ReadAnnotation:
                    {
                        callback.OnReadAnnotations(transactionId, requests);
                        break;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error sending callback to HDA client.");
                return false;
            }
        }
        #endregion
        
        #region Read Modified
        /// <summary>
        /// Reads the modified data.
        /// </summary>
        public List<HdaReadRequest> ReadModified(
            DateTime startTime, 
            DateTime endTime, 
            uint numValues, 
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();

            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            // create the read requests.
            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                numValues,
                false,
                serverHandles,
                details);

            details.IsReadModified = true;

            for (int ii = 0; ii < requests.Count; ii++)
            {
                requests[ii].ModificationInfos = new List<ModificationInfo>();
            }

            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadNext(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }
            
            return requests;
        }

        /// <summary>
        /// Reads the modified data.
        /// </summary>
        public int[] ReadModified(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            uint numValues,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();
            
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            // create the read requests.
            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                numValues,
                false,
                serverHandles,
                details);

            details.IsReadModified = true;

            for (int ii = 0; ii < requests.Count; ii++)
            {
                requests[ii].ModificationInfos = new List<ModificationInfo>();
            }

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.ReadModified,
                transactionId, 
                new ExtensionObject(details),
                requests,
                false,
                out cancelId);

            // return the initial results.
            return errors;
        }
        #endregion

        #region Read Processed
        /// <summary>
        /// Reads the processed data.
        /// </summary>
        public List<HdaReadRequest> ReadProcessed(
            DateTime startTime,
            DateTime endTime,
            long resampleInterval,
            int[] serverHandles,
            uint[] aggregateIds)
        {
            Session session = ThrowIfNotConnected();
            
            // create the read requests.
            ReadProcessedDetails details = new ReadProcessedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                resampleInterval,
                serverHandles,
                aggregateIds,
                details);
            
            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadNext(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }

            
            return requests;
        }

        /// <summary>
        /// Reads the processed data.
        /// </summary>
        public int[] ReadProcessed(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            long resampleInterval,
            int[] serverHandles,
            uint[] aggregateIds,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();

            // create the read requests.
            ReadProcessedDetails details = new ReadProcessedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                resampleInterval,
                serverHandles,
                aggregateIds,
                details);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.Read,
                transactionId, 
                new ExtensionObject(details),
                requests,
                false,
                out cancelId);

            // return the initial results.
            return errors;
        }
                
        /// <summary>
        /// Creates the read requests.
        /// </summary>
        private List<HdaReadRequest> CreateReadRequests(
            Session session,
            DateTime startTime,
            DateTime endTime,
            double resampleInterval,
            int[] serverHandles, 
            uint[] aggregateIds,
            ReadProcessedDetails details)
        {
            // start time or end time must be specified.
            if (startTime == DateTime.MinValue || endTime == DateTime.MinValue || startTime == endTime || resampleInterval < 0)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // check the number of intervals.
            if (m_configuration.MaxReturnValues > 0 && resampleInterval != 0)
            {
                long range = Math.Abs(((endTime - startTime).Ticks));

                if (range/(TimeSpan.TicksPerMillisecond*resampleInterval) > m_configuration.MaxReturnValues)
                {
                    throw ComUtils.CreateComException(ResultIds.E_MAXEXCEEDED);
                }
            }
            
            details.StartTime = startTime;
            details.EndTime = endTime;
            details.ProcessingInterval = resampleInterval;

            // build the list of requests.
            List<HdaReadRequest> requests = new List<HdaReadRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaReadRequest request = new HdaReadRequest();
                requests.Add(request);

                // initialize request.
                request.AggregateId = aggregateIds[ii];

                // look up server handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }
                
                // set node id to use.
                request.NodeId = request.Handle.NodeId;
                request.ClientHandle = request.Handle.ClientHandle;

                // check aggregate.
                NodeId aggregateId = ComUtils.GetHdaAggregateId(aggregateIds[ii]);

                if (aggregateId == null)
                {
                    aggregateId = m_mapper.GetRemoteIntegerIdMapping(Opc.Ua.BrowseNames.AggregateFunctions, aggregateIds[ii]);
                }

                if (aggregateId == null)
                {
                    request.Error = ResultIds.E_NOT_AVAIL;
                    continue;
                }

                details.AggregateType.Add(aggregateId);
            }

            return requests;
        }
        #endregion

        #region Advise Processed
        #endregion
        
        #region Read At Time
        /// <summary>
        /// Reads the data at the specified times.
        /// </summary>
        public List<HdaReadRequest> ReadAtTime(
            DateTime[] timestamps,
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();
            
            // create the read requests.
            ReadAtTimeDetails details = new ReadAtTimeDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                timestamps,
                serverHandles,
                details);

            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadNext(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }
            
            return requests;
        }

        /// <summary>
        /// Reads the data at the specified times.
        /// </summary>
        public int[] ReadAtTime(
            int transactionId,
            DateTime[] timestamps,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();

            // create the read requests.
            ReadAtTimeDetails details = new ReadAtTimeDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                timestamps,
                serverHandles,
                details);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.Read,
                transactionId, 
                new ExtensionObject(details),
                requests,
                false,
                out cancelId);

            // return the initial results.
            return errors;
        }
                
        /// <summary>
        /// Creates the read requests.
        /// </summary>
        private List<HdaReadRequest> CreateReadRequests(
            Session session,
            DateTime[] timestamps,
            int[] serverHandles, 
            ReadAtTimeDetails details)
        {
            if (m_configuration.MaxReturnValues > 0 && timestamps.Length > m_configuration.MaxReturnValues)
            {
                throw ComUtils.CreateComException(ResultIds.E_MAXEXCEEDED);
            }

            details.ReqTimes.AddRange(timestamps);

            // build the list of requests.
            List<HdaReadRequest> requests = new List<HdaReadRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaReadRequest request = new HdaReadRequest();
                requests.Add(request);

                // look up server handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }
                
                // set node id to use.
                request.NodeId = request.Handle.NodeId;
                request.ClientHandle = request.Handle.ClientHandle;
            }

            return requests;
        }
        #endregion

        #region Read Annotations
        /// <summary>
        /// Reads the annotations.
        /// </summary>
        public List<HdaReadRequest> ReadAnnotations(
            DateTime startTime,
            DateTime endTime,
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();
            
            // create the read requests.
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                serverHandles,
                details);

            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadNext(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }
            
            return requests;
        }

        /// <summary>
        /// Reads the annotations.
        /// </summary>
        public int[] ReadAnnotations(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();

            // create the read requests.
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                serverHandles,
                details);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.ReadAnnotation,
                transactionId, 
                new ExtensionObject(details),
                requests,
                false,
                out cancelId);

            // return the initial results.
            return errors;
        }
                
        /// <summary>
        /// Creates the read requests.
        /// </summary>
        private List<HdaReadRequest> CreateReadRequests(
            Session session,
            DateTime startTime,
            DateTime endTime,
            int[] serverHandles, 
            ReadRawModifiedDetails details)
        {
            if (startTime == DateTime.MinValue || endTime == DateTime.MinValue)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // specify the parameters for the request.
            details.StartTime = startTime;
            details.EndTime = endTime;
            details.IsReadModified = false;
            details.NumValuesPerNode = 0;
            details.ReturnBounds = false;

            // build the list of requests.
            List<HdaReadRequest> requests = new List<HdaReadRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaReadRequest request = new HdaReadRequest();
                requests.Add(request);

                // initialize request.
                request.AttributeId = INTERNAL_ATTRIBUTE_ANNOTATION;
                
                // look up server handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;
                
                // look up annotation property.
                NodeId annotationsId = m_itemManager.GetAnnotationsPropertyNodeId(session, request.Handle);

                if (NodeId.IsNull(annotationsId))
                {
                    request.Error = ResultIds.S_NODATA;
                    continue;
                }

                // set node id.
                request.NodeId = annotationsId;
            }

            return requests;
        }
        #endregion
        
        #region Read Attributes
        /// <summary>
        /// Reads the attributes.
        /// </summary>
        public List<HdaReadRequest> ReadAttributes(
            DateTime startTime,
            DateTime endTime,
            int serverHandle,
            uint[] attributeIds)
        {
            Session session = ThrowIfNotConnected();
            
            // create the read requests.
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                serverHandle,
                attributeIds,
                details);

            ExtensionObject extension = new ExtensionObject(details);

            // fetch all of the values.
            if (ReadAttributes(session, extension, requests, false))
            {
                ReadNext(session, extension, requests, true);
            }
            
            return requests;
        }

        /// <summary>
        /// Reads the attributes.
        /// </summary>
        public int[] ReadAttributes(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            int serverHandle,
            uint[] attributeIds,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();

            // create the read requests.
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            List<HdaReadRequest> requests = CreateReadRequests(
                session,
                startTime,
                endTime,
                serverHandle,
                attributeIds,
                details);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.ReadAttribute,
                transactionId, 
                new ExtensionObject(details),
                requests,
                true,
                out cancelId);

            // return the initial results.
            return errors;
        }
                
        /// <summary>
        /// Creates the read requests.
        /// </summary>
        private List<HdaReadRequest> CreateReadRequests(
            Session session,
            DateTime startTime,
            DateTime endTime,
            int serverHandle, 
            uint[] attributeIds, 
            ReadRawModifiedDetails details)
        {
            if (startTime == DateTime.MinValue)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // check for the special case where current values are requested.
            if (endTime == DateTime.MinValue && startTime.AddSeconds(10) < DateTime.UtcNow)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // specify the parameters for the request.
            details.StartTime = startTime;
            details.EndTime = endTime;
            details.IsReadModified = false;
            details.NumValuesPerNode = 0;
            details.ReturnBounds = true;
            
            // look up server handle.
            HdaItemHandle handle = m_itemManager.LookupHandle(serverHandle);

            if (handle == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDHANDLE);
            }
            
            // get the node ids for the requested attributes.
            NodeId[] nodeIds = null;
            
            int[] errors = m_itemManager.GetAttributeHistoryNodeIds(
                session, 
                handle, 
                attributeIds,
                out nodeIds);

            // build the list of requests.
            List<HdaReadRequest> requests = new List<HdaReadRequest>();

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                HdaReadRequest request = new HdaReadRequest();
                requests.Add(request);

                // initialize request.
                request.Handle = handle;
                request.AttributeId = attributeIds[ii];

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;
                
                // check for errors.
                if (errors[ii] != ResultIds.S_OK)
                {
                    request.Error = errors[ii];
                    request.IsComplete = true;
                    continue;
                }

                request.NodeId = nodeIds[ii];
            }

            return requests;
        }

        /// <summary>
        /// Reads the historical or current value for attributes.
        /// </summary>
        private bool ReadAttributes(
            Session session,
            ExtensionObject extension,
            List<HdaReadRequest> requests,
            bool releaseContinuationPoints)
        {
            ReadRawModifiedDetails details = extension.Body as ReadRawModifiedDetails;

            if (details == null)
            {
                return false;
            }

            // check if reading historical values.
            if (details.EndTime != DateTime.MinValue)
            {
                return ReadNext(session, extension, requests, false);
            }

            HdaItemHandle handle = null;
            List<uint> attributeIds = new List<uint>();
            List<int> indexes = new List<int>();
            
            // build the list of requests.
            for (int ii = 0; ii < requests.Count; ii++)
            {
                HdaReadRequest request = requests[ii];

                if (request.Error < 0)
                {
                    continue;
                }

                // handle should be the same for all.
                if (handle == null)
                {
                    handle = request.Handle;
                }

                attributeIds.Add(request.AttributeId);
                indexes.Add(ii);
            }

            // check if nothing to do.
            if (attributeIds.Count == 0)
            {
                return false;
            }

            // reads the current values for all requested attributes.
            DaValue[] values = m_itemManager.ReadCurrentValues(session, handle, attributeIds.ToArray());

            // build the list of requests.
            for (int ii = 0; ii < attributeIds.Count; ii++)
            {
                HdaReadRequest request = requests[indexes[ii]];

                if (values[ii].Error < 0)
                {
                    request.Error = values[ii].Error;
                }
                else
                {
                    request.Values = new List<DaValue>();
                    request.Values.Add(values[ii]);
                }

                request.IsComplete = true;
            }
                
            return false;
        }
        #endregion

        #region Update Raw
        /// <summary>
        /// Updates the history.
        /// </summary>
        public int[] UpdateRaw(
            PerformUpdateType updateType,
            int[] serverHandles,
            DaValue[] values)
        {
            Session session = ThrowIfNotConnected();
            
            // create the update requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                updateType,
                serverHandles,
                values);

            // update the server.
            return UpdateHistory(session, requests, true);
        }
                
        /// <summary>
        /// Updates the history.
        /// </summary>
        public int[] UpdateRaw(
            int transactionId,
            PerformUpdateType updateType,
            int[] serverHandles,
            DaValue[] values,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();
            
            // create the update requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                updateType,
                serverHandles,
                values);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.Update,
                transactionId, 
                requests,
                out cancelId);

            // return the initial results.
            return errors;
        }

        /// <summary>
        /// Creates the update requests.
        /// </summary>
        private List<HdaUpdateRequest> CreateUpdateRequests(
            Session session,
            PerformUpdateType updateType,
            int[] serverHandles,
            DaValue[] values)
        {
            List<HdaUpdateRequest> requests = new List<HdaUpdateRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaUpdateRequest request = new HdaUpdateRequest();
                requests.Add(request);

                // find handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;

                // check if nothing to do.
                DaValue value = values[ii];

                if (value == null)
                {
                    request.Error = ResultIds.E_FAIL;
                    continue;
                }

                // specify the parameters for the request.
                UpdateDataDetails details = new UpdateDataDetails();

                details.NodeId = request.Handle.NodeId;
                details.PerformInsertReplace = updateType;

                DataValue value2 = m_mapper.GetRemoteDataValue(value, m_itemManager.GetRemoteDataType(request.Handle));

                value2.SourceTimestamp = value.Timestamp;
                value2.StatusCode = ComUtils.GetHdaQualityCode(value.HdaQuality);

                details.UpdateValues.Add(value2);

                request.Details = new ExtensionObject(details);
            }

            return requests;
        }
        
        /// <summary>
        /// Calls the server and updates the history.
        /// </summary>
        private int[] UpdateHistory(
            Session session,
            List<HdaUpdateRequest> requests,
            bool checkOperationError)
        {
            int[] errors = new int[requests.Count];

            // build list of nodes to update.
            ExtensionObjectCollection nodesToUpdate = new ExtensionObjectCollection();
            List<int> indexes = new List<int>();

            for (int ii = 0; ii < requests.Count; ii++)
            {
                if (requests[ii].Error < 0)
                {
                    errors[ii] = requests[ii].Error;
                    continue;
                }

                // check if nothing to do.
                if (requests[ii].Details == null)
                {
                    continue;
                }

                nodesToUpdate.Add(requests[ii].Details);
                indexes.Add(ii);
            }

            if (nodesToUpdate.Count == 0)
            {
                return errors;
            }

            // call the server.
            HistoryUpdateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.HistoryUpdate(
                null,
                nodesToUpdate,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToUpdate);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            // process results.
            for (int ii = 0; ii < nodesToUpdate.Count; ii++)
            {
                HdaUpdateRequest request = requests[indexes[ii]];

                request.Error = MapUpdateStatusToErrorCode(results[ii].StatusCode);

                if (checkOperationError)
                {
                    if (StatusCode.IsGood(results[ii].StatusCode))
                    {
                        request.Error = MapUpdateStatusToErrorCode(results[ii].OperationResults[0]);
                    }
                }

                errors[indexes[ii]] = request.Error;
            }

            return errors;
        }

        /// <summary>
        /// Creates and queues a new transaction.
        /// </summary>
        private int[] CreateTransaction(
            TransationType transationType,
            int transactionId,
            List<HdaUpdateRequest> requests,
            out int cancelId)
        {

            lock (Lock)
            {
                cancelId = ++m_lastCancelId;

                // create the transaction.
                UpdateRequestTransaction transaction = new UpdateRequestTransaction();

                transaction.TransationType = transationType;
                transaction.TransactionId = transactionId;
                transaction.CancelId = cancelId;
                transaction.Requests = new List<HdaUpdateRequest>();

                // keep only the valid requests.
                int[] errors = new int[requests.Count];

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    errors[ii] = requests[ii].Error;

                    if (errors[ii] < 0)
                    {
                        continue;
                    }

                    transaction.Requests.Add(requests[ii]);
                }
                
                // queue the transaction.
                if (transaction.Requests.Count > 0)
                {
                    m_transactions.Add(transaction.CancelId, transaction);
                    ThreadPool.QueueUserWorkItem(DoUpdate, transaction);
                }

                // return the error list.
                return errors;
            }
        }

        /// <summary>
        /// Executes an asynchronous update operation.
        /// </summary>
        private void DoUpdate(object state)
        {
            Session session = null;
            UpdateRequestTransaction transaction = (UpdateRequestTransaction)state;

            try
            {
                // check if the transaction is still active.
                lock (Lock)
                {
                    if (!m_transactions.ContainsKey(transaction.CancelId))
                    {
                        return;
                    }

                    session = Session;
                }

                // check for session error.
                if (session == null)
                {
                    for (int ii = 0; ii < transaction.Requests.Count; ii++)
                    {
                        transaction.Requests[ii].Error = ResultIds.E_FAIL;
                    }

                    SendCallback(transaction.TransactionId, transaction.TransationType, transaction.Requests);
                    return;
                }

                // send request to the server.
                try
                {
                    switch (transaction.TransationType)
                    {
                        case TransationType.Update:
                        {
                            UpdateHistory(session, transaction.Requests, true);
                            break;
                        }

                        case TransationType.DeleteAtTime:
                        case TransationType.DeleteRaw:
                        case TransationType.InsertAnnotation:
                        {
                            UpdateHistory(session, transaction.Requests, false);
                            break;
                        }
                    }
                }

                // handle network error.
                catch (Exception)
                {
                    for (int ii = 0; ii < transaction.Requests.Count; ii++)
                    {
                        transaction.Requests[ii].Error = ResultIds.E_FAIL;
                    }
                }

                // send data to client.
                SendCallback(transaction.TransactionId, transaction.TransationType, transaction.Requests);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating history.");
            }
            finally
            {
                // remove transaction.
                lock (Lock)
                {
                    m_transactions.Remove(transaction.CancelId);
                }
            }
        }
        
        /// <summary>
        /// Sends the callback for a read request.
        /// </summary>
        private bool SendCallback(
            int transactionId,
            TransationType transactionType,
            List<HdaUpdateRequest> requests)
        {
            try
            {
                IComHdaDataCallback callback = m_callback;

                if (callback == null)
                {
                    return false;
                }

                switch (transactionType)
                {
                    case TransationType.Update:
                    case TransationType.DeleteRaw:
                    case TransationType.DeleteAtTime:
                    {
                        callback.OnUpdateComplete(transactionId, requests);
                        break;
                    }

                    case TransationType.InsertAnnotation:
                    {
                        callback.OnInsertAnnotations(transactionId, requests);
                        break;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error sending callback to HDA client.");
                return false;
            }
        }
        #endregion
        
        #region Delete At Time
        /// <summary>
        /// Deletes the data at the specified times.
        /// </summary>
        public int[] DeleteAtTime(
            DateTime[] timestamps,
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();
            
            // create the delete requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                timestamps,
                serverHandles);

            // delete the values.
            return UpdateHistory(session, requests, false);
        }

        /// <summary>
        /// Deletes the data at the specified times.
        /// </summary>
        public int[] DeleteAtTime(
            int transactionId,
            DateTime[] timestamps,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();

            // create the read requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                timestamps,
                serverHandles);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.DeleteAtTime,
                transactionId, 
                requests,
                out cancelId);

            // return the initial results.
            return errors;
        }
                
        /// <summary>
        /// Creates the update requests.
        /// </summary>
        private List<HdaUpdateRequest> CreateUpdateRequests(
            Session session,
            DateTime[] timestamps,
            int[] serverHandles)
        {
            List<HdaUpdateRequest> requests = new List<HdaUpdateRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaUpdateRequest request = new HdaUpdateRequest();
                requests.Add(request);

                // find handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;

                // specify the parameters for the request.
                DeleteAtTimeDetails details = new DeleteAtTimeDetails();

                details.NodeId = request.Handle.NodeId;
                details.ReqTimes.AddRange(timestamps);

                request.Details = new ExtensionObject(details);
            }

            return requests;
        }
        #endregion
        
        #region Delete Raw
        /// <summary>
        /// Deletes the raw data.
        /// </summary>
        public int[] DeleteRaw(
            DateTime startTime, 
            DateTime endTime, 
            int[] serverHandles)
        {
            Session session = ThrowIfNotConnected();
            
            // create the delete requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                startTime,
                endTime,
                serverHandles);

            // delete the values.
            return UpdateHistory(session, requests, false);
        }

        /// <summary>
        /// Deletes the raw data.
        /// </summary>
        public int[] DeleteRaw(
            int transactionId,
            DateTime startTime,
            DateTime endTime,
            int[] serverHandles,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();
            
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            // create the delete requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                startTime,
                endTime,
                serverHandles);
            
            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.DeleteRaw,
                transactionId, 
                requests,
                out cancelId);

            // return the initial results.
            return errors;
        }

        /// <summary>
        /// Creates the update requests.
        /// </summary>
        private List<HdaUpdateRequest> CreateUpdateRequests(
            Session session,
            DateTime startTime,
            DateTime endTime,
            int[] serverHandles)
        {
            if (startTime == DateTime.MinValue || endTime == DateTime.MinValue)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            List<HdaUpdateRequest> requests = new List<HdaUpdateRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaUpdateRequest request = new HdaUpdateRequest();
                requests.Add(request);

                // find handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;

                // specify the parameters for the request.
                DeleteRawModifiedDetails details = new DeleteRawModifiedDetails();

                details.NodeId = request.Handle.NodeId;
                details.IsDeleteModified = false;
                details.StartTime = startTime;
                details.EndTime = endTime;

                request.Details = new ExtensionObject(details);
            }

            return requests;
        }
        #endregion

        #region Insert Annotations
        /// <summary>
        /// Inserts the annotations.
        /// </summary>
        public int[] InsertAnnotations(
            int[] serverHandles,
            DateTime[] timestamps,
            Annotation[][] annotations)
        {
            Session session = ThrowIfNotConnected();
            
            // create the update requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                serverHandles,
                timestamps,
                annotations);

            // update the server.
            return UpdateHistory(session, requests, false);
        }

        /// <summary>
        /// Inserts the annotations.
        /// </summary>
        public int[] InsertAnnotations(
            int transactionId,
            int[] serverHandles,
            DateTime[] timestamps,
            Annotation[][] annotations,
            out int cancelId)
        {
            Session session = ThrowIfNotConnected();
            
            // create the update requests.
            List<HdaUpdateRequest> requests = CreateUpdateRequests(
                session,
                serverHandles,
                timestamps,
                annotations);

            // queue the transaction.
            int[] errors = CreateTransaction(
                TransationType.InsertAnnotation,
                transactionId, 
                requests,
                out cancelId);

            // return the initial results.
            return errors;
        }

        /// <summary>
        /// Creates the update requests.
        /// </summary>
        private List<HdaUpdateRequest> CreateUpdateRequests(
            Session session,
            int[] serverHandles,
            DateTime[] timestamps,
            Annotation[][] annotations)
        {
            List<HdaUpdateRequest> requests = new List<HdaUpdateRequest>();

            for (int ii = 0; ii < serverHandles.Length; ii++)
            {
                HdaUpdateRequest request = new HdaUpdateRequest();
                requests.Add(request);

                // find handle.
                request.Handle = m_itemManager.LookupHandle(serverHandles[ii]);

                if (request.Handle == null)
                {
                    request.Error = ResultIds.E_INVALIDHANDLE;
                    continue;
                }

                request.ClientHandle = request.Handle.ClientHandle;

                // check if nothing to do.
                Annotation[] list = annotations[ii];

                if (list == null || list.Length == 0)
                {
                    request.Error = ResultIds.S_OK;
                    continue;
                }

                // get the annotations proprerty.
                NodeId annotationsId = m_itemManager.GetAnnotationsPropertyNodeId(session, request.Handle);

                if (NodeId.IsNull(annotationsId))
                {
                    request.Error = ResultIds.E_FAIL;
                    continue;
                }

                // specify the parameters for the request.
                UpdateDataDetails details = new UpdateDataDetails();

                details.NodeId = annotationsId;
                details.PerformInsertReplace = PerformUpdateType.Insert;

                for (int jj = 0; jj < list.Length; jj++)
                {
                    DataValue value = new DataValue();
                    value.WrappedValue = new ExtensionObject(list[jj]);
                    value.SourceTimestamp = timestamps[ii];
                    details.UpdateValues.Add(value);                    
                }

                request.Details = new ExtensionObject(details);
            }

            return requests;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the next batch of values from the server.
        /// </summary>
        private bool ReadNext(
            Session session,
            ExtensionObject details,
            List<HdaReadRequest> requests,
            bool releaseContinuationPoints)
        {
            // get the value.
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();

            for (int ii = 0; ii < requests.Count; ii++)
            {
                HdaReadRequest request = requests[ii];

                if (request.IsComplete || request.Error < 0)
                {
                    continue;
                }

                if (NodeId.IsNull(request.NodeId))
                {
                    request.Error = ResultIds.S_NODATA;
                    continue;
                }

                HistoryReadValueId nodeToRead = new HistoryReadValueId();
                nodeToRead.NodeId = request.NodeId;
                nodeToRead.ContinuationPoint = request.ContinuationPoint;
                nodeToRead.Handle = request;
                nodesToRead.Add(nodeToRead);
            }

            // check if something to do.
            if (nodesToRead.Count == 0)
            {
                return false;
            }

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            session.HistoryRead(
                null,
                details,
                TimestampsToReturn.Source,
                releaseContinuationPoints,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // check if nothing more to do.
            if (releaseContinuationPoints)
            {
                return false;
            }

            // process results.
            bool continuationPoints = false;

            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                HdaReadRequest request = (HdaReadRequest)nodesToRead[ii].Handle;

                if (request.Values == null)
                {
                    request.Values = new List<DaValue>();
                }

                request.Error = ProcessReadResults(
                    session, 
                    results[ii], 
                    request.AttributeId, 
                    request.Values, 
                    request.ModificationInfos);
                
                request.ContinuationPoint = results[ii].ContinuationPoint;

                // check if continuation point provided.
                if (request.ContinuationPoint != null && request.ContinuationPoint.Length > 0)
                {
                    request.Error = ResultIds.S_MOREDATA;
                    continuationPoints = true;
                }
                else
                {
                    request.IsComplete = true;
                }
            }

            return continuationPoints;
        }

        /// <summary>
        /// Processes the results of a history read operation.
        /// </summary>
        private int ProcessReadResults(
            Session session,
            HistoryReadResult result, 
            uint attributeId,
            List<DaValue> values,
            List<ModificationInfo> modificationInfos)
        {
            // check for item level error.
            int error = MapReadStatusToErrorCode(result.StatusCode);

            if (error < 0)
            {
                return error;
            }

            // check if no data found.
            if (result.StatusCode == StatusCodes.GoodNoData)
            {
                return ResultIds.S_NODATA;
            }

            // extract the history data.
            HistoryData data = ExtensionObject.ToEncodeable(result.HistoryData) as HistoryData;

            if (data == null)
            {
                return ResultIds.E_FAIL;
            }
            
            // check for modified data.
            HistoryModifiedData modifiedData = data as HistoryModifiedData;

            if (modificationInfos != null)
            {
                if (modifiedData == null)
                {
                    return ResultIds.E_FAIL;
                }

                modificationInfos.AddRange(modifiedData.ModificationInfos);
            }
            
            // convert the values.
            for (int ii = 0; ii < data.DataValues.Count; ii++)
            {
                DaValue value = GetAttributeValue(session, m_mapper, attributeId, data.DataValues[ii]);
                values.Add(value);

                // ensure matching modification info record exists.
                if (modificationInfos != null)
                {
                    if (modifiedData == null || ii >= modifiedData.ModificationInfos.Count)
                    {
                        modificationInfos.Add(new ModificationInfo());
                    }
                    else
                    {
                        modificationInfos.Add(modifiedData.ModificationInfos[ii]);
                    }
                }
            }

            // check if no data found.
            if (result.StatusCode == StatusCodes.GoodMoreData)
            {
                return ResultIds.S_MOREDATA;
            }

            return ResultIds.S_OK;
        }
        #endregion
                
        #region Static Functions
        /// <summary>
        /// Converts a UA value to an HDA attribute value.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="mapper">The mapper.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        internal static DaValue GetAttributeValue(Session session, ComNamespaceMapper mapper, uint attributeId, DataValue value)
        {
            switch (attributeId)
            {
                case INTERNAL_ATTRIBUTE_ANNOTATION:
                {
                    DaValue result = new DaValue();

                    Annotation annotation = value.GetValue<Annotation>(null);

                    if (annotation == null)
                    {
                        result.Error = ResultIds.E_BADTYPE;
                        return result;
                    }

                    result.Value = annotation;
                    result.HdaQuality = ComUtils.GetHdaQualityCode(value.StatusCode);
                    result.Timestamp = value.SourceTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                case Constants.OPCHDA_ENG_UNITS:
                {
                    DaValue result = new DaValue();

                    EUInformation engineeringUnits = value.GetValue<EUInformation>(null);

                    if (engineeringUnits == null)
                    {
                        result.Error = ResultIds.E_INVALIDATTRID;
                        return result;
                    }

                    if (engineeringUnits.DisplayName != null)
                    {
                        result.Value = engineeringUnits.DisplayName.Text;
                    }

                    result.HdaQuality = ComUtils.GetHdaQualityCode(value.StatusCode);
                    result.Timestamp = value.SourceTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                case Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                case Constants.OPCHDA_NORMAL_MAXIMUM:
                {
                    DaValue result = new DaValue();

                    Range range = value.GetValue<Range>(null);

                    if (range == null)
                    {
                        result.Error = ResultIds.E_INVALIDATTRID;
                        return result;
                    }

                    result.Value = range.High;
                    result.HdaQuality = ComUtils.GetHdaQualityCode(value.StatusCode);
                    result.Timestamp = value.SourceTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                case Constants.OPCHDA_LOW_ENTRY_LIMIT:
                case Constants.OPCHDA_NORMAL_MINIMUM:
                {
                    DaValue result = new DaValue();

                    Range range = value.GetValue<Range>(null);

                    if (range == null)
                    {
                        result.Error = ResultIds.E_INVALIDATTRID;
                        return result;
                    }

                    result.Value = range.Low;
                    result.HdaQuality = ComUtils.GetHdaQualityCode(value.StatusCode);
                    result.Timestamp = value.SourceTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                case Constants.OPCHDA_MAX_TIME_INT:
                case Constants.OPCHDA_MIN_TIME_INT:
                {
                    DaValue result = new DaValue();

                    int error = ComHdaProxy.MapReadStatusToErrorCode(value.StatusCode);

                    if (error < 0)
                    {
                        result.Error = error;
                        return result;
                    }

                    // need to support the VT_CY type.
                    result.Value = (decimal)value.GetValue<double>(0);
                    result.HdaQuality = ComUtils.GetHdaQualityCode(value.StatusCode);
                    result.Timestamp = value.SourceTimestamp;
                    result.Error = ResultIds.S_OK;

                    return result;
                }

                default:
                case Constants.OPCHDA_ITEMID:
                case Constants.OPCHDA_ARCHIVING:
                case Constants.OPCHDA_STEPPED:
                case Constants.OPCHDA_EXCEPTION_DEV:
                case Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                case Constants.OPCHDA_DERIVE_EQUATION:
                {
                    return mapper.GetLocalDataValue(value);
                }
            }
        }

        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        internal static int MapPropertyReadStatusToErrorCode(StatusCode statusCode)
        {
            // map good status.
            if (StatusCode.IsGood(statusCode))
            {
                return ResultIds.S_OK;
            }

            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNotReadable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadTypeMismatch: { return ResultIds.E_BADTYPE; }
                }

                return ResultIds.E_FAIL;
            }

            // uncertain values for property reads are errors.
            return ResultIds.E_FAIL;
        }

        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        internal static int MapReadStatusToErrorCode(StatusCode statusCode)
        {
            // map bad well known status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotReadable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadUnexpectedError: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadInternalError: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadSessionClosed: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadTypeMismatch: { return ResultIds.E_BADTYPE; }
                }
            }

            // all other values are mapped to quality codes.
            return ResultIds.S_OK;
        }

        /// <summary>
        /// Converts a StatusCode returned during a HistoryUpdate to an HRESULT.
        /// </summary>
        internal static int MapUpdateStatusToErrorCode(StatusCode statusCode)
        {
            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotWritable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadHistoryOperationInvalid: { return ResultIds.E_NOTSUPPORTED; }
                    case StatusCodes.BadHistoryOperationUnsupported: { return ResultIds.E_NOTSUPPORTED; }
                    case StatusCodes.BadOutOfRange: { return ResultIds.E_RANGE; }
                    case StatusCodes.BadEntryExists: { return ResultIds.E_DATAEXISTS; }
                    case StatusCodes.BadNoEntryExists: { return ResultIds.E_NODATAEXISTS; }
                }

                return ResultIds.E_FAIL;
            }

            // ignore uncertain and success codes.
            return ResultIds.S_OK;
        }
        #endregion

        #region Transaction Class
        /// <summary>
        /// The parameters for an asynchronous transaction.
        /// </summary>
        private class Transaction
        {
            public TransationType TransationType;
            public int TransactionId;
            public int CancelId;
        }
        #endregion

        #region ReadRequestTransaction Class
        /// <summary>
        /// The parameters for a read request.
        /// </summary>
        private class ReadRequestTransaction : Transaction
        {
            public ExtensionObject Details;
            public List<HdaReadRequest> Requests;
        }
        #endregion

        #region UpdateRequestTransaction Class
        /// <summary>
        /// The parameters for an update request.
        /// </summary>
        private class UpdateRequestTransaction : Transaction
        {
            public List<HdaUpdateRequest> Requests;
        }
        #endregion

        #region TransationType Enumeration
        /// <summary>
        /// The possible transaction types.
        /// </summary>
        private enum TransationType
        {
            Read,
            ReadModified,
            ReadAttribute,
            ReadAnnotation,
            Update,
            DeleteRaw,
            DeleteAtTime,
            InsertAnnotation,
            Cancel
        }
        #endregion

        #region Private Fields
        internal const uint INTERNAL_ATTRIBUTE_ANNOTATION = 1000;
        internal const uint INTERNAL_ATTRIBUTE_VALUE_RANK = 1001;

        private ComHdaProxyConfiguration m_configuration;
        private ComNamespaceMapper m_mapper;
        private ComDaBrowseCache m_browseCacheManager;
        private ComDaBrowseManager m_browseManager;
        private ComHdaItemManager m_itemManager;
        private List<HdaAggregate> m_aggregates;
        private IComHdaDataCallback m_callback;
        private Dictionary<int, Transaction> m_transactions;
        private int m_lastCancelId;
        #endregion
	}
}
