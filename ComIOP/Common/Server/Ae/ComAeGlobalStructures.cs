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
using System.Runtime.InteropServices;
using System.Globalization;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// Primary purpose is to maintain list of server instances and as a respository for
    /// data common to all server instances.
    /// </summary>
    public class Global
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public Global()
        {
            m_StartTime = DateTime.Now;
            m_EvServerSet = new List<ComAeProxy>();
            m_lcid = ComUtils.LOCALE_SYSTEM_DEFAULT;
            m_StdAttrNames = new string[] { "AckComment", "Areas" };
            m_StdAttrIds = new int[] { 0, 1 };
        }

        /// <summary>
        /// Called during incoming event processing and as a result of a client-initiated Refresh.
        /// Forwards to each server instance for further processing.
        /// </summary>
        /// <param name="Source"></param>
        /// <param name="Condition"></param>
        /// <param name="cond"></param>
        public void NotifyClients(string Source, string Condition, OPCCondition cond)
        {
            OnEventClass OEClass = new OnEventClass(Source, Condition, cond);

            foreach (ComAeProxy s in m_EvServerSet)
            {
                s.ProcessNewEvent(OEClass);
            }

        }

        /// <summary>
        /// Called from server object constructor to insert server reference into the global list
        /// </summary>
        /// <param name="s"></param>
        public void ServerListInsert(ComAeProxy s)
        {
            m_EvServerSet.Add(s);
        }

        /// <summary>
        /// Called from server object finalizer to remove server reference from the global list
        /// </summary>
        /// <param name="s"></param>
        public void ServerListRemove(ComAeProxy s)
        {
            m_EvServerSet.Remove(s);
        }

        /// <summary>
        /// Start time for this COM proxy
        /// </summary>
        public DateTime StartTime
        {
            get { return m_StartTime; }
        }

        /// <summary>
        /// Locale ID
        /// </summary>
        public int LCID
        {
            get { return m_lcid; }
        }

        /// <summary>
        /// Static accessor
        /// </summary>
        public static Global TheGlobal
        {
            get { return theGlobal; }
        }

        /// <summary>
        /// Standard attribute names
        /// </summary>
        public string[] StdAttrNames
        {
            get { return m_StdAttrNames; }
        }

        /// <summary>
        /// Standard attribute IDs
        /// </summary>
        public int[] StdAttrIds
        {
            get { return m_StdAttrIds; }
        }

        private readonly string[] m_StdAttrNames;
        private readonly int[] m_StdAttrIds;
        private static Global theGlobal = new Global();
        private List<ComAeProxy> m_EvServerSet;
        private DateTime m_StartTime;
        private int m_lcid;

    }

}
