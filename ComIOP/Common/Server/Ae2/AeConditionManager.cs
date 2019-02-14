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
    /// Manages conditions requring acknowledgement.
    /// </summary>
    public class AeConditionManager
    {
        #region Public Methods
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public void Initialize()
        {
            lock (m_lock)
            {
                m_cookies = new Dictionary<string, int>();
                m_events = new Dictionary<int, AeEvent>();
            }
        }

        /// <summary>
        /// Processes an event (assigns a cookie if acknowledgment is required).
        /// </summary>
        public void ProcessEvent(AeEvent e)
        {
            if (NodeId.IsNull(e.ConditionId))
            {
                return;
            }

            lock (m_lock)
            {
                string conditionId = GetConditionId(e);

                // assign a cookie to the condition/branch.
                int cookie = 0;

                if (!m_cookies.TryGetValue(conditionId, out cookie))
                {
                    cookie = ++m_counter;
                    m_cookies[conditionId] = cookie;
                }

                // remove acked events.
                if (e.AckedState)
                {
                    m_events.Remove(cookie);
                    m_cookies.Remove(conditionId);
                }

                // save event for acking.
                else
                {
                    m_events[cookie] = e;
                    e.Cookie = cookie;
                }
            }
        }

        /// <summary>
        /// Acknowledges one or more events.
        /// </summary>
        public int[] AcknowledgeEvents(
            Session session,
            string comment,
            string acknowledgerId,
            AeAcknowledgeRequest[] requests)
        {
            if (session == null || !session.Connected)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            StringBuilder buffer = new StringBuilder();
            buffer.Append('[');
            buffer.Append(acknowledgerId);
            buffer.Append(']');

            if (!String.IsNullOrEmpty(comment))
            {
                buffer.Append(comment);
            }

            // wrap the comment once.
            Variant commentToWrite = new Variant(new LocalizedText(buffer.ToString()));

            int[] errors = new int[requests.Length];
            CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();

            for (int ii = 0; ii < requests.Length; ii++)
            {
                int cookie = requests[ii].Cookie;

                AeEvent e = null;

                lock (m_lock)
                {
                    // look up the event.
                    if (!m_events.TryGetValue(cookie, out e))
                    {
                        errors[ii] = ResultIds.E_INVALIDARG;

                        if (cookie < m_counter)
                        {
                            errors[ii] = ResultIds.S_ALREADYACKED;
                        }
                        
                        continue;
                    }
                    
                    if (e.SourceName != requests[ii].SourceName)
                    {
                        errors[ii] = ResultIds.E_INVALIDARG;
                        continue;
                    }

                    if (e.ConditionName != requests[ii].ConditionName)
                    {
                        errors[ii] = ResultIds.E_INVALIDARG;
                        continue;
                    }

                    if (e.ActiveTime != requests[ii].ActiveTime)
                    {
                        errors[ii] = ResultIds.E_INVALIDTIME;
                        continue;
                    }
                    
                    // check that the cookie is still valid.
                    string conditionId = GetConditionId(e);
                    int expectedCookie = 0;

                    if (!m_cookies.TryGetValue(conditionId, out expectedCookie))
                    {
                        errors[ii] = ResultIds.S_ALREADYACKED;
                        continue;
                    }

                    // check cookie.
                    if (expectedCookie != cookie)
                    {
                        errors[ii] = ResultIds.E_INVALIDARG;
                        continue;
                    }

                    m_events.Remove(cookie);
                }

                CallMethodRequest request = new CallMethodRequest();
                request.MethodId = Opc.Ua.MethodIds.AcknowledgeableConditionType_Acknowledge;
                request.ObjectId = e.ConditionId;
                request.InputArguments.Add(new Variant(e.EventId));
                request.InputArguments.Add(commentToWrite);
                request.Handle = ii;
                methodsToCall.Add(request);
            }

            if (methodsToCall.Count > 0)
            {
                try
                {
                    // call the server.
                    CallMethodResultCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    session.Call(
                        null,
                        methodsToCall,
                        out results,
                        out diagnosticInfos);

                    // verify that the server returned the correct number of results.
                    ClientBase.ValidateResponse(results, methodsToCall);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

                    // process results.
                    for (int ii = 0; ii < methodsToCall.Count; ii++)
                    {
                        int index = (int)methodsToCall[ii].Handle;

                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                    }
                }
                catch (Exception)
                {
                    // report error.
                    for (int ii = 0; ii < methodsToCall.Count; ii++)
                    {
                        int index = (int)methodsToCall[ii].Handle;
                        errors[ii] = ResultIds.E_FAIL;
                    }
                }
            }

            return errors;
        }
        #endregion

        #region Private Method
        /// <summary>
        /// Combines the condition id/branch id in a unique identifier.
        /// </summary>
        private string GetConditionId(AeEvent e)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.Append(e.ConditionId);

            if (NodeId.IsNull(e.BranchId))
            {
                buffer.Append(e.BranchId);
            }

            return buffer.ToString();
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private int m_counter;
        private Dictionary<int, AeEvent> m_events;
        private Dictionary<string, int> m_cookies;
        #endregion
    }

    /// <summary>
    /// Stores a request to acknowledge an event.
    /// </summary>
    public class AeAcknowledgeRequest
    {
        /// <summary>
        /// The source of the condition.
        /// </summary>
        public string SourceName { get; set; }

        /// <summary>
        /// The name of the condition.
        /// </summary>
        public string ConditionName { get; set; }

        /// <summary>
        /// The active time.
        /// </summary>
        public DateTime ActiveTime { get; set; }

        /// <summary>
        /// The cookie.
        /// </summary>
        public int Cookie { get; set; }
    }
}
