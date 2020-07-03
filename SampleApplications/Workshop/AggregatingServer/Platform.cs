using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
using System.Timers;
using AggregatingServer.Clients;
using AggregatingServer.Servers;

namespace AggregatingServer.Core
{

    /// <summary>Class <c>AggregatingServer</c> represents Aggregating Server.</summary>
    public class Platform
    {
        #region Properties

        List<DiscoveryClient>  discoveryClients;
        public string urlLDS { get; set; }
        public DateTime lastCounterResetTime { get; set; }
        public ServerOnNetworkCollectionEx serversOnNetwork { get; set; }
        public int timerFindServerPeriod { get; set; }
        protected Timer timerFindServersOnNetwork;
        protected System.Uri uriLDS;
        protected List<InterfaceClient> interfaceClients;
        protected AggregatingServer.Servers.AggregatingServer aggregatingServer;


        #endregion
        /// <summary>
        /// Connects to OPC UA server to create a Client Interface.
        /// </summary>
        /// <param name="serverID"></param>
        /// <param name="configPath"></param>
        public void ConnectToServer(uint serverID, string configPath)
        {
            ServerOnNetworkEx serverOnNetworkEx = serversOnNetwork.Find(s => s.RecordId == serverID);
            if (serverOnNetworkEx.isConnected) return;
            
            InterfaceClient client = new InterfaceClient(serverID);

            client.Initialize(endPointUrl: serverOnNetworkEx.DiscoveryUrl, applicationName: "DefaultClient");
            client.LoadConfiguration(configDir: configPath, configSectionName: "DefaultClient");
            client.Run().GetAwaiter().GetResult();

            interfaceClients.Add(client);

            serverOnNetworkEx.isConnected = true;
        }

        /// <summary>
        /// 
        /// </summary>
        public Platform()
        {
            // set default parameters 
            timerFindServerPeriod = 1000; // 1000 ms
            urlLDS = "opc.tcp://localhost:4840/";
            discoveryClients = new List<DiscoveryClient>();

            interfaceClients = new List<InterfaceClient>();

            // create instance 
            aggregatingServer = new Servers.AggregatingServer();

        }

        /// <summary>
        ///  UpdateTimer
        /// </summary>
        /// <param name="timeOut"></param>
        public void StartFindServersOnNetwork()
        {

            uriLDS = new Uri(urlLDS);
            discoveryClients.Add(DiscoveryClient.Create(uriLDS));


            // Create a timer with a two second interval.
            timerFindServersOnNetwork = new System.Timers.Timer(timerFindServerPeriod);
            // Hook up the Elapsed event for the timer. 
            timerFindServersOnNetwork.Elapsed += (sender, e) => OnUpdateTimer(sender,e,this);
            timerFindServersOnNetwork.AutoReset = true;
            timerFindServersOnNetwork.Enabled = false;

            FindServersOnNetwork(this);

        }

        /// <summary>
        ///  OnUpdateTimer
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// <param name="obj"></param>
        private static void OnUpdateTimer(Object source, ElapsedEventArgs e, Object obj)
        {
            Platform aggregatingServer = (Platform)obj;
            //FindServersOnNetwork("opc.tcp://VM-WIN16-DEV:4840/");
            FindServersOnNetwork(aggregatingServer);
        }

        #region Methods
        /// <summary>
        /// FindServersOnNetwork
        /// </summary>
        /// <param name="aggregatingServer"></param>
        static public void FindServersOnNetwork(Platform aggregatingServer)
        {
            DateTime lastCounterResetTime;
            ServerOnNetworkCollection serverOnNetworks;

            try
            {
                foreach (DiscoveryClient discoveryClient in aggregatingServer.discoveryClients)
                {
                    discoveryClient.FindServersOnNetwork(null, 0, 0, null, out lastCounterResetTime, out serverOnNetworks);

                    aggregatingServer.lastCounterResetTime = lastCounterResetTime;
                    aggregatingServer.serversOnNetwork = (ServerOnNetworkCollectionEx)serverOnNetworks;
                }
            }
            catch(Exception e)
            {

            }
        }
        /// <summary>
        /// Start aggregating server
        /// </summary>
        public void AggregatingServerStart()
        {
            aggregatingServer.Initialize(applicationName: "phi-ware Aggregating Server");
            aggregatingServer.LoadConfiguration(configSectionName: "AggregatingServer");
            aggregatingServer.CheckCertificate();
            aggregatingServer.Run().GetAwaiter().GetResult();

            // Post-creation actions
            aggregatingServer.PostCreate();


            Console.WriteLine("aggregating server started: " + Environment.NewLine + string.Join(Environment.NewLine, aggregatingServer.baseAddresses) );
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverID"></param>
        public void AggregatingServerAggregate(uint serverID)
        {

            // find the client which refers to serverID
            InterfaceClient interfaceClient;
            
            interfaceClient = interfaceClients.Find(i => i.serverOnNetworkID == serverID);

            // Add namespace
            foreach(string ns in interfaceClient.GetNamespaces())
            {
                // add namespace to aggregating server
                aggregatingServer.AddNamespaceUri(ns);

                /*

                // browse 
                Byte[] continuationPoint;
                ReferenceDescriptionCollection references;
                ResponseHeader responseHeader;

                responseHeader = interfaceClient.Browse(ObjectIds.ObjectsFolder, BrowseDirection.Forward, true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    ReferenceTypeIds.HierarchicalReferences, out continuationPoint, out references);
                    */
            }

            // get server's name
            ServerOnNetworkEx serverOnNetwork = serversOnNetwork.Find(s => s.RecordId == serverID);
            
            // Connect to OPC server

                                    

            aggregatingServer.AddNode(ObjectIdsAS.AggregatedServersFolder,
                serverOnNetwork.ServerName,
                serverOnNetwork.ServerName, ObjectTypeIds.BaseObjectType, 2 /* FIX HERE*/);


        }

        #endregion
    }

}
