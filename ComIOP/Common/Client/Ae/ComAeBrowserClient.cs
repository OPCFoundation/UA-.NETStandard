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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Ae;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Browses areas and sources in the AE server.
    /// </summary>
    public class ComAeBrowserClient : ComObject
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComAeBrowserClient"/> class.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="qualifiedName">The qualified area name.</param>
        public ComAeBrowserClient(
            ComAeClient client,
            string qualifiedName)
        {
            m_client = client;
            m_qualifiedName = qualifiedName; 
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
                Utils.SilentDispose(m_enumerator);
                m_enumerator = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns the next AE area or source.
        /// </summary>
        /// <returns>A DA element. Null if nothing left to browse.</returns>
        public BaseObjectState Next(ISystemContext context, ushort namespaceIndex)
        {
            // check if already completed.
            if (m_completed)
            {
                return null;
            }

            // create the browser.
            if (base.Unknown == null)
            {
                base.Unknown = m_client.CreateAreaBrowser();

                if (base.Unknown == null)
                {
                    return null;
                }

                if (!ChangeBrowsePosition(OPCAEBROWSEDIRECTION.OPCAE_BROWSE_TO, m_qualifiedName))
                {
                    return null;
                }
            }

            // create the enumerator if not already created.
            if (m_enumerator == null)
            {
                m_enumerator = CreateEnumerator(false);
                m_sources = false;

                // a null indicates an error.
                if (m_enumerator == null)
                {
                    m_completed = true;
                    return null;
                }
            }

            // need a loop in case errors occur fetching element metadata.
            BaseObjectState node = null;

            do
            {
                // fetch the next name.
                string name = m_enumerator.Next();

                // a null indicates the end of list.
                if (name == null)
                {
                    if (!m_sources)
                    {
                        m_enumerator.Dispose();
                        m_enumerator = CreateEnumerator(true);
                        m_sources = true;
                        continue;
                    }

                    m_completed = true;
                    return null;
                }

                // create the node.
                if (m_sources)
                {
                    string qualifiedName = GetQualifiedSourceName(name);

                    if (String.IsNullOrEmpty(qualifiedName))
                    {
                        continue;
                    }

                    node = new AeSourceState(context, m_qualifiedName, qualifiedName, name, namespaceIndex);
                }
                else
                {
                    string qualifiedName = GetQualifiedAreaName(name);

                    if (String.IsNullOrEmpty(qualifiedName))
                    {
                        continue;
                    }

                    node = new AeAreaState(context, qualifiedName, name, namespaceIndex);
                }

                break;
            }
            while (node == null);

            // return node.
            return node;
        }

        /// <summary>
        /// Finds the area.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="qualifiedName">Name of the qualified.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public BaseObjectState FindArea(ISystemContext context, string qualifiedName, ushort namespaceIndex)
        {
            // create the browser.
            if (base.Unknown == null)
            {
                base.Unknown = m_client.CreateAreaBrowser();

                if (base.Unknown == null)
                {
                    return null;
                }
            }

            // goto area.
            if (!ChangeBrowsePosition(OPCAEBROWSEDIRECTION.OPCAE_BROWSE_TO, qualifiedName))
            {
                return null;
            }

            // find browse name via parent.
            if (!ChangeBrowsePosition(OPCAEBROWSEDIRECTION.OPCAE_BROWSE_UP, String.Empty))
            {
                return null;
            }

            // remove the enumerator.
            if (m_enumerator != null)
            {
                m_enumerator.Dispose();
                m_enumerator = null;
            }

            m_enumerator = CreateEnumerator(false);

            do
            {
                // fetch the next name.
                string name = m_enumerator.Next();

                // a null indicates the end of list.
                if (name == null)
                {
                    m_completed = true;
                    return null;
                }

                // create the node.
                if (qualifiedName == GetQualifiedAreaName(name))
                {
                    return new AeAreaState(context, qualifiedName, name, namespaceIndex);
                }
            }
            while (!m_completed);

            // return node.
            return null;
        }

        /// <summary>
        /// Finds the source.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="areaName">Name of the area.</param>
        /// <param name="sourceName">Name of the source.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The source.</returns>
        public BaseObjectState FindSource(ISystemContext context, string areaName, string sourceName, ushort namespaceIndex)
        {
            // create the browser.
            if (base.Unknown == null)
            {
                base.Unknown = m_client.CreateAreaBrowser();

                if (base.Unknown == null)
                {
                    return null;
                }
            }

            if (!ChangeBrowsePosition(OPCAEBROWSEDIRECTION.OPCAE_BROWSE_TO, areaName))
            {
                return null;
            }

            // remove the enumerator.
            if (m_enumerator != null)
            {
                m_enumerator.Dispose();
                m_enumerator = null;
            }

            m_enumerator = CreateEnumerator(true);

            do
            {
                // fetch the next name.
                string name = m_enumerator.Next();

                // a null indicates the end of list.
                if (name == null)
                {
                    m_completed = true;
                    return null;
                }

                // create the node.
                if (sourceName == name)
                {
                    string qualifiedName = GetQualifiedSourceName(name);
                    return new AeSourceState(context, m_qualifiedName, qualifiedName, name, namespaceIndex);
                }
            }
            while (!m_completed);

            // return node.
            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Changes the browse position.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="qualifiedName">The qualified area name.</param>
        private bool ChangeBrowsePosition(OPCAEBROWSEDIRECTION direction, string qualifiedName)
        {
            string methodName = "IOPCEventAreaBrowser.CreateAreaBrowser";

            try
            {
                IOPCEventAreaBrowser server = BeginComCall<IOPCEventAreaBrowser>(methodName, true);
                server.ChangeBrowsePosition(direction, qualifiedName);
                return true;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Gets the qualified name for the area.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The qualified name for the area.</returns>
        private string GetQualifiedAreaName(string name)
        {
            string methodName = "IOPCEventAreaBrowser.GetQualifiedAreaName";

            string qualifiedName = null;

            try
            {
                IOPCEventAreaBrowser server = BeginComCall<IOPCEventAreaBrowser>(methodName, true);
                server.GetQualifiedAreaName(name, out qualifiedName);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return qualifiedName;
        }

        /// <summary>
        /// Gets the qualified name for the source.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The qualified name for the area.</returns>
        private string GetQualifiedSourceName(string name)
        {
            string methodName = "IOPCEventAreaBrowser.GetQualifiedSourceName";

            string qualifiedName = null;

            try
            {
                IOPCEventAreaBrowser server = BeginComCall<IOPCEventAreaBrowser>(methodName, true);
                server.GetQualifiedSourceName(name, out qualifiedName);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return qualifiedName;
        }

        /// <summary>
        /// Creates an enumerator for the current browse position.
        /// </summary>
        /// <param name="sources">if set to <c>true</c> then sources are enumerated.</param>
        /// <returns>The wrapped enumerator.</returns>
        private EnumString CreateEnumerator(bool sources)
        {
            IEnumString unknown = null;

            string methodName = "IOPCEventAreaBrowser.BrowseOPCAreas";

            try
            {
                IOPCEventAreaBrowser server = BeginComCall<IOPCEventAreaBrowser>(methodName, true);

                OPCAEBROWSETYPE browseType = OPCAEBROWSETYPE.OPC_AREA;

                if (sources)
                {
                    browseType = OPCAEBROWSETYPE.OPC_SOURCE;
                }

                server.BrowseOPCAreas(browseType, String.Empty, out unknown);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            // wrapper the enumrator. hardcoding a buffer size of 256.
            return new EnumString(unknown, 256);
        }
        #endregion

        #region Private Fields
        private ComAeClient m_client;
        private string m_qualifiedName;
        private Opc.Ua.Com.Client.EnumString m_enumerator;
        private bool m_completed;
        private bool m_sources;
        #endregion
    }
}
