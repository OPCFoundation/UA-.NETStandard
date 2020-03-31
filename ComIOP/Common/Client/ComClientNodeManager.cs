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
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ComClientNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ComClientNodeManager(IServerInternal server, string namespaceUri, bool ownsTypeModel)
        :
            base(server)
        {
            // check if this node manager owns the COM Interop type model.
            if (ownsTypeModel)
            {
                SetNamespaces(namespaceUri, Namespaces.ComInterop);
            }
            else
            {
                SetNamespaces(namespaceUri);
            }
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
                Utils.SilentDispose(m_metadataUpdateTimer);
                m_metadataUpdateTimer = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Updates the type cache.
        /// </summary>
        protected void StartMetadataUpdates(WaitCallback callback, object callbackData, int initialDelay, int period)
        {
            lock (Lock)
            {
                if (m_metadataUpdateTimer != null)
                {
                    m_metadataUpdateTimer.Dispose();
                    m_metadataUpdateTimer = null;
                }

                m_metadataUpdateCallback = callback;
                m_metadataUpdateTimer = new Timer(DoMetadataUpdate, callbackData, initialDelay, period);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the metadata cached for the server.
        /// </summary>
        private void DoMetadataUpdate(object state)
        {
            try
            {
                if (!Server.IsRunning)
                {
                    return;
                }

                ComClientManager system = (ComClientManager)SystemContext.SystemHandle;
                ComClient client = (ComClient)system.SelectClient(SystemContext, true);

                int[] availableLocales = client.QueryAvailableLocales();

                if (availableLocales != null)
                {
                    lock (Server.DiagnosticsLock)
                    {
                        // check if the server is running.
                        if (!Server.IsRunning)
                        {
                            return;
                        }

                        // get the LocaleIdArray property.
                        BaseVariableState localeArray = Server.DiagnosticsNodeManager.Find(Opc.Ua.VariableIds.Server_ServerCapabilities_LocaleIdArray) as BaseVariableState;

                        List<string> locales = new List<string>();

                        // preserve any existing locales.
                        string[] existingLocales = localeArray.Value as string[];

                        if (existingLocales != null)
                        {
                            locales.AddRange(existingLocales);
                        }

                        for (int ii = 0; ii < availableLocales.Length; ii++)
                        {
                            if (availableLocales[ii] == 0 || availableLocales[ii] == ComUtils.LOCALE_SYSTEM_DEFAULT || availableLocales[ii] == ComUtils.LOCALE_USER_DEFAULT)
                            {
                                continue;
                            }

                            try
                            {
                                System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.GetCultureInfo(availableLocales[ii]);

                                if (!locales.Contains(culture.Name))
                                {
                                    locales.Add(culture.Name);
                                }
                            }
                            catch (Exception e)
                            {
                                Utils.Trace(e, "Can't process an invalid locale id: {0:X4}.", availableLocales[ii]);
                            }
                        }

                        localeArray.Value = locales.ToArray();
                    }
                }

                // invoke callback.
                if (m_metadataUpdateCallback != null)
                {
                    m_metadataUpdateCallback(state);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating HDA server metadata.");
            }
        }
        #endregion

        #region Private Fields
        private Timer m_metadataUpdateTimer;
        private WaitCallback m_metadataUpdateCallback;
        #endregion
    }
}
