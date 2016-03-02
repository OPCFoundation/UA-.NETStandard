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

namespace Opc.Ua.Server
{    
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// An interface to a source that stores the event history associated many Objects or Views.
    /// </summary>
    [Obsolete("The IEventHistoryProducer interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IEventHistoryProducer
    {                        
        /// <summary>
        /// Reads events from the historian.
        /// </summary>
        void ReadEvents(
            OperationContext          context,
            ReadEventDetails          details,
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<RequestHandle>      handles,
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors);

        /// <summary>
        /// Updates events in the historian.
        /// </summary>
        void UpdateEvents(
            OperationContext           context,
            IList<RequestHandle>       handles,
            IList<UpdateEventDetails>  nodesToUpdate, 
            IList<HistoryUpdateResult> results, 
            IList<ServiceResult>       errors);
                
        /// <summary>
        /// Deletes events in the historian.
        /// </summary>
        void DeleteEvents(
            OperationContext           context,
            IList<RequestHandle>       handles,
            IList<DeleteEventDetails>  eventsToDelete, 
            IList<HistoryUpdateResult> results, 
            IList<ServiceResult>       errors);
    }
#endif
}
