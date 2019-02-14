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
using OpcRcw.Da;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A class that implements the IOPCDataCallback interface.
    /// </summary>
    internal class ComDaDataCallback : OpcRcw.Da.IOPCDataCallback, IDisposable
    {
	    #region Constructors
	    /// <summary>
	    /// Initializes the object with the containing subscription object.
	    /// </summary>
	    public ComDaDataCallback(ComDaGroup group)
	    { 
            // save group.
            m_group = group;

		    // create connection point.
		    m_connectionPoint = new ConnectionPoint(group.Unknown, typeof(OpcRcw.Da.IOPCDataCallback).GUID);

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
	    /// Called when a data changed event is received.
	    /// </summary>
	    public void OnDataChange(
		    int                  dwTransid,
		    int                  hGroup,
		    int                  hrMasterquality,
		    int                  hrMastererror,
		    int                  dwCount,
		    int[]                phClientItems,
		    object[]             pvValues,
		    short[]              pwQualities,
		    System.Runtime.InteropServices.ComTypes.FILETIME[] pftTimeStamps,
		    int[]                pErrors)
	    {
		    try
		    {
			    // unmarshal item values.
			    DaValue[] values = ComDaGroup.GetItemValues(
				    dwCount,
				    pvValues, 
				    pwQualities, 
				    pftTimeStamps, 
				    pErrors);

			    // invoke the callback.
			    m_group.OnDataChange(phClientItems, values);
		    }
		    catch (Exception e) 
		    { 
                Utils.Trace(e, "Unexpected error processing OnDataChange callback.");
		    }
	    }

	    /// <summary>
	    /// Called when an asynchronous read operation completes.
	    /// </summary>
	    public void OnReadComplete(
		    int                  dwTransid,
		    int                  hGroup,
		    int                  hrMasterquality,
		    int                  hrMastererror,
		    int                  dwCount,
		    int[]                phClientItems,
		    object[]             pvValues,
		    short[]              pwQualities,
		    System.Runtime.InteropServices.ComTypes.FILETIME[] pftTimeStamps,
		    int[]                pErrors)
	    {
		    try
		    {
			    // unmarshal item values.
			    DaValue[] values = ComDaGroup.GetItemValues(
				    dwCount,
				    pvValues, 
				    pwQualities, 
				    pftTimeStamps, 
				    pErrors);

			    // invoke the callback.
                m_group.OnReadComplete(dwTransid, phClientItems, values);
		    }
		    catch (Exception e) 
		    { 
                Utils.Trace(e, "Unexpected error processing OnReadComplete callback.");
		    }
	    }
        
	    /// <summary>
	    /// Called when an asynchronous write operation completes.
	    /// </summary>
	    public void OnWriteComplete(
		    int   dwTransid,
		    int   hGroup,
		    int   hrMastererror,
		    int   dwCount,
		    int[] phClientItems,
		    int[] pErrors)
	    {
		    try
		    {
                m_group.OnWriteComplete(dwTransid, phClientItems, pErrors);
		    }
		    catch (Exception e) 
		    { 
                Utils.Trace(e, "Unexpected error processing OnWriteComplete callback.");
		    }
	    }
                    
	    /// <summary>
	    /// Called when an asynchronous operation is cancelled.
	    /// </summary>
	    public void OnCancelComplete(
		    int dwTransid,
		    int hGroup)
	    {
	    }
	    #endregion

	    #region Private Members
	    private ComDaGroup m_group;
	    private ConnectionPoint m_connectionPoint;
	    #endregion
    }
}
