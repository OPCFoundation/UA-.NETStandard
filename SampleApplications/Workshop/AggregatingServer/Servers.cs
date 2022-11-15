using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AggregatingServer.Core;
using Opc.Ua;

namespace AggregatingServer.Servers
{
    /// <summary>
    /// Represents aggregating server.
    /// </summary>
    public class AggregatingServer : Server
    {
        
        /// <summary>
        /// Server OPC UA base addresses
        /// </summary>
        public List<string> baseAddresses
        {
            get
            {
                return application.ApplicationConfiguration.ServerConfiguration.BaseAddresses;
            }
        }
        //protected ServerType serverType;
        public AggregatingServer()
        {
            ServerType serverType = new ServerType();

            server = serverType;            
        }

        /// <summary>
        /// Create address space structure for aggregating server
        /// </summary>
        public void PostCreate()
        {            
            AddNode(((ServerType)server).nodeManager.FindNodeInAddressSpace(ObjectIds.ObjectsFolder), (string)ObjectIdsAS.AggregatedServersFolder.Identifier,
                (string)ObjectIdsAS.AggregatedServersFolder.Identifier, ObjectIds.ObjectsFolder, ((ServerType)server).nodeManager.NamespaceIndex, NodeClass.Object);
        }

        /// <summary>
        /// Add uri to server namespace
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public ushort AddNamespaceUri(string uri)
        {
            ServerType pserver = (ServerType)server;
            return pserver.nodeManager.AddNamespaceUri(uri);
        }

        // TODO : prepisat podla PostCreate()
        public BaseInstanceState AddNode(NodeState parentNode, string path, string name, NodeId nodeType, ushort namespaceIndex, NodeClass nodeClass = NodeClass.Unspecified)
        {
            Dictionary<NodeId, IList<IReference>> externalReferences = new Dictionary<NodeId, IList<IReference>>();

            ServerType serverType = (ServerType)server;

            // Create Folder for Aggregated servers
            BaseInstanceState root = serverType.nodeManager.AddNode(parentNode,
                    path, name, nodeType, namespaceIndex, nodeClass);

            // add references     
            // link root to objects folder.
            IList<IReference> references = null;
            if (!externalReferences.TryGetValue(parentNode.NodeId, out references))
            {
                externalReferences[parentNode.NodeId] = references = new List<IReference>();
            }
            references.Add(new NodeStateReference(parentNode.NodeId, false, root.NodeId));

            ((ServerType)server).AddExternalRefferences(externalReferences);            

            return root;
        }

        public BaseInstanceState AddNode (NodeId parentNodeId, string path, string name, NodeId nodeType, ushort namespaceIndex, NodeClass nodeClass = NodeClass.Unspecified)
        {
            Dictionary<NodeId, IList<IReference>> externalReferences = new Dictionary<NodeId, IList<IReference>>();

            ServerType serverType = (ServerType)server;
            NodeState parentNode = serverType.nodeManager.FindNodeInAddressSpace(parentNodeId);

            // Create Folder for Aggregated servers
            BaseInstanceState root = serverType.nodeManager.AddNode(parentNode,
                    path, name, nodeType, namespaceIndex, nodeClass);            

            
            // link root to objects folder.
            IList<IReference> references = null;
            if (!externalReferences.TryGetValue(parentNodeId, out references))
            {
                externalReferences[parentNodeId] = references = new List<IReference>();
            }
            references.Add(new NodeStateReference(parentNodeId, false, root.NodeId));

            ((ServerType)server).AddExternalRefferences(externalReferences);

            return root;

        }

    }

    /// <summary>
    /// Contains Aggregated server well known nodes
    /// </summary>
    public static  partial class ObjectIdsAS
    {
        public static readonly NodeId AggregatedServersFolder = new NodeId("ns=2;s=AggregatedServers");
    }
}
