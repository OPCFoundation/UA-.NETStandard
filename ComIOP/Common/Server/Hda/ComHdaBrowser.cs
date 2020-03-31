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
    /// A class that implements a COM DA group.
    /// </summary>
    public class ComHdaBrowser : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComHdaBrowser"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="browseManager">The browse manager.</param>
        public ComHdaBrowser(ComHdaProxy server, ComDaBrowseManager browseManager)
        {
            m_server = server;
            m_browseManager = browseManager;
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
            m_disposed = true;
        }

        /// <summary>
        /// Throws if disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the handle.
        /// </summary>
        /// <value>The handle.</value>
        public IntPtr Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Sets the attribute filter.
        /// </summary>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <param name="operators">The operators.</param>
        /// <param name="values">The values.</param>
        /// <returns></returns>
        public int[] SetAttributeFilter(uint[] attributeIds, int[] operators, object[] values)
        {
            int[] errors = new int[attributeIds.Length];

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                // check for supported attribute.
                if (!m_server.IsSupportedAttribute(attributeIds[ii]))
                {
                    errors[ii] = ResultIds.E_UNKNOWNATTRID;
                    continue;
                }

                // only support filters on display name.
                if (attributeIds[ii] != OpcRcw.Hda.Constants.OPCHDA_ITEMID)
                {
                    errors[ii] = ResultIds.W_NOFILTER;
                    continue;
                }

                // must be a string.
                if (!(values[ii] is string))
                {
                    errors[ii] = ResultIds.E_INVALIDDATATYPE;
                    continue;
                }

                // create the filter.
                ItemNameFilter filter = new ItemNameFilter();
                filter.Operator = operators[ii];
                filter.Value = (string)values[ii];

                if (m_filters == null)
                {
                    m_filters = new List<ItemNameFilter>();
                }

                m_filters.Add(filter);
                
                errors[ii] = ResultIds.S_OK;
            }

            return errors;
        }

        /// <summary>
        /// Moves the current browse position up.
        /// </summary>
        public void BrowseUp()
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseUp(session);
        }

        /// <summary>
        /// Moves the current browse position down.
        /// </summary>
        public void BrowseDown(string targetName)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseDown(session, targetName);
        }

        /// <summary>
        /// Moves the current browse position to the specified item.
        /// </summary>
        public void BrowseTo(string itemId)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseTo(session, itemId);
        }

        /// <summary>
        /// Browses the current branch.
        /// </summary>
        /// <param name="browseType">Type of the browse.</param>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> Browse(int browseType)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            string itemId = String.Empty;
            string continuationPoint = null;

            ComDaBrowseElement position = m_browseManager.GetBrowsePosition(session);

            if (position != null)
            {
                itemId = position.ItemId;
            }

            IList<ComDaBrowseElement> elements = m_browseManager.BrowseForElements(
                session,
                itemId,
                null,
                0,
                (int)BrowseElementFilter.All,
                null,
                out continuationPoint);

            return ApplyFilters(elements, browseType);
        }

        /// <summary>
        /// Browse for all items below the current branch.
        /// </summary>
        /// <returns>
        /// The list of item ids that meet the criteria.
        /// </returns>
        public IList<string> BrowseForItems()
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }
            
            ComDaBrowseElement position = m_browseManager.GetBrowsePosition(session);

            List<ComDaBrowseElement> elements = new List<ComDaBrowseElement>();
            BrowseForItems(session, position, elements);

            return ApplyFilters(elements, (int)OPCHDA_BROWSETYPE.OPCHDA_FLAT);
        }

        /// <summary>
        /// Gets the item id for the specified browse element.
        /// </summary>
        /// <param name="browseName">The name of the browse element.</param>
        /// <returns></returns>
        public string GetItemId(string browseName)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetItemId(session, browseName);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Recursively browses for items.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="hits">The hits.</param>
        private void BrowseForItems(Session session, ComDaBrowseElement parent, List<ComDaBrowseElement> hits)
        {
            string itemId = String.Empty;
            string continuationPoint = null;

            if (parent != null)
            {
                itemId = parent.ItemId;
            }

            IList<ComDaBrowseElement> elements = m_browseManager.BrowseForElements(
                session,
                itemId,
                null,
                0,
                (int)BrowseElementFilter.All,
                null,
                out continuationPoint);

            for (int ii = 0; ii < elements.Count; ii++)
            {
                ComDaBrowseElement element = elements[ii];

                if (element.IsHistoricalItem)
                {
                    hits.Add(element);
                }

                if (element.HasChildren)
                {
                    BrowseForItems(session, element, hits);
                }
            }
        }

        /// <summary>
        /// Applies the filters to the list of names.
        /// </summary>
        /// <param name="elements">The elements.</param>
        /// <param name="browseType">Type of the browse.</param>
        /// <returns></returns>
        private IList<string> ApplyFilters(IList<ComDaBrowseElement> elements, int browseType)
        {
            List<string> names = new List<string>();

            for (int ii = 0; ii < elements.Count; ii++)
            {
                ComDaBrowseElement element = elements[ii];

                if (browseType == (int)OPCHDA_BROWSETYPE.OPCHDA_BRANCH)
                {
                    if (!element.HasChildren)
                    {
                        continue;
                    }
                }

                if (browseType == (int)OPCHDA_BROWSETYPE.OPCHDA_LEAF)
                {
                    if (!element.IsHistoricalItem || element.HasChildren)
                    {
                        continue;
                    }
                }

                if (browseType == (int)OPCHDA_BROWSETYPE.OPCHDA_ITEMS)
                {
                    if (!element.IsHistoricalItem)
                    {
                        continue;
                    }
                }

                if (m_filters != null)
                {
                    for (int jj = 0; jj < m_filters.Count; jj++)
                    {
                        if (!MatchFilter(element.BrowseName, m_filters[jj]))
                        {
                            continue;
                        }
                    }
                }

                if (browseType == (int)OPCHDA_BROWSETYPE.OPCHDA_ITEMS || browseType == (int)OPCHDA_BROWSETYPE.OPCHDA_FLAT)
                {
                    names.Add(element.ItemId);
                }
                else
                {
                    names.Add(element.BrowseName);
                }
            }

            return names;
        }

        /// <summary>
        /// Return true if the name natches the filter.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="filter">The filter.</param>
        /// <returns></returns>
        private bool MatchFilter(string name, ItemNameFilter filter)
        {
            if (String.IsNullOrEmpty(name))
            {
                return false;
            }

            switch ((OPCHDA_OPERATORCODES)filter.Operator)
            {
                case OPCHDA_OPERATORCODES.OPCHDA_EQUAL:
                {
                    return name == filter.Value;
                }

                case OPCHDA_OPERATORCODES.OPCHDA_GREATER:
                {
                    return name.CompareTo(filter.Value) > 0;
                }

                case OPCHDA_OPERATORCODES.OPCHDA_GREATEREQUAL:
                {
                    return name.CompareTo(filter.Value) >= 0;
                }

                case OPCHDA_OPERATORCODES.OPCHDA_LESS:
                {
                    return name.CompareTo(filter.Value) < 0;
                }

                case OPCHDA_OPERATORCODES.OPCHDA_LESSEQUAL:
                {
                    return name.CompareTo(filter.Value) <= 0;
                }

                case OPCHDA_OPERATORCODES.OPCHDA_NOTEQUAL:
                {
                    return name.CompareTo(filter.Value) != 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Dumps the current state of the browser.
        /// </summary>
        private void TraceState(string context, params object[] args)
        {
#if TRACESTATE
            if ((Utils.TraceMask & Utils.TraceMasks.Information) == 0)
            {
                return;
            }

            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("ComHdaBrowser::{0}", context);

            if (args != null)
            {
                buffer.Append("( ");

                for (int ii = 0; ii < args.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(new Variant(args[ii]));
                }

                buffer.Append(" )");
            }

            Utils.Trace("{0}", buffer.ToString());
#endif
        }
        #endregion

        /// <summary>
        /// Stores a filter to use when browsing.
        /// </summary>
        private class ItemNameFilter
        {
            public int Operator;
            public string Value;
        }

        #region Private Fields
        private object m_lock = new object();
        private bool m_disposed;
        private IntPtr m_handle;
        private ComHdaProxy m_server;
        private List<ItemNameFilter> m_filters;
        private ComDaBrowseManager m_browseManager;
        #endregion
    }
}
