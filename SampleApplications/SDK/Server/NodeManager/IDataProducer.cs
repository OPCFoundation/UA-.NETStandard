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
using System.Reflection;
using System.Collections.Generic;

#pragma warning disable 0618

namespace Opc.Ua.Server
{    
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// An interface to an object that produces current data for multiple variables.
    /// </summary>
    [Obsolete("The IDataProducer interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IDataProducer
    {
        /// <summary>
        /// Reads the value for the variable from from the producer.
        /// </summary>
        /// <remarks>
        /// Returns true if the value/status/timestamp were changed.
        /// </remarks>
        bool ReadValue(
            NodeSource        source, 
            object            producerHandle,
            out object        value, 
            out DateTime      timestamp,
            out ServiceResult status);

        /// <summary>
        /// Writes the value for the variable from to the producer.
        /// </summary>
        ServiceResult WriteValue(
            NodeSource source, 
            object     producerHandle,
            object     value, 
            DateTime   timestamp, 
            StatusCode status);
    }
    
    /// <summary>
    /// An interface to an object that produces historical data for multiple variables.
    /// </summary>
    [Obsolete("The IDataHistoryProducer interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IDataHistoryProducer
    {                        
        /// <summary>
        /// Reads raw data from the historian.
        /// </summary>
        void ReadRaw(
            OperationContext          context,
            ReadRawModifiedDetails    details,
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<RequestHandle>      handles,
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors);

        /// <summary>
        /// Reads raw data from the historian at the specified time.
        /// </summary>
        void ReadAtTime(
            OperationContext          context,
            ReadAtTimeDetails         details,
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<RequestHandle>      handles,
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors);

        /// <summary>
        /// Reads processed data from the historian.
        /// </summary>
        void ReadProcessed(
            OperationContext          context,
            ReadProcessedDetails      details,
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<RequestHandle>      handles,
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors);

        /// <summary>
        /// Updates raw data in the historian.
        /// </summary>
        void UpdateRaw(
            OperationContext           context,
            IList<RequestHandle>       handles,
            IList<UpdateDataDetails>   nodesToUpdate, 
            IList<HistoryUpdateResult> results, 
            IList<ServiceResult>       errors);
        
        /// <summary>
        /// Deletes raw data in the historian.
        /// </summary>
        void DeleteRaw(
            OperationContext                context,
            IList<RequestHandle>            handles,
            IList<DeleteRawModifiedDetails> nodesToDelete, 
            IList<HistoryUpdateResult>      results, 
            IList<ServiceResult>            errors);
        
        /// <summary>
        /// Deletes raw data in the historian at the specified time.
        /// </summary>
        void DeleteAtTime(
            OperationContext           context,
            IList<RequestHandle>       handles,
            IList<DeleteAtTimeDetails> nodesToDelete, 
            IList<HistoryUpdateResult> results, 
            IList<ServiceResult>       errors);
    }
#endif
}
