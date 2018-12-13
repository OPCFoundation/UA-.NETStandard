/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#define CUSTOM_NODE_MANAGER

using Opc.Ua.Server;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Opc.Ua.Sample
{
    /// <summary>
    /// A class which implements an instance of a UA server.
    /// </summary>
    public partial class PubSubSampleServer : StandardServer
    {
        Opc.Ua.Sample.PubSub.PublisherNodeManager _PublisherNodeManager;
        #region Overridden Methods
        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the 
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            Utils.Trace("The server is starting.");

            base.OnServerStarting(configuration);     
            
            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validators for SAML and X509 identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            
            // request notifications when the user identity is changed. all valid users are accepted by default.
            server.SessionManager.ImpersonateUser += new ImpersonateEventHandler(SessionManager_ImpersonateUser);
            if(_PublisherNodeManager!=null)
            {
              Task serverstartedTask=new Task(()=>_PublisherNodeManager.OnServerStarted());
                serverstartedTask.Start();
            }
        }

        /// <summary>
        /// Cleans up before the server shuts down.
        /// </summary>
        /// <remarks>
        /// This method is called before any shutdown processing occurs.
        /// </remarks>
        protected override void OnServerStopping()
        {
            Debug.WriteLine("The Server is stopping.");

            base.OnServerStopping();
            
            #if INCLUDE_Sample
            CleanSampleModel();
            #endif
        }
        
        #if CUSTOM_NODE_MANAGER
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// 
        /// Applications with small address spaces do not need to create their own NodeManagers and can add any
        /// application specific nodes to the CoreNodeManager. Applications should use custom NodeManagers when
        /// the structure of the address space is stored in another system or when the address space is too large
        /// to keep in memory.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Debug.WriteLine("Creating the Node Managers.");

            List<INodeManager> nodeManagers = new List<INodeManager>();

            // create the custom node managers.
              _PublisherNodeManager = new global::Opc.Ua.Sample.PubSub.PublisherNodeManager(server, configuration);
            nodeManagers.Add(_PublisherNodeManager);
            nodeManagers.Add(new global::TestData.TestDataNodeManager(server, configuration));
            nodeManagers.Add(new global::MemoryBuffer.MemoryBufferNodeManager(server, configuration));
            nodeManagers.Add(new global::Boiler.BoilerNodeManager(server, configuration));

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }
        #endif

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName      = "OPC UA PubSub SDK Samples";
            properties.ProductUri       = "http://opcfoundation.org/UA/Samples/v1.0";
            properties.SoftwareVersion  = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber      = Utils.GetAssemblyBuildNumber();
            properties.BuildDate        = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            // for (int ii = 0; ii < certificates.Count; ii++)
            // {
            //    properties.SoftwareCertificates.Add(certificates[ii]);
            // }

            return properties; 
        }

        /// <summary>
        /// Initializes the address space after the NodeManagers have started.
        /// </summary>
        /// <remarks>
        /// This method can be used to create any initialization that requires access to node managers.
        /// </remarks>
        protected override void OnNodeManagerStarted(IServerInternal server)
        {
            Debug.WriteLine("The NodeManagers have started.");

            // allow base class processing to happen first.
            base.OnNodeManagerStarted(server); 
            
            // adds the sample information models to the core node manager. 
            #if INCLUDE_Sample
            InitializeSampleModel();
            #endif
        }
                
        #if USER_AUTHENTICATION
        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(server, configuration);
            
            // add some localized strings to the resource manager to demonstrate that localization occurs.
            resourceManager.Add("InvalidPassword", "de-DE", "Das Passwort ist nicht gültig für Konto '{0}'.");
            resourceManager.Add("InvalidPassword", "es-ES", "La contraseña no es válida para la cuenta de '{0}'.");

            resourceManager.Add("UnexpectedUserTokenError", "fr-FR", "Une erreur inattendue s'est produite lors de la validation utilisateur.");
            resourceManager.Add("UnexpectedUserTokenError", "de-DE", "Ein unerwarteter Fehler ist aufgetreten während des Anwenders.");
           
            return resourceManager;
        }
        #endif
        #endregion
    }
}
