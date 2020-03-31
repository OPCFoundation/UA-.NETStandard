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
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComAe2Browser : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComHdaBrowser"/> class.
        /// </summary>
        public ComAe2Browser(ComAe2Proxy server, ComAe2ProxyConfiguration configuration, ComAeNamespaceMapper mapper)
        {
            m_server = server;
            m_configuration = configuration;
            m_mapper = mapper;

            m_cache = new Dictionary<string, AeBrowseElement>();

            AeBrowseElement root = new AeBrowseElement();
            root.NodeId = Opc.Ua.ObjectIds.Server;
            root.ItemId = String.Empty;
            root.BrowseText = String.Empty;
            root.IsArea = true;
            root.Duplicated = false;

            m_cache[String.Empty] = root;
            m_position = root;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ComAe2Browser()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
        /// Moves the current browse position up.
        /// </summary>
        public void BrowseUp()
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            // determine the parent id.
            string parentId = String.Empty;

            lock (m_lock)
            {
                // can't browse up from root.
                if (String.IsNullOrEmpty(m_position.ItemId))
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                // get parent id.
                int index = m_position.ItemId.LastIndexOf('/');

                if (index != -1)
                {
                    parentId = m_position.ItemId.Substring(0, index);
                }
            }

            // browse to the parent.
            BrowseTo(parentId);
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

            lock (m_lock)
            {
                // check if this is the first access.
                if (m_position.Areas == null)
                {
                    Browse(true, String.Empty);
                }

                // find the area.
                if (m_position.Areas != null)
                {
                    for (int ii = 0; ii < m_position.Areas.Count; ii++)
                    {
                        if (m_position.Areas[ii].BrowseText == targetName)
                        {
                            m_position = m_position.Areas[ii];
                            return;
                        }
                    }
                }

                throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
            }
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

            lock (m_lock)
            {
                // check if value has been cached.
                if (itemId == null)
                {
                    itemId = String.Empty;
                }

                AeBrowseElement element = null;

                if (m_cache.TryGetValue(itemId, out element))
                {
                    m_position = element;
                    return;
                }

                // parse the item id looking for a known parent.
                Stack<string> names = new Stack<string>();
                AeBrowseElement root = null;
                string currentId = itemId;

                while (!String.IsNullOrEmpty(currentId))
                {
                    string parentId = null;
                    string itemName = currentId;

                    int index = currentId.LastIndexOf('/');

                    if (index >= 0)
                    {
                        parentId = currentId.Substring(0, index);
                        itemName = currentId.Substring(index + 1);
                    }

                    // save time by using an intermediate parent if it has already been cached.
                    if (!String.IsNullOrEmpty(parentId))
                    {
                        if (m_cache.TryGetValue(parentId, out root))
                        {
                            names.Push(itemName);
                            break;
                        }
                    }

                    currentId = parentId;
                    names.Push(itemName);
                    root = null;
                }

                // use root if no parent found.
                if (root == null)
                {
                    root = m_cache[String.Empty];
                }

                // find the element.
                element = Find(session, itemId, root, names, true);

                if (element == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                }

                // update cache and set position.
                m_cache[itemId] = element;
                m_position = element;
            }
        }

        /// <summary>
        /// Gets the qualified name.
        /// </summary>
        public string GetQualifiedName(string targetName, bool isArea)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            lock (m_lock)
            {
                List<AeBrowseElement> elements = null;

                // check if this is the first access.
                if (isArea)
                {
                    if (m_position.Areas == null)
                    {
                        Browse(true, String.Empty);
                    }

                    elements = m_position.Areas;
                }
                else
                {
                    if (m_position.Sources == null)
                    {
                        Browse(false, String.Empty);
                    }

                    elements = m_position.Sources;
                }

                // find the target.
                for (int ii = 0; ii < elements.Count; ii++)
                {
                    if (elements[ii].BrowseText == targetName)
                    {
                        return elements[ii].ItemId;
                    }
                }

                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }
        }

        /// <summary>
        /// Browses the current branch.
        /// </summary>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> Browse(bool isArea, string filter)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            lock (m_lock)
            {
                // fetch the children.
                List<AeBrowseElement> children = Browse(session, m_position, filter, isArea);

                // create list of names.
                List<string> names = new List<string>(children.Count);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    names.Add(children[ii].BrowseText);
                }

                return names;
            }
        }

        /// <summary>
        /// Finds the children that match the pattern (updates cache if required).
        /// </summary>
        private List<AeBrowseElement> Browse(Session session, AeBrowseElement start, string pattern, bool isArea)
        {
            // check cache.
            List<AeBrowseElement> targets = (isArea)?start.Areas:start.Sources;

            if (targets == null)
            {
                // fetch from server.
                targets = Browse(session, start, isArea);

                // update cache.
                if (isArea)
                {
                    start.Areas = targets;
                }
                else
                {
                    start.Sources = targets;
                }
            }

            // check if all matched.
            if (String.IsNullOrEmpty(pattern) || pattern == "*")
            {
                return targets;
            }
     
            // apply filter.
            List<AeBrowseElement> hits = new List<AeBrowseElement>();

            for (int ii = 0; ii < targets.Count; ii++)
            {
                if (ComUtils.Match(targets[ii].BrowseText, pattern, false))
                {
                    hits.Add(targets[ii]);
                }
            }
            
            return hits;
        }

        /// <summary>
        /// Fetches the children from the server.
        /// </summary>
        private List<AeBrowseElement> Browse(Session session, AeBrowseElement start, bool isArea)
        {
            // browse for notifiers and sources.
            BrowseDescription nodeToBrowse = new BrowseDescription();
            nodeToBrowse.NodeId = start.NodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.BrowseName);
            nodeToBrowse.NodeClassMask = (uint)0;

            if (isArea)
            {
                nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasNotifier;
                nodeToBrowse.IncludeSubtypes = true;
            }
            else
            {
                nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasEventSource;
                nodeToBrowse.IncludeSubtypes = true;
            }

            ReferenceDescriptionCollection references = ComAeUtils.Browse(
                session,
                nodeToBrowse,
                false);

            if (references == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            List<AeBrowseElement> hits = new List<AeBrowseElement>();

            for (int ii = 0; ii < references.Count; ii++)
            {
                // ignore remote references.
                if (references[ii].NodeId.IsAbsolute)
                {
                    continue;
                }

                // need to check if at the end of the tree.
                if (references[ii].ReferenceTypeId != ReferenceTypeIds.HasEventSource)
                {
                    nodeToBrowse.NodeId = (NodeId)references[ii].NodeId;

                    ReferenceDescriptionCollection children = ComAeUtils.Browse(session, nodeToBrowse, false);

                    if (!isArea)
                    {
                        if (children != null && children.Count > 0)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (children == null || children.Count == 0)
                        {
                            continue;
                        }
                    }
                }

                string browseText = m_mapper.GetLocalBrowseName(references[ii].BrowseName);

                // check for duplicate browse names.
                for (int jj = 0; jj < hits.Count; jj++)
                {
                    if (hits[jj].BrowseText == browseText)
                    {
                        hits[jj].Duplicated = true;
                        browseText = null;
                        break;
                    }
                }

                // add new element.
                if (browseText != null)
                {
                    AeBrowseElement element = new AeBrowseElement();
                    element.Parent = start;
                    element.NodeId = (NodeId)references[ii].NodeId;
                    element.BrowseText = m_mapper.GetLocalBrowseName(references[ii].BrowseName);
                    element.IsArea = isArea;
                    hits.Add(element);

                    StringBuilder itemId = new StringBuilder();
                    itemId.Append(start.ItemId);
                    itemId.Append('/');
                    itemId.Append(element.BrowseText);
                    element.ItemId = itemId.ToString();
                }
            }

            // remove any duplicates.
            for (int ii = 0; ii < hits.Count;)
            {
                if (hits[ii].Duplicated)
                {
                    hits.RemoveAt(ii);
                    continue;
                }

                ii++;
            }

            return hits;
        }

        /// <summary>
        /// Checks if the item id identified by the is a valid area or source.
        /// </summary>
        public bool IsValidQualifiedName(string itemId, bool isArea)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            lock (m_lock)
            {
                // find the root.
                Stack<string> names = new Stack<string>();
                AeBrowseElement root = FindRoot(itemId, names);

                // find the target.
                AeBrowseElement target = Find(session, itemId, root, names, isArea);

                if (target == null)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Returns the areas or sources that meet search pattern.
        /// </summary>
        public List<NodeId> SearchByQualifiedName(string pattern, bool isArea)
        {
            Session session = m_server.Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }
            
            List<NodeId> hits = new List<NodeId>();

            // check if a wildcard has been specified.
            int start = -1;

            for (int ii = 0; ii < pattern.Length; ii++)
            {
                if (IsWildcardChar(pattern[ii]))
                {
                    start = ii;
                    break;
                }
            }

            lock (m_lock)
            {
                // no wildcard found.
                if (start == -1)
                {
                    AeBrowseElement target = Find(session, pattern, isArea);

                    if (target != null)
                    {
                        hits.Add(target.NodeId);
                    }

                    return hits;
                }
                
                // find the root where no wildcards exist.
                int end = start;

                for (int ii = start; ii >= 0; ii--)
                {
                    if (pattern[ii] == '/')
                    {
                        end = ii;
                        break;
                    }
                }
                
                // check if the root exists.
                string rootId = pattern.Substring(0, end);                
                AeBrowseElement root = Find(session, rootId, true);

                if (root == null)
                {
                    return hits;
                }
                                
                // update the pattern to look for children of root.
                pattern = pattern.Substring(end+1);
                
                // check if the pattern has multiple levels.
                end = pattern.IndexOf('/');

                if (end == -1)
                {
                    List<AeBrowseElement> children = Browse(session, root, pattern, isArea);

                    // remove any duplicates.
                    for (int ii = 0; ii < children.Count; ii++)
                    {
                        hits.Add(children[ii].NodeId);
                    }

                    return hits;
                }
            }

            return hits;
        }

        /// <summary>
        /// Finds the area/source with the specified identifier.
        /// </summary>
        private AeBrowseElement Find(Session session, string itemId, bool isArea)
        {
            // check if it has been cached.
            AeBrowseElement target = null;

            if (m_cache.TryGetValue(itemId, out target))
            {
                if (target.IsArea == isArea)
                {
                    return target;
                }

                return null;
            }

            // find the first parent that is already cached.
            Stack<string> names = new Stack<string>();
            AeBrowseElement root = FindRoot(itemId, names);

            // browse for the node in the server.
            try
            {
                return Find(session, itemId, root, names, isArea);
            }
            catch
            {
                return null;
            }
        }

        private static readonly char[] s_WildcardChars = "*?#[".ToCharArray();

        /// <summary>
        /// Returns true if char is one of the special chars that must be escaped.
        /// </summary>
        private static bool IsWildcardChar(char ch)
        {
            for (int ii = 0; ii < s_WildcardChars.Length; ii++)
            {
                if (s_WildcardChars[ii] == ch)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region AeBrowseElement Class
        /// <summary>
        /// Stores metadata about a browse element.
        /// </summary>
        private class AeBrowseElement
        {
            public AeBrowseElement Parent { get; set; }
            public NodeId NodeId { get; set; }
            public string ItemId { get; set; }
            public string BrowseText { get; set; }
            public bool IsArea { get; set; }
            public bool Duplicated { get; set; }
            public List<AeBrowseElement> Areas { get; set; }
            public List<AeBrowseElement> Sources { get; set; }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Parses the item if looking for a known root element.
        /// </summary>
        private AeBrowseElement FindRoot(string itemId, Stack<string> names)
        {
            // parse the item id looking for a known parent.
            AeBrowseElement root = null;
            string currentId = itemId;

            while (!String.IsNullOrEmpty(currentId))
            {
                string parentId = null;
                string itemName = currentId;

                int index = currentId.LastIndexOf('/');

                if (index >= 0)
                {
                    parentId = currentId.Substring(0, index);
                    itemName = currentId.Substring(index + 1);
                }

                // save time by using an intermediate parent if it has already been cached.
                if (!String.IsNullOrEmpty(parentId))
                {
                    if (m_cache.TryGetValue(parentId, out root))
                    {
                        names.Push(itemName);
                        break;
                    }
                }

                currentId = parentId;
                names.Push(itemName);
                root = null;
            }

            if (root == null)
            {
                root = m_cache[String.Empty];
            }

            return root;
        }

        /// <summary>
        /// Finds an element identified by the path from the root.
        /// </summary>
        private AeBrowseElement Find(Session session, string itemId, AeBrowseElement root, Stack<string> names, bool isArea)
        {
            string browseText = null;

            BrowsePath browsePath = new BrowsePath();
            browsePath.StartingNode = root.NodeId;

            while (names.Count > 0)
            {
                RelativePathElement path = new RelativePathElement();

                path.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasNotifier;
                path.IsInverse = false;
                path.IncludeSubtypes = true;

                // final hop can be HasEventSource for sources.
                if (!isArea && names.Count == 1)
                {
                    path.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasEventSource;
                }

                browseText = names.Pop();
                path.TargetName = m_mapper.GetRemoteBrowseName(browseText);
                browsePath.RelativePath.Elements.Add(path);
            }

            BrowsePathCollection browsePaths = new BrowsePathCollection();
            browsePaths.Add(browsePath);

            // make the call to the server.
            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            // check if the start node actually exists.
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                return null;
            }

            // must be exact one target.
            if (results[0].Targets.Count != 1)
            {
                return null;
            }

            // can't be an external reference.
            BrowsePathTarget target = results[0].Targets[0];

            if (target.RemainingPathIndex != UInt32.MaxValue)
            {
                return null;
            }

            // need to check if at the end of the tree.
            BrowseDescription nodeToBrowse = new BrowseDescription();
            nodeToBrowse.NodeId = (NodeId)target.TargetId;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasEventSource;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.IncludeSubtypes = true;

            ReferenceDescriptionCollection children = ComAeUtils.Browse(session, nodeToBrowse, false);

            if (!isArea)
            {
                if (children != null && children.Count > 0)
                {
                    return null;
                }
            }
            else
            {
                if (children == null || children.Count == 0)
                {
                    return null;
                }
            }

            // construct the element.
            AeBrowseElement element = new AeBrowseElement();
            element.NodeId = (NodeId)target.TargetId;
            element.ItemId = itemId;
            element.BrowseText = browseText;
            element.IsArea = isArea;

            return element;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private bool m_disposed;
        private IntPtr m_handle;
        private ComAe2Proxy m_server;
        private ComAe2ProxyConfiguration m_configuration;
        private ComAeNamespaceMapper m_mapper;
        private AeBrowseElement m_position;
        private Dictionary<string, AeBrowseElement> m_cache;
        #endregion
	}
}
