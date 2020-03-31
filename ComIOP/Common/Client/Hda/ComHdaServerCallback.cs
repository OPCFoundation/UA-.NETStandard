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
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A class that implements the IOPCHDA_DataCallback interface.
    /// </summary>
    internal class ComHdaDataCallback : OpcRcw.Hda.IOPCHDA_DataCallback, IDisposable
    {
	    #region Constructors
	    /// <summary>
	    /// Initializes the object with the containing subscription object.
	    /// </summary>
        public ComHdaDataCallback(ComHdaClient server)
	    { 
            // save group.
            m_server = server;

		    // create connection point.
            m_connectionPoint = new ConnectionPoint(server.Unknown, typeof(OpcRcw.Hda.IOPCHDA_DataCallback).GUID);

		    // advise.
		    m_connectionPoint.Advise(this);
	    }
	    #endregion
        
        #region IDisposable Members        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (m_connectionPoint != null)
            {
                if (disposing)
                {
                    m_connectionPoint.Dispose();
                    m_connectionPoint = null;
                }
            }
        }
        #endregion

	    #region Public Properties
        /// <summary>
        /// Whether the callback is connected.
        /// </summary>
        public bool Connected 
        {
            get 
            {
                return m_connectionPoint != null;
            }
        }
        #endregion

	    #region IOPCDataCallback Members
        /// <summary>
        /// Called when a data change arrives.
        /// </summary>
        public void OnDataChange(
            int dwTransactionID,
            int hrStatus,
            int dwNumItems,
            OPCHDA_ITEM[] pItemValues,
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnDataChange callback.");
            }
        }

        /// <summary>
        /// Called when an async read completes.
        /// </summary>
        public void OnReadComplete(
            int dwTransactionID, 
            int hrStatus,
            int dwNumItems,  
            OPCHDA_ITEM[] pItemValues,
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnReadComplete callback.");
            }
        }

        /// <summary>
        /// Called when an async read modified completes.
        /// </summary>
        public void OnReadModifiedComplete(
            int dwTransactionID, 
            int hrStatus,
            int dwNumItems, 
            OPCHDA_MODIFIEDITEM[] pItemValues,
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnReadModifiedComplete callback.");
            }
        }

        /// <summary>
        /// Called when an async read attributes completes.
        /// </summary>
        public void OnReadAttributeComplete(
            int dwTransactionID, 
            int hrStatus,
            int hClient, 
            int dwNumItems, 
            OPCHDA_ATTRIBUTE[] pAttributeValues,
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnReadAttributeComplete callback.");
            }
        }
        
        /// <summary>
        /// Called when an async read annotations completes.
        /// </summary>
        public  void OnReadAnnotations(
            int dwTransactionID, 
            int hrStatus,
            int dwNumItems, 
            OPCHDA_ANNOTATION[] pAnnotationValues,
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnReadAnnotations callback.");
            }
        }

        /// <summary>
        /// Called when an async insert annotations completes.
        /// </summary>
        public void OnInsertAnnotations (
            int dwTransactionID, 
            int hrStatus,
            int dwCount, 
            int[] phClients, 
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnInsertAnnotations callback.");
            }
        }
        
        /// <summary>
        /// Called when a playback result arrives.
        /// </summary>
        public void OnPlayback (
            int dwTransactionID, 
            int hrStatus,
            int dwNumItems, 
            IntPtr ppItemValues, 
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnPlayback callback.");
            }
        }
        
        /// <summary>
        /// Called when a async update completes.
        /// </summary>
        public void OnUpdateComplete (
            int dwTransactionID, 
            int hrStatus,
            int dwCount, 
            int[] phClients, 
            int[] phrErrors)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnUpdateComplete callback.");
            }
        }

        /// <summary>
        /// Called when a async opeartion cancel completes.
        /// </summary>
        public void OnCancelComplete(
            int dwCancelID)
        {
            try
            {
                // TBD
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OnCancelComplete callback.");
            }
        }
	    #endregion

	    #region Private Members
	    private ComHdaClient m_server;
	    private ConnectionPoint m_connectionPoint;
	    #endregion
    }
}
