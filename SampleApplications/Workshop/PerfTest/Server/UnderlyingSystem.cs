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
using System.Text;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.PerfTestServer
{
    public class UnderlyingSystem
    {
        public void Initialize()
        {
            m_registers = new List<MemoryRegister>();
            MemoryRegister register1 = new MemoryRegister();
            register1.Initialize(1, "R1", 50000);
            m_registers.Add(register1);
        }

        public IList<MemoryRegister> GetRegisters()
        {
            return m_registers;
        }

        public MemoryRegister GetRegister(int id)
        {
            if (id > 0 && id <= m_registers.Count)
            {
                return m_registers[id - 1];
            }

            return null;
        }

        private List<MemoryRegister> m_registers;
    }

    public class MemoryRegister
    {
        public int Id
        {
            get { return m_id; }
        }

        public string Name
        {
            get { return m_name; }
        }

        public int Size
        {
            get { return m_values.Length; }
        }

        public void Initialize(int id, string name, int size)
        {
            m_id = id;
            m_name = name;
            m_values = new int[size];
            m_monitoredItems = new IDataChangeMonitoredItem2[size][];
        }

        public int Read(int index)
        {
            if (index >= 0 && index < m_values.Length)
            {
                return m_values[index];
            }

            return 0;
        }

        public void Write(int index, int value)
        {
            if (index >= 0 && index < m_values.Length)
            {
                m_values[index] = value;
            }
        }

        public void Subscribe(int index, IDataChangeMonitoredItem2 monitoredItem)
        {
            lock (m_lock)
            {
                if (m_timer == null)
                {
                    m_timer = new Timer(OnUpdate, null, 45, 45);
                }

                if (index >= 0 && index < m_values.Length)
                {
                    IDataChangeMonitoredItem2[] monitoredItems = m_monitoredItems[index];

                    if (monitoredItems == null)
                    {
                        m_monitoredItems[index] = monitoredItems = new IDataChangeMonitoredItem2[1];
                    }
                    else
                    {
                        m_monitoredItems[index] = new IDataChangeMonitoredItem2[monitoredItems.Length + 1];
                        Array.Copy(monitoredItems, m_monitoredItems[index], monitoredItems.Length);
                        monitoredItems = m_monitoredItems[index];
                    }

                    monitoredItems[monitoredItems.Length - 1] = monitoredItem;
                }
            }
        }

        public void Unsubscribe(int index, IDataChangeMonitoredItem2 monitoredItem)
        {
            lock (m_lock)
            {
                if (index >= 0 && index < m_values.Length)
                {
                    IDataChangeMonitoredItem2[] monitoredItems = m_monitoredItems[index];

                    if (monitoredItems != null)
                    {
                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            if (Object.ReferenceEquals(monitoredItems[ii], monitoredItem))
                            {
                                m_monitoredItems[index] = new IDataChangeMonitoredItem2[monitoredItems.Length - 1];

                                if (ii > 0)
                                {
                                    Array.Copy(monitoredItems, m_monitoredItems[index], ii);
                                }

                                if (ii < monitoredItems.Length - 1)
                                {
                                    Array.Copy(monitoredItems, ii + 1, m_monitoredItems[index], 0, monitoredItems.Length - ii - 1);
                                }

                                monitoredItems = m_monitoredItems[index];
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void OnUpdate(object state)
        {
            try
            {
                lock (m_lock)
                {
                    DateTime start = HiResClock.UtcNow;
                    int delta = m_values.Length/2;

                    DataValue value = new DataValue();
                    value.ServerTimestamp = DateTime.UtcNow;
                    value.SourceTimestamp = DateTime.UtcNow;

                    for (int ii = m_start; ii < delta + m_start && ii < m_values.Length; ii++)
                    {
                        m_values[ii] += (ii+1);

                        IDataChangeMonitoredItem2[] monitoredItems = m_monitoredItems[ii];

                        if (monitoredItems != null)
                        {
                            value.WrappedValue = new Variant(m_values[ii]);

                            for (int jj = 0; jj < monitoredItems.Length; jj++)
                            {
                                monitoredItems[jj].QueueValue(value, null, true);
                            }
                        }
                    }

                    m_start += delta;

                    if (m_start >= m_values.Length)
                    {
                        m_start = 0;
                    }

                    if ((HiResClock.UtcNow - start).TotalMilliseconds > 50)
                    {
                        Utils.Trace("Update took {0}ms.", (HiResClock.UtcNow - start).TotalMilliseconds);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating items.");
            }
        }

        private object m_lock = new object();
        private int m_id;
        private string m_name;
        private int[] m_values;
        private int m_start;
        private Timer m_timer;
        private IDataChangeMonitoredItem2[][] m_monitoredItems;
    }
}
