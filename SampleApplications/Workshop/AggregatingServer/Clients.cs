using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AggregatingServer.Core;
using Opc.Ua;

namespace AggregatingServer.Clients
{
    public class InterfaceClient : AggregatingServer.Core.Client
    {
        protected uint m_serverOnNetworkID;

        public uint serverOnNetworkID
        {
            get { return m_serverOnNetworkID; }
        }

        public ResponseHeader Browse(NodeId nodeId, BrowseDirection browseDirection, bool includeSubtypes, 
            uint nodeClassMask, NodeId referenceTypeId, out Byte[] continuationPoint,
        out ReferenceDescriptionCollection references)
        {
            return session.Browse(
                requestHeader: null,
                view: null,
                nodeToBrowse: nodeId /*ObjectIds.ObjectsFolder*/,
                maxResultsToReturn: 0u,
                browseDirection: browseDirection /*BrowseDirection.Forward*/,
                referenceTypeId: referenceTypeId /*ReferenceTypeIds.HierarchicalReferences*/,
                includeSubtypes: includeSubtypes,
                nodeClassMask: nodeClassMask /*(uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method*/,
                continuationPoint: out continuationPoint, 
                references: out references);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverOnNetworkID"></param>
        public InterfaceClient(uint serverOnNetworkID) : base()
        {
            this.m_serverOnNetworkID = serverOnNetworkID;
        }

        public List<string> GetNamespaces()
        {
            List<string> namespaces = new List<string>();
            NodeId nodeId = new NodeId(Variables.Server_NamespaceArray, 0);
            INode node = session.NodeCache.Find(nodeId);

            if (node != null)
            {
                // read values
                DataValue dataValue = session.ReadValue(nodeId);
                //((Object)dataValue.Value).
                object[] obj = (object[])dataValue.Value;
                for (int ii = 0; ii < obj.Cast<string>().ToArray().Length; ii++)
                {
                    //Console.WriteLine("[{0:D}] " + obj.Cast<string>().ToArray()[ii], ii);
                    namespaces.Add(obj.Cast<string>().ToArray()[ii]);
                }
            }                                  
            return namespaces;
        }
    }
}
