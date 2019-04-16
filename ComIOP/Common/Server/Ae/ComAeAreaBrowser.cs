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
using OpcRcw.Ae;
using OpcRcw.Comn;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server.Ae
{
    /// <summary>
    /// Implements COM-AE AreaBrowser class
    /// </summary>
    [ComVisible(true)]
    public class AreaBrowser :
        IOPCEventAreaBrowser
    {
        /// <summary>
        /// Initializes the object with the default values
        /// </summary>
        /// <param name="server">The server associated with the browser</param>
        /// <param name="session">The session associated with the browser</param>
        public AreaBrowser(ComAeProxy server, Session session)
        {
            m_server = server;
            m_session = session;
            // ensure browse stack has been initialized.
            if (m_browseStack.Count == 0)
            {
                INode parent = m_session.NodeCache.Find(Objects.Server);
                m_browseStack.Push(parent);
            }
        }


        #region IOPCEventAreaBrowser Members

        /// <summary>
        /// Provides a way to move �up� or �down� in a hierarchical space from the current position, or a way to move to a specific position in the area space tree. The target szString must represent an area, rather than a source.
        /// </summary>
        /// <param name="dwBrowseDirection">OPCAE_BROWSE_UP, OPCAE_BROWSE_DOWN, or OPCAE_BROWSE_TO</param>
        /// <param name="szString">
        /// For DOWN, the partial area name of the area to move into. This would be one of the strings returned from BrowseOPCAreas.
        /// For UP this parameter is ignored and should point to a NULL string.
        /// For BROWSE_TO, the fully qualified area name (as obtained from GetQualifiedAreaName method) or a pointer to a NUL string to go to the root.
        /// </param>
        public void ChangeBrowsePosition(OPCAEBROWSEDIRECTION dwBrowseDirection, string szString)
        {
            lock (m_lock)
            {
				try
                {
                    switch (dwBrowseDirection)
                    {
                        // move to a specified position or root.
                        case OPCAEBROWSEDIRECTION.OPCAE_BROWSE_TO:
                        {
                            try
                            {
                                // move to root.
                                if (String.IsNullOrEmpty(szString))
                                {
                                    m_browseStack.Clear();
                                    m_browseStack.Push(m_session.NodeCache.Find(Objects.Server));
                                    break;
                                }

                                // Translate the fully qualified area name to NodeId
                                List<string> szAreas = new List<string>();
                                szAreas.Add(szString);
                                BrowsePathResultCollection results = m_server.GetBrowseTargets(szAreas);
                                if (StatusCode.IsBad(results[0].StatusCode))
                                {
                                    throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                                }

                                BrowsePathTarget target = results[0].Targets[0];
                                INode node = m_session.NodeCache.Find(target.TargetId);
                                if (node == null)
                                {
                                    throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                                }

                                // build a new browse stack.
                                Stack<INode> browseStack = new Stack<INode>();

                                if (!FindPathToNode(node, browseStack))
                                {
                                    throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                                }

                                // push target node onto stack.
                                browseStack.Push(node);

                                m_browseStack = browseStack;
                            }
                            catch
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                            }
                            break;
                        }

                        // move to a child branch.
                    case OPCAEBROWSEDIRECTION.OPCAE_BROWSE_DOWN:
                        {
                            // check for invalid name.
                            if (String.IsNullOrEmpty(szString))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                            }

                            // find the current node.
                            INode parent = m_browseStack.Peek();

                            if (parent == null)
                            {
                                throw ComUtils.CreateComException(ResultIds.E_FAIL);
                            }

                            // find the child.
                            INode child = FindChildByName(parent.NodeId, szString);
                            
                            if (child == null)
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDBRANCHNAME);
                            }
                            // save the new position.
                            m_browseStack.Push(child);
                            break;
                        }

                        // move to a parent branch.
                    case OPCAEBROWSEDIRECTION.OPCAE_BROWSE_UP:
                        {
                            // check for invalid name.
                            if (!String.IsNullOrEmpty(szString))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_FAIL);
                            }

                            // can't move up from root.
                            if (m_browseStack.Count <= 1)
                            {
                                throw ComUtils.CreateComException(ResultIds.E_FAIL);
                            }

                            // move up the stack.
                            m_browseStack.Pop();
                            break;
                        }

                        default:
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }
                    }
                }
                catch (COMException e)
                {
                    throw ComUtils.CreateComException(e);
                }
                catch (Exception e)
				{
                    Utils.Trace(e, "Unexpected error in ChangeBrowsePosition");
					throw ComUtils.CreateComException(e);
				}
            }
        }

        /// <summary>
        /// Returns whether the specified node allows event notification. (Is the EventNotifier attribute set to SubscribeToEvents?)
        /// </summary>
        /// <param name="nodeId">The NodeId of the node being checked.</param>
        private bool IsEventNotificationListAllowed(NodeId nodeId)
        {
            //only nodes that allow event notification 
            DataValue value = new DataValue();
            try
            {
                Node node = m_session.ReadNode(nodeId);
                ServiceResult result = node.Read(null, Attributes.EventNotifier, value);
                if (result.Code == StatusCodes.BadAttributeIdInvalid)
                    value.Value = (byte)0; //no EventNotifier attribute found
            }
            catch (Exception e)
            {
                //we will assume either no attributes or no EventNotifier attribute
                Utils.Trace(e, "Unexpected error in IsEventNotificationListAllowed");
                value.Value = (byte)0;
            }

            return System.Convert.ToBoolean((byte)value.Value & EventNotifiers.SubscribeToEvents);

        }

        /// <summary>
        /// Finds the path to the Node from the Objects folder.
        /// </summary>
        /// <remarks>
        /// The DA proxy exposes only nodes that can be related back to the Objects folder via HierarchicalReferences references.
        /// If the client browses to a specific node the proxy must determine if that node has a reference back to the
        /// Objects folder. If it does not then it is not a valid node.
        /// </remarks>
        private bool FindPathToNode(INode startNode, Stack<INode> path)
        {
            // find all parent nodes.
            try
            {
                foreach (INode node in m_session.NodeCache.Find(startNode.NodeId, ReferenceTypes.HierarchicalReferences, true, true))
                {
                    // ignore external nodes.
                    if (node.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    // ignore non-objects/variables.
                    if ((node.NodeClass & (NodeClass.Object | NodeClass.Variable)) == 0)
                    {
                        continue;
                    }

                    // check if server object found.
                    if (node.NodeId == Objects.Server)
                    {
                        path.Push(node);
                        return true;
                    }

                    // recursively follow parents.
                    if (FindPathToNode(node, path))
                    {
                        path.Push(node);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindPathToNode");
            }
            // path does not lead to the objects folder.
            return false;
        }
        /// <summary>
        /// Finds the child node with the specified browse name.
        /// </summary>
        private INode FindChildByName(ExpandedNodeId startId, string browseName)
        {
            // find all parent nodes.
            IList<INode> children;
            try
            {
                children = m_session.NodeCache.Find(startId, ReferenceTypes.HasEventSource, false, true); // This would also include 
                                                                                                              // the HasNotifier reference which is 
                                                                                                              // a subtype of HasEventSource 
                foreach (INode child in children)
                {
                    // ignore external nodes.
                    if (child.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    // ignore non-objects/variables.
                    if ((child.NodeClass & (NodeClass.Object | NodeClass.Variable)) == 0)
                    {
                        continue;
                    }

                    // ignore the namespace when comparing.
                    if (browseName == child.BrowseName.ToString())
                    {
                        return child;
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in FindChildByName");
            }
            // child with specified browse name does not exist.
            return null;
        }

        /// <summary>
        /// Return an IEnumString for a list of Areas as determined by the passed parameters.
        /// </summary>
        /// <param name="dwBrowseFilterType">
        /// OPC_AREA - returns only areas.
        /// OPC_SOURCE - returns only sources.</param>
        /// <param name="szFilterCriteria">A server specific filter string. A pointer to a NULL string indicates no filtering.</param>
        /// <param name="ppIEnumString">Where to save the returned interface pointer. NULL if the HRESULT is other than S_OK or S_FALSE.</param>
        public void BrowseOPCAreas(
            OPCAEBROWSETYPE             dwBrowseFilterType, 
            string                      szFilterCriteria, 
            out OpcRcw.Comn.IEnumString ppIEnumString)
        {
            ppIEnumString = null;
            if (dwBrowseFilterType != OPCAEBROWSETYPE.OPC_AREA && dwBrowseFilterType != OPCAEBROWSETYPE.OPC_SOURCE)
            {
                throw ComUtils.CreateComException("BrowseOPCAreas", ResultIds.E_INVALIDARG);
            }
            try
            {
                lock (m_lock)
                {
                    // find the current node.
                    INode parent = null;

                    // ensure browse stack has been initialized.
                    if (m_browseStack.Count == 0)
                    {
                        parent = m_session.NodeCache.Find(Objects.Server);
                        m_browseStack.Push(parent);
                    }

                    parent = m_browseStack.Peek();

                    if (parent == null)
                    {
                        throw ComUtils.CreateComException("BrowseOPCAreas",ResultIds.E_FAIL);
                    }

                    List<string> names = new List<string>();
                    IList<INode> children;

                    ////// find children.
                       children = m_session.NodeCache.Find(parent.NodeId, ReferenceTypes.HasEventSource, false, true); // This would also include 
                                                                                                                        // the HasNotifier reference which is 
                                                                                                                        // a subtype of HasEventSource 

                    foreach (INode child in children)
                    {
                        // ignore external nodes.
                        if (child.NodeId.IsAbsolute)
                        {
                            continue;
                        }

                        // ignore non-objects/variables.
                        if ((child.NodeClass & (NodeClass.Object | NodeClass.Variable)) == 0)
                        {
                            continue;
                        }

                        // ignore duplicate browse names.
                        if (names.Contains(child.BrowseName.ToString()))
                        {
                            continue;
                        }

                        // For a node to be an area, it has to have the 'HasNotifier' reference and must
                        // allow event notification (the EventNotifier attribute is set to SubscribeToEvents) 
                        // For a node to be a source, it should be the target of the HasEventSource reference
                        if (IsValidNode((NodeId)child.NodeId, child.BrowseName.ToString(), dwBrowseFilterType))
                        {
                            if (String.IsNullOrEmpty(szFilterCriteria))
                            {
                                names.Add(child.BrowseName.ToString());
                            }
                            else
                            {
                                // apply filters
                                if (ComUtils.Match(child.BrowseName.ToString(), szFilterCriteria, true))
                                {
                                   names.Add(child.BrowseName.ToString());
                                }
                            }
                        }
                     }

                    // create enumerator.
                    ppIEnumString = (OpcRcw.Comn.IEnumString)new EnumString(names);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in BrowseOPCAreas");
                throw ComUtils.CreateComException(e);
            }
        }

        private bool IsValidNode(NodeId nodeId, string nodeName, OPCAEBROWSETYPE dwBrowseFilterType)
        {
            Browser browse = new Browser(m_session);
            ReferenceDescriptionCollection references = null;

            try
            {
                // For a node to be an area, it has to have the 'HasNotifier' reference and must
                // allow event notification (the EventNotifier attribute is set to SubscribeToEvents)
                if (dwBrowseFilterType == OPCAEBROWSETYPE.OPC_AREA)
                {
                    // if node is 'Server' then the HasNotifier is implicit, so return true
                    if (nodeName == "Server")
                    {
                        return true;
                    }

                    references = browse.Browse(nodeId);
                    
                    foreach (ReferenceDescription reference in references)
                    {
                        if ((reference.ReferenceTypeId == ReferenceTypes.HasNotifier) || (reference.ReferenceTypeId == ReferenceTypes.HasEventSource))
                        {
                            return IsEventNotificationListAllowed(nodeId);
                        }
                    }
                }

                // For a node to be a source, it should be the target of the HasEventSource reference
                else   // Check if this is a source. 
                {
                    IList<INode> parent = m_session.NodeCache.Find(nodeId, ReferenceTypes.HasEventSource, true, false);
                    if (parent.Count != 0)
                        return true;
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in IsValidNode");
            }
             
            return false;
        }

        /// <summary>
        /// Provides a mechanism to assemble a fully qualified Area name in a hierarchical space.
        /// </summary>
        /// <param name="szAreaName">The name of an Area at the current level, obtained from the string enumerator returned by BrowseOPCAreas with a BrowseFilterType of OPC_AREA</param>
        /// <param name="pszQualifiedAreaName">Where to return the resulting fully qualified area name.</param>
        public void GetQualifiedAreaName(string szAreaName, out string pszQualifiedAreaName)
        {
            pszQualifiedAreaName = String.Empty;

            try
            {
                pszQualifiedAreaName = szAreaName;
                // Make sure the stack is not null
                INode parent = m_browseStack.Peek();
                if (parent == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                // And make sure this is avalid Area name at the level
                INode child = FindChildByName(parent.NodeId, szAreaName);
                if (child == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }
                pszQualifiedAreaName = "";
                INode[] stack = m_browseStack.ToArray();
                for (int i = stack.Length - 2; i >= 0; i--)
                {
                    // Translate the server namespace index in browsename to the corresponding client namespace index
                    QualifiedName QName = stack[i].BrowseName;
                    QualifiedName translatedName = new QualifiedName(QName.Name, (ushort)m_server.ServerMappingTable[QName.NamespaceIndex]);
                    if (pszQualifiedAreaName.Length != 0)
                        pszQualifiedAreaName = pszQualifiedAreaName + "/" + translatedName.ToString();
                    else
                        pszQualifiedAreaName = translatedName.ToString();
                }
                //Also translate the areaname
                QualifiedName QualifiedAreaName =  QualifiedName.Parse(szAreaName);
                QualifiedName TranslatedAreaName = new QualifiedName(QualifiedAreaName.Name, (ushort)m_server.ServerMappingTable[QualifiedAreaName.NamespaceIndex]);
                pszQualifiedAreaName = pszQualifiedAreaName + "/" + TranslatedAreaName.ToString();

            }
            catch (COMException e)
            {
                throw ComUtils.CreateComException(e);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error in GetQualifiedAreaName");
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Provides a mechanism to assemble a fully qualified Source name in a hierarchical space.
        /// </summary>
        /// <param name="szSourceName">The name of a Source at the current level, obtained from the string enumerator returned by BrowseOPCAreas with a BrowseFilterType of OPC_SOURCE.</param>
        /// <param name="pszQualifiedSourceName">Where to return the resulting fully qualified source name.</param>
        public void GetQualifiedSourceName(string szSourceName, out string pszQualifiedSourceName)
        {
            GetQualifiedAreaName(szSourceName, out pszQualifiedSourceName);
        }
        #endregion

        #region Private Members
        private object m_lock = new object();
        private ComAeProxy m_server = null;
        private Session m_session = null;
        private Stack<INode> m_browseStack = new Stack<INode>();
        #endregion
    }
}
