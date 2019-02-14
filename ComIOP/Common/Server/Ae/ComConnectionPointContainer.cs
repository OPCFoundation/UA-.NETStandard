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
using System.Collections;
using System.Text;
using System.Runtime.InteropServices;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Server.Ae
{
	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
    public class ConnectionPointContainer : OpcRcw.Comn.IConnectionPointContainer
	{
		#region Public Members
		/// <summary>
		/// Called when a IConnectionPoint.Advise is called.
		/// </summary>
		public virtual void OnAdvise(Guid riid)
		{
			// does nothing.
		}

		/// <summary>
		/// Called when a IConnectionPoint.Unadvise is called.
		/// </summary>
		public virtual void OnUnadvise(Guid riid)
		{
			// does nothing.
		}
		#endregion

		#region Protected Members
		/// <summary>
		/// Initializes the object with default values.
		/// </summary>
		protected ConnectionPointContainer()
		{
			// does nothing.
		}

		/// <summary>
		/// Registers an interface as a connection point.
		/// </summary>
		protected void RegisterInterface(Guid iid)
		{
			m_connectionPoints[iid] = new ConnectionPoint(iid, this);
		}

		/// <summary>
		/// Unregisters an interface as a connection point.
		/// </summary>
		protected void UnregisterInterface(Guid iid)
		{
			m_connectionPoints.Remove(iid);
		}

		/// <summary>
		/// Returns the callback interface for the connection point (if currently connected).
		/// </summary>
		protected object GetCallback(Guid iid)
		{
			ConnectionPoint connectionPoint = (ConnectionPoint)m_connectionPoints[iid];

			if (connectionPoint != null)
			{
				return connectionPoint.Callback;
			}

			return null;
		}

		/// <summary>
		/// Whether a client has connected to the specified connection point.
		/// </summary>
		protected bool IsConnected(Guid iid)
		{
			ConnectionPoint connectionPoint = (ConnectionPoint)m_connectionPoints[iid];

			if (connectionPoint != null)
			{
				return connectionPoint.IsConnected;
			}

			return false;
		}
		#endregion

		#region IConnectionPointContainer Members
		/// <remarks/>
        public void EnumConnectionPoints(out OpcRcw.Comn.IEnumConnectionPoints ppenum)
		{
			lock (m_lock)
			{
				try
				{
					ppenum = new EnumConnectionPoints(m_connectionPoints.Values);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
        public void FindConnectionPoint(ref Guid riid, out OpcRcw.Comn.IConnectionPoint ppCP)
		{
			lock (m_lock)
			{
				try
				{
					ppCP = null;

					ConnectionPoint connectionPoint = (ConnectionPoint)m_connectionPoints[riid];

					if (connectionPoint == null)
					{
						throw new ExternalException("CONNECT_E_NOCONNECTION", ResultIds.CONNECT_E_NOCONNECTION);
					}

                    ppCP = connectionPoint as IConnectionPoint;
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
		#endregion

		#region Private Members
        private object m_lock = new object();
		private Hashtable m_connectionPoints = new Hashtable();
		#endregion
	}

	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
    public class ConnectionPoint : OpcRcw.Comn.IConnectionPoint
	{			
		/// <summary>
		/// Creates a connection point for the specified interface and container.
		/// </summary>
		public ConnectionPoint(Guid iid, ConnectionPointContainer container)
		{
			m_interface = iid;
			m_container = container;
		}

		/// <summary>
		/// The current callback object.
		/// </summary>
		public object Callback
		{
			get { return m_callback; }
		}

		/// <summary>
		/// Whether the client has connected to the connection point.
		/// </summary>
		public bool IsConnected
		{
			get { return m_callback != null; }
		}

        #region IConnectionPoint Members

        /// <remarks/>
        public void GetConnectionInterface(out Guid pIID)
        {
            lock (m_lock)
            {
                try
                {
                    pIID = m_interface;
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <remarks/>
        public void GetConnectionPointContainer(out OpcRcw.Comn.IConnectionPointContainer ppCPC)
        {
            lock (m_lock)
            {
                try
                {
                    ppCPC = m_container;
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
        }

		/// <remarks/>
		public void Advise(object pUnkSink, out int pdwCookie)
		{
			lock (m_lock)
			{
				try
				{
                    if (pUnkSink == null)
					{
						throw new ExternalException("E_POINTER", ResultIds.E_POINTER);
					}

					pdwCookie = 0;

					// check if an callback already exists.
					if (m_callback != null)
					{
						throw new ExternalException("CONNECT_E_ADVISELIMIT", ResultIds.CONNECT_E_ADVISELIMIT);
					}

                    m_callback = pUnkSink;
					pdwCookie  = ++m_cookie;

					// notify container.
					m_container.OnAdvise(m_interface);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
		public void Unadvise(int dwCookie)
		{
			lock (m_lock)
			{
				try
				{
					// not a valid connection id.
					if (m_cookie != dwCookie || m_callback == null)
					{
						throw new ExternalException("CONNECT_E_NOCONNECTION", ResultIds.CONNECT_E_NOCONNECTION);
					}

					// clear the callback.
                    Marshal.ReleaseComObject(m_callback);
					m_callback = null;

					// notify container.
					m_container.OnUnadvise(m_interface);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
        }

        /// <remarks/>
        public void EnumConnections(out OpcRcw.Comn.IEnumConnections ppenum)
        {
            throw new ExternalException("E_NOTIMPL", ResultIds.E_NOTIMPL);
        }
		#endregion

		#region Private Members
        private object m_lock = new object();
		private Guid m_interface = Guid.Empty;
		private ConnectionPointContainer m_container = null;
		private object m_callback = null;
		private int m_cookie = 0;
		#endregion
	}
	
	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
    public class EnumConnectionPoints : OpcRcw.Comn.IEnumConnectionPoints
	{	
		/// <summary>
		/// Initializes the object with a set of connection points.
		/// </summary>
		internal EnumConnectionPoints(ICollection connectionPoints)
		{
			if (connectionPoints != null)
			{
                foreach (OpcRcw.Comn.IConnectionPoint connectionPoint in connectionPoints)
				{
					m_connectionPoints.Add(connectionPoint);
				}
			}
		}

		#region IEnumConnectionPoints Members
		/// <remarks/>
        public void Skip(int cConnections)
		{
			lock (m_lock)
			{
				try
				{
					m_index += cConnections;

					if (m_index > m_connectionPoints.Count)
					{
						m_index = m_connectionPoints.Count;
					}
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
        public void Clone(out OpcRcw.Comn.IEnumConnectionPoints ppenum)
		{
			lock (m_lock)
			{
				try
				{
					ppenum = new EnumConnectionPoints(m_connectionPoints);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
		public void Reset()
		{
			lock (m_lock)
			{
				try
				{
					m_index = 0;
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
        public void RemoteNext(int cConnections, IntPtr ppCP, out int pceltFetched)
		{
			lock (m_lock)
			{
				try
				{
					if (ppCP == IntPtr.Zero)
					{
						throw new ExternalException("E_INVALIDARG", ResultIds.E_INVALIDARG);
					}

                    pceltFetched = 0;

					if (m_index >= m_connectionPoints.Count)
					{
						return;
					}

                    object[] unknowns = new object[cConnections];

                    for (int ii = 0; ii < m_connectionPoints.Count - m_index && ii < cConnections; ii++)
                    {
                        unknowns[ii] = (OpcRcw.Comn.IConnectionPoint)m_connectionPoints[m_index + ii];
                        pceltFetched++;
                    }

                    m_index += pceltFetched;

                    IntPtr[] pointers = new IntPtr[pceltFetched];
                    Marshal.Copy(ppCP, pointers, 0, pceltFetched);

                    for (int ii = 0; ii < pceltFetched; ii++)
                    {
                        pointers[ii] = Marshal.GetIUnknownForObject(unknowns[ii]);
                    }

                    Marshal.Copy(pointers, 0, ppCP, pceltFetched);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
		#endregion

		#region Private Members
        private object m_lock = new object();
		private ArrayList m_connectionPoints = new ArrayList();
		private int m_index = 0;
		#endregion
	}
}
