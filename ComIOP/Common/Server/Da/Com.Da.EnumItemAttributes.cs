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

using OpcRcw.Da;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Server.Da
{
	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
	public class EnumOPCItemAttributes : IEnumOPCItemAttributes
	{	
		/// <remarks/>
		public class ItemAttributes
		{
			/// <remarks/>
			public string       ItemID            = null;
			/// <remarks/>
			public string       AccessPath        = null;
			/// <remarks/>
			public int          ClientHandle      = -1;
			/// <remarks/>
			public int          ServerHandle      = -1;
			/// <remarks/>
			public bool         Active            = false;
			/// <remarks/>
			public VarEnum      CanonicalDataType = VarEnum.VT_EMPTY;
			/// <remarks/>
			public VarEnum      RequestedDataType = VarEnum.VT_EMPTY;
			/// <remarks/>
			public int          AccessRights      = Constants.OPC_READABLE | Constants.OPC_WRITEABLE;
			/// <remarks/>
			public OPCEUTYPE    EuType            = OPCEUTYPE.OPC_NOENUM;
			/// <remarks/>
			public double       MaxValue          = 0;
			/// <remarks/>
			public double       MinValue          = 0;
			/// <remarks/>
			public string[]     EuInfo            = null;
		}

		/// <summary>
		/// Initializes the object with a set of connection points.
		/// </summary>
		internal EnumOPCItemAttributes(ICollection items)
		{
			if (items != null)
			{
				foreach (ItemAttributes item in items)
				{
					m_items.Add(item);
				}
			}
		}
		
		#region IEnumOPCItemAttributes Members
		/// <remarks/>
		public void Skip(int celt)
		{
			lock (m_lock)
			{
				try
				{
					m_index += celt;

					if (m_index > m_items.Count)
					{
						m_index = m_items.Count;
					}
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <remarks/>
		public void Clone(out IEnumOPCItemAttributes ppEnumItemAttributes)
		{			
			lock (m_lock)
			{
				try
				{
					ppEnumItemAttributes = new EnumOPCItemAttributes(m_items);
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
		public void Next(int celt, out System.IntPtr ppItemArray, out int pceltFetched)
		{
			lock (m_lock)
			{
				try
				{
					pceltFetched = 0;
					ppItemArray  = IntPtr.Zero;

					if (m_index >= m_items.Count)
					{
                        return;
					}

					// determine how many items to return.
					pceltFetched = m_items.Count - m_index;

					if (pceltFetched > celt)
					{
						pceltFetched = celt;
					}

					// allocate return array.
					ppItemArray = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OPCITEMATTRIBUTES))*pceltFetched);

					// marshal items to return.
					IntPtr pos = ppItemArray;

					for (int ii = 0; ii < pceltFetched; ii++)
					{
						ItemAttributes item = (ItemAttributes)m_items[m_index+ii];

						OPCITEMATTRIBUTES copy = new OPCITEMATTRIBUTES();

						copy.szItemID            = item.ItemID;
						copy.szAccessPath        = item.AccessPath;
						copy.hClient             = item.ClientHandle;
						copy.hServer             = item.ServerHandle;
						copy.bActive             = (item.Active)?1:0;
						copy.vtCanonicalDataType = (short)item.CanonicalDataType;
						copy.vtRequestedDataType = (short)item.RequestedDataType;
						copy.dwAccessRights      = item.AccessRights;
						copy.dwBlobSize          = 0;
						copy.pBlob               = IntPtr.Zero;
						copy.dwEUType            = item.EuType;
						copy.vEUInfo             = null;

						switch (item.EuType)
						{
							case OPCEUTYPE.OPC_ANALOG:     { copy.vEUInfo = new double[] { item.MinValue, item.MaxValue }; break; }
							case OPCEUTYPE.OPC_ENUMERATED: { copy.vEUInfo = item.EuInfo;                                   break; }
						}				

						Marshal.StructureToPtr(copy, pos, false);
						pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMATTRIBUTES)));
					}

					// update index.
					m_index += pceltFetched;
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
		private ArrayList m_items = new ArrayList();
		private int m_index = 0;
		#endregion
	}	
}
