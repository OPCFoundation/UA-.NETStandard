using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;


namespace AggregatingServer.Client
{

    public class ClientLDS : AggregatingServer.Core.Client
    {

    }
    /// <summary>
    /// class ClientInterface consolidates functions necessary for aggregating
    /// an OPC UA server.
    /// </summary>
    public class _oldClientInterface
    {
        const int ReconnectPeriod = 10;
        public Session session;
        SessionReconnectHandler reconnectHandler;
        string endpointURL;
        int clientRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static int exitCode;
        Browser browser;
        NodeId rootId;
        protected ApplicationInstance appInstance;
        /// <summary>
        /// Id of OPC UA server in Aggrgating Server
        /// </summary>
        private int _serverId;
        /// <summary>
        /// Id of OPC UA server in Aggrgating Server
        /// </summary>
        public int serverId { get { return _serverId; } }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="_endpointURL"></param>
        /// <param name="_autoAccept"></param>
        /// <param name="_stopTimeout"></param>
        /// <param name="serverId"></param>
        public _oldClientInterface(string _endpointURL, bool _autoAccept, int _stopTimeout, int serverId = -1)
        {
            endpointURL = _endpointURL;
            autoAccept = _autoAccept;
            clientRunTime = _stopTimeout <= 0 ? Timeout.Infinite : _stopTimeout * 1000;
            this._serverId = serverId;
        }

        public List<string> GetNamespaces()
        {
            List<string> nameSpaces = new List<string>();

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
                    nameSpaces.Add(obj.Cast<string>().ToArray()[ii]);
                }
            }

            return nameSpaces;
        }

        /// <summary>
        /// SetView can filter objects by their types. See  BrowseTreeCtrl.cs of UA Net Standard project. 
        /// Set also rootId.
        /// Srts root ID
        /// </summary>
        /// <param name="session"></param>
        /// <param name=""></param>
        /// <param name="viewId"></param>
        public void SetView(/*Session session, BrowseViewType viewType, NodeId viewId*/)
        {
            NodeId rootId;
            // check if session is connected.
            if (session == null || !session.Connected)
            {
              
                return;
            }

            browser = new Browser(session);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = null;
            browser.IncludeSubtypes = true;
            browser.NodeClassMask = 0;
            browser.ContinueUntilDone = false;

            // set root id
            rootId = Objects.RootFolder;

            // check if session is connected.
            if (browser == null || !browser.Session.Connected)
            {
                return;
            }

            // find root node by Id
            INode node = browser.Session.NodeCache.Find(rootId);

            if (node == null)
            {
                return;
            }

            ReferenceDescription reference = new ReferenceDescription();

            reference.ReferenceTypeId = ReferenceTypeIds.References;
            reference.IsForward = true;
            reference.NodeId = node.NodeId;
            reference.NodeClass = (NodeClass)node.NodeClass;
            reference.BrowseName = node.BrowseName;
            reference.DisplayName = node.DisplayName;
            reference.TypeDefinition = null;

            // root text
            string text = GetTargetText(reference);
            this.rootId = rootId;
        }

        /// <summary>
        /// Returns to display text for the target of a reference.
        /// </summary>
        public string GetTargetText(ReferenceDescription reference)
        {
            if (reference != null)
            {
                if (reference.DisplayName != null && !String.IsNullOrEmpty(reference.DisplayName.Text))
                {
                    return reference.DisplayName.Text;
                }

                if (reference.BrowseName != null)
                {
                    return reference.BrowseName.Name;
                }
            }

            return null;
        }
        /// <summary>
        /// 
        /// </summary>
        public void Browse()
        {
            // set view 
            //SetView(session, null);

            // find root node by Id
            ReferenceDescriptionCollection references = null;

            Browse(rootId, null, out references);

        }

        public void GetNodebyId(string nodeId)
        {
            //session.FetchNamespaceTables();
            

            NodeId _node = new NodeId("Server", 0);

            INode node = session.NodeCache.Find(_node);
            ReferenceDescriptionCollection references = null;

            Browse(nodeId, null, out references);






        }

        public void GetNodebyId(uint nodeId)
        {

            NodeId _nodeId = new NodeId(nodeId);

            // find root node by Id
            INode node = browser.Session.NodeCache.Find(_nodeId);
            ReferenceDescriptionCollection references = null;
            Browse(_nodeId, null, out references);



        }

        public void TraverseTree()
        {

        }

        /// <summary>
        /// Browses the server address space and adds the targets to the tree.
        /// </summary>
        public void Browse(NodeId nodeId, ReferenceDescription referenceDescription, out ReferenceDescriptionCollection references)
        {
            // save node being browsed.
            //m_nodeToBrowse = node;

            // find node to browse.
            ReferenceDescription reference = referenceDescription as  ReferenceDescription;
            
            if (referenceDescription == null &&  nodeId == null)
            {
                references = null;
                return;
            }

            // fetch references.
            if (reference != null)
            {
                references = browser.Browse((NodeId)reference.NodeId);
            }
            else
            {
                references = browser.Browse(nodeId);
            }

            // add nodes to tree.
            //AddReferences(m_nodeToBrowse, references);

            return;
        }
        /// <summary>
        /// Not functional. Moved to CreateSession().
        /// </summary>
        public void Run()
        {
            try
            {
                //ConsoleSampleClient().Wait();
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                return;
            }

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(clientRunTime);

            // return error conditions
            if (session.KeepAliveStopped)
            {
                exitCode = -1;
                return;
            }

            exitCode = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task CreateSession(string configPath = "")
        {
            Console.WriteLine("1 - Create an Application Configuration.");
            exitCode = -1;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "DefaultClient",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "DefaultClient"
            };

            ApplicationConfiguration config;

            // load the application configuration.
            if (configPath.Length > 0)
            {
                //filePath = ApplicationConfiguration.GetFilePathFromAppConfig("DefaultClient");            

                System.IO.FileInfo fileInfo = new System.IO.FileInfo(configPath + "\\" + application.ConfigSectionName + ".Config.xml");

                config = await ApplicationConfiguration.Load(fileInfo, ApplicationType.Client, null);

                application.ApplicationConfiguration = config;
            }
            else
                config = await application.LoadApplicationConfiguration(false);


                // check the application certificate.
                bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {
                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    autoAccept = true;
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
            }

            Console.WriteLine("2 - Discover endpoints of {0}.", endpointURL);
            exitCode = -1;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, haveAppCertificate, 15000);
            Console.WriteLine("    Selected endpoint uses: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Console.WriteLine("3 - Create a session with OPC UA server.");
            exitCode = -1; 
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            Console.WriteLine("4 - Register KeepAlive .");
            session.KeepAlive += Client_KeepAlive;

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="e"></param>
        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="validator"></param>
        /// <param name="e"></param>
        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }
    }

}
