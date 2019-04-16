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
using System.Xml;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Configuration;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

using OpcRcw.Da;
using OpcRcw.Comn;

using Opc.Ua.Client;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Com.Server.Da
{	
	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
	[ComVisible(true)]
	[Guid(Server.DaProxyGuid)]
	[ProgId("OpcUa.DaProxy")]
	public class Server : 
		ConnectionPointContainer,
		IOPCCommon,
		IOPCServer, 
		IOPCBrowseServerAddressSpace, 
		IOPCItemProperties,
		IOPCBrowse, 
		IOPCItemIO,
		IOPCWrappedServer
	{	
		#region Construction and Initialization
		/// <summary>
		/// Initializes the object with the default values.
		/// </summary>
		protected Server()
		{
			RegisterInterface(typeof(OpcRcw.Comn.IOPCShutdown).GUID);
		}
        		
        /// <summary>
        /// The GUID 
        /// </summary>
        private const string DaProxyGuid = "25501B7C-2E39-4fd8-BA5A-FAB081375498";

        /// <summary>
        /// The DefaultServerUrl 
        /// </summary>
        private const string DefaultServerUrl = "http://localhost:51211/UA/SampleServer";

        /// <summary>
        /// Creates a new session.
        /// </summary>
        private Session CreateSession(Guid clsid)
        {
            // lookup the cached configuration information.
            ConfiguredEndpoint endpoint = null;
            bool previouslyConnected = true;

            if (!m_verifiedEndpoints.TryGetValue(clsid, out endpoint))
            {
                endpoint = LoadConfiguredEndpoint(clsid);

                if (endpoint != null)
                {
                    Utils.Trace("Loaded endpoint with URL: {0}", endpoint.EndpointUrl);
                    previouslyConnected = false;
                }
            } 
            
            if (endpoint == null)
            { 
                endpoint = m_endpointCache.Create(DefaultServerUrl);
            }
            
            // Initialize the client configuration.
            // Fetch the current configuration information by connecting to the server's discovery endpoint.
            // This method assumes that the discovery endpoint can be constructed by appending "/discovery" to the URL.
            if (endpoint.UpdateBeforeConnect && !previouslyConnected)
            {
                endpoint.UpdateFromServer(BindingFactory.Default);
                Utils.Trace("Updated endpoint from server: {0}", endpoint.EndpointUrl);
            }

            // Need to specify that the server is trusted by the client application.
            if (!previouslyConnected)
            {
                m_configuration.SecurityConfiguration.AddTrustedPeer(endpoint.Description.ServerCertificate);
            }

            // Set the session keep alive to 600 seconds.
            m_configuration.ClientConfiguration.DefaultSessionTimeout = 600000;

            ServiceMessageContext messageContext = m_configuration.CreateMessageContext();

            // Initialize the channel which will be created with the server.
            ITransportChannel channel = SessionChannel.Create(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true),
                messageContext);

              // Wrap the channel with the session object.
            Session session = new Session(channel, m_configuration, endpoint, null);
            session.ReturnDiagnostics = DiagnosticsMasks.SymbolicId;
            
            // The user login credentials must be provided when opening a session.
            IUserIdentity identity = null;

            if (endpoint.UserIdentity != null)
            {
                identity = new Opc.Ua.UserIdentity(endpoint.UserIdentity);
            }

            // Create the session. This actually connects to the server.
            session.Open("COM Client Session", identity);
        
            // need to fetch the references in order use the node cache.
            session.FetchTypeTree(ReferenceTypeIds.References);

            // save the updated information.
            if (!previouslyConnected)
            {
                try
                {
                    SaveConfiguredEndpoint(clsid, endpoint);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not save SaveConfiguredEndpoint in registry."); 
                }

                m_verifiedEndpoints.Add(clsid, endpoint);
            }
            
            return session;
        }

        /// <summary>
        /// Always accept server certificates.
        /// </summary>
        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

		/// <summary>
		/// Called when the object is loaded by the COM wrapper process.
		/// </summary>
		public virtual void Load(Guid clsid)
		{
            try
            {
                // load the configuration if this is the first time.
                lock (m_staticLock)
                {
                    if (m_configuration == null)
                    {
                        m_configuration = LoadConfiguration();
                        m_endpointCache = new ConfiguredEndpointCollection(m_configuration);
                        m_verifiedEndpoints = new Dictionary<Guid,ConfiguredEndpoint>();
                        m_configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
                    }

                    lock (m_lock)
                    {                    
                        // set the start time.
                        m_startTime = DateTime.UtcNow;

                        // create session.
                        m_session = CreateSession(clsid);

                        // read available LocaleIDs from the server.
                        m_localeIds = GetLocaleIDs();
                                                
					    // look up default time zone.
					    DateTime now = DateTime.Now;

					    m_timebias = (int)-TimeZone.CurrentTimeZone.GetUtcOffset(now).TotalMinutes;

					    if (TimeZone.CurrentTimeZone.IsDaylightSavingTime(now))
					    {
						    m_timebias += 60;
					    }
                    }
                }                
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could connect to server."); 
                throw ComUtils.CreateComException(e);
            }
		}

		/// <summary>
		/// Called when the object is unloaded by the COM wrapper process.
		/// </summary>
		public virtual void Unload()
		{
            // close session.
            lock (m_lock)
            {
                if (m_session != null)
                {
                    m_session.Close();
                }

                if (m_continuationPointTimer != null)
                {
                    m_continuationPointTimer.Dispose();
                    m_continuationPointTimer = null;
                }
            }
        }
        
        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        public void CheckApplicationInstanceCertificate(ApplicationConfiguration configuration)
        {
            X509Certificate2 certificate = configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            if (certificate != null)
            {
                return;
            }

            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            List<string> hostNames = new List<string>();
            hostNames.Add(System.Net.Dns.GetHostName());
            hostNames.Add(System.Net.Dns.GetHostAddresses(hostNames[0])[0].ToString());

            certificate = Opc.Ua.CertificateFactory.CreateCertificate(
                id.StoreType,
                id.StorePath,
                configuration.ApplicationUri,
                configuration.ApplicationName,
                id.SubjectName,
                hostNames,
                1024,
                120);
           
            configuration.CertificateValidator.Update(configuration.SecurityConfiguration);
        }

        /// <summary>
        /// Loads the configuration for the DaProxy server.
        /// </summary>
        private ApplicationConfiguration LoadConfiguration()
        {
            // TBD - normally this information would be loaded from the app.config file, however, 
            // the .NET configuration libraries have a problem loading config sections if the 
            // assembly that defines that config section is not in the same directory as the EXE.
            // Since that is difficult to guarantee and a likely source of endless configuaration
            // problems it is necessary to use something other than the app.config.

            // In this case the XML that would normally stored in the app.config is loaded from the 
            // directory where the wrapper assembly resides.

		    XmlTextReader reader = new XmlTextReader(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName + ".proxyconfig");

            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(ApplicationConfiguration));
                
                ApplicationConfiguration configuration = (ApplicationConfiguration)serializer.ReadObject(reader);

                if (configuration != null)
                {
                    configuration.Validate(ApplicationType.Client);
                }

                CheckApplicationInstanceCertificate(configuration);

                return configuration;
            }
            catch
            {
                // create a default configuration object.
                ApplicationConfiguration configuration = new ApplicationConfiguration();
                                
                // Need to specify the application instance certificate for the client.
                configuration.SecurityConfiguration.ApplicationCertificate             = new CertificateIdentifier();
                configuration.SecurityConfiguration.ApplicationCertificate.StoreType   = CertificateStoreType.Windows;
                configuration.SecurityConfiguration.ApplicationCertificate.StorePath   = "LocalMachine\\My";
                configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = "UA Sample Client";
                
                configuration.Validate(ApplicationType.Client);

                return configuration;
            }
            finally
            {
                reader.Close();
            }
        }
        
        /// <summary>
        /// Reads the UA endpoint information associated the CLSID
        /// </summary>
        /// <param name="clsid">The CLSID used to activate the COM server.</param>
        /// <returns>The endpoint.</returns>
        private ConfiguredEndpoint LoadConfiguredEndpoint(Guid clsid)
        {
            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\ComPseudoServers\\{0}.xml", clsid);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, false);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    return null;
                }

                // open the file.
                FileStream istrm = File.Open(absolutePath, FileMode.Open, FileAccess.Read);

                using (XmlTextReader reader = new XmlTextReader(istrm))
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpoint));
                    return (ConfiguredEndpoint)serializer.ReadObject(reader, false);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error loading endpoint configuration for COM Proxy with CLSID={0}.", clsid);
                return null;
            }
        }

        /// <summary>
        /// Saves the UA endpoint information associated the CLSID.
        /// </summary>
        /// <param name="clsid">The CLSID used to activate the COM server.</param>
        /// <param name="endpoint">The endpoint.</param>
        private void SaveConfiguredEndpoint(Guid clsid, ConfiguredEndpoint endpoint)
        {
            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\ComPseudoServers\\{0}.xml", clsid);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, true);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    absolutePath = Utils.GetAbsoluteFilePath(relativePath, true, false, true);
                }

                // open the file.
                FileStream ostrm = File.Open(absolutePath, FileMode.Open, FileAccess.ReadWrite);

                using (XmlTextWriter writer = new XmlTextWriter(ostrm, System.Text.Encoding.UTF8))
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpoint));
                    serializer.WriteObject(writer, endpoint);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error saving endpoint configuration for COM Proxy with CLSID={0}.", clsid);
            }
        }
		#endregion

        #region Internal Interface
        /// <summary>
		/// Changes the name of an existing group.
		/// </summary>
		internal int SetGroupName(string oldName, string newName)
		{
			lock (m_lock)
			{
				// look up existing group.
				Group group = (Group)m_groups[oldName];

                // validate arguments.
				if (newName == null || newName.Length == 0 || group == null)
				{
					return ResultIds.E_INVALIDARG;
				}

				// check that new name is unique among all groups.
				if (m_groups.ContainsKey(newName))
				{
					return ResultIds.E_DUPLICATENAME;
				}

				// update group table.
				m_groups.Remove(oldName);
				m_groups[newName] = group;

				return ResultIds.S_OK;
			}
		}

        /// <summary>
        /// Sets the last update time.
        /// </summary>
        internal void SetLastUpdate()
        {
            lock (m_lock)
            {
                m_lastUpdateTime = DateTime.UtcNow;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //-----------------------------------------------------------------------------------------------------------//
		#region IOPCCommon Members
		/// <summary>
        /// Sets the Locale ID for the server.
        /// </summary>
		public void SetLocaleID(int dwLcid)
		{
			lock (m_lock)
			{
                if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				try
				{
                    bool localeSupported = false;
                    
                    foreach (int locale in m_localeIds)
                    {
                        if (dwLcid == locale)
                        {
                            if (locale != m_lcid)
                            {
                                UpdateLocale(m_session, ComUtils.GetLocale(locale));
                            }

                            // save the passed locale value.
                            m_lcid = dwLcid;
                            localeSupported = true;
                            break;
                        }
                    }
                    
                    if (!localeSupported) throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Update locale ID for the session.
        /// </summary>
        private void UpdateLocale(Session session, string locale)
        {            
            if (session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

            try
            {
                StringCollection preferredLocales = new StringCollection();
                preferredLocales.Add(locale);
                session.ChangePreferredLocales(preferredLocales);
            }
            catch (Exception e)
            {
                throw ComUtils.CreateComException(e);
            }
        }

        /// <summary>
        /// Get the local IDs from the server.
        /// </summary>
        internal List<int> GetLocaleIDs()
        {            
            lock (m_lock)
            {
                string[] locales = null;
                List<int> localeList = new List<int>();
                DataValueCollection values = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                try
                {
                    ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                    ReadValueId valueId = new ReadValueId();
                    valueId.NodeId = new NodeId(Opc.Ua.Variables.Server_ServerCapabilities_LocaleIdArray);
                    valueId.AttributeId = Attributes.Value;
                    nodesToRead.Add(valueId);

                    // read values from the UA server.
                    ResponseHeader responseHeader = m_session.Read(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        out values,
                        out diagnosticInfos);

                    // validate response from the UA server.
                    ClientBase.ValidateResponse(values, nodesToRead);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                    // add the default locale
                    localeList.Add(ComUtils.LOCALE_SYSTEM_DEFAULT);
                    localeList.Add(ComUtils.LOCALE_USER_DEFAULT);
                    
                    // cache the supported locales.
                    locales = values[0].Value as string[];

                    if (locales != null)
                    {
                        foreach (string locale in locales)
                        {
                            localeList.Add(ComUtils.GetLocale(locale));
                        }
                    }
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }

                return localeList;
            }
        }

        /// <summary>
        /// Gets the available Locale IDs from the server.
        /// </summary>
		public void QueryAvailableLocaleIDs(out int pdwCount, out System.IntPtr pdwLcid)
		{
            if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

			lock (m_lock)
			{
				try
				{
					pdwCount = 0;
					pdwLcid  = IntPtr.Zero;

                    // marshal parameters.
                    if (m_localeIds != null && m_localeIds.Count > 0)
					{
                        pdwLcid = Marshal.AllocCoTaskMem(m_localeIds.Count * Marshal.SizeOf(typeof(int)));

                        int[] lcids = new int[m_localeIds.Count];

                        for (int ii = 0; ii < m_localeIds.Count; ii++)
						{
                            lcids[ii] = m_localeIds[ii];
						}

                        Marshal.Copy(lcids, 0, pdwLcid, m_localeIds.Count);
                        pdwCount = m_localeIds.Count;
					}				
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Gets the current Locale ID from the server.
        /// </summary>
		public void GetLocaleID(out int pdwLcid)
		{			
			lock (m_lock)
			{
				try
				{
					pdwLcid = m_lcid; 
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Gets the error string from the server.
        /// </summary>
        void OpcRcw.Comn.IOPCCommon.GetErrorString(int dwError, out string ppString)
		{
			lock (m_lock)
			{
				try
				{
                    // look up COM errors locally.
                    ppString = ComUtils.GetSystemMessage(dwError, m_lcid);                    
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Sets the name of the client.
        /// </summary>
		public void SetClientName(string szName)
		{
			lock (m_lock)
			{
                m_clientName = szName;
            }
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //-----------------------------------------------------------------------------------------------------------//
        #region IOPCServer Members
        ///<summary>
        /// IOPCServer::GetGroupByName - Returns an additional interface pointer for the group based on its name.
        ///</summary>
		public void GetGroupByName(string szName, ref Guid riid, out object ppUnk)
		{
			lock (m_lock)
            {
                if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (szName == null || szName.Length == 0)
                {
                    throw ComUtils.CreateComException("E_INVALIDARG", ResultIds.E_INVALIDARG);
                }

                try
                {
                    if (m_groups.ContainsKey(szName))
                    {
                        ppUnk = m_groups[szName];
                        return;
                    }
                    else
                    {
                        // group not found.
                        throw ComUtils.CreateComException("E_INVALIDARG", ResultIds.E_INVALIDARG);
                    }
                }
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        ///<summary>
        /// IOPCServer::GetErrorString - Returns the error string for a server specific error code.
        ///</summary>
		public void GetErrorString(int dwError, int dwLocale, out string ppString)
		{
			lock (m_lock)
			{
				try
				{
                    // check if locale is supported
                    if (!IsLocaleSupported(dwLocale))
                    {
                        throw ComUtils.CreateComException("E_INVALIDARG", ResultIds.E_INVALIDARG);
                    }

                    ppString = null;

                    // look up COM errors locally
                    ppString = ComUtils.GetSystemMessage(dwError, dwLocale);
				}
				catch (Exception e) 
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        ///<summary>
        /// Checks if the local ID is supported.
        ///</summary>
        internal bool IsLocaleSupported(int myLocale)
        {            
            lock (m_lock)
            {
                foreach (int locale in m_localeIds)
                {
                    if (myLocale == locale)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        ///<summary>
        /// IOPCServer::RemoveGroup - Deletes the group.
        ///</summary>
		public void RemoveGroup(int hServerGroup, int bForce)
		{
            Group groupToRemove = null;

            try
            {
                lock (m_lock)
                {
                    if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    // validate arguments.
                    if (hServerGroup == 0)
                    {
                        throw ComUtils.CreateComException(ResultIds.S_OK);
                    }

                    // find the group.
                    foreach (KeyValuePair<string,Group> current in m_groups)
                    {
                        if (current.Value.ServerHandle == hServerGroup)
                        {
                            groupToRemove = current.Value;
                            m_groups.Remove(current.Key);
                            break;
                        }
                    }
                }

                // no such group found.
                if (groupToRemove == null)
                {
                    throw ComUtils.CreateComException("E_FAIL", ResultIds.E_FAIL);
                }

                // delete the subscription for the group.
                groupToRemove.Dispose();
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Error removing group.");
                throw ComUtils.CreateComException(e);
            }
		}

        ///<summary>
        /// IOPCServer::CreateGroupEnumerator - Creates various enumerators for the groups provided by the Server.
        ///</summary>
		public void CreateGroupEnumerator(OpcRcw.Da.OPCENUMSCOPE dwScope, ref Guid riid, out object ppUnk)
		{
			lock (m_lock)
			{
				try
				{
					switch (dwScope)
					{
						case OPCENUMSCOPE.OPC_ENUM_PUBLIC:
						case OPCENUMSCOPE.OPC_ENUM_PUBLIC_CONNECTIONS:
						{
							if (riid == typeof(OpcRcw.Comn.IEnumString).GUID)
							{
								ppUnk = new EnumString(null);
                                return;
							}

							if (riid == typeof(OpcRcw.Comn.IEnumUnknown).GUID)
							{
								ppUnk = new EnumUnknown(null);
                                return;
							}

							throw ComUtils.CreateComException("E_NOINTERFACE", ResultIds.E_NOINTERFACE);
						}
					}

					if (riid == typeof(IEnumUnknown).GUID)
					{
						ppUnk = new EnumUnknown(m_groups);
                        return;
					}

					if (riid == typeof(OpcRcw.Comn.IEnumString).GUID)
					{
						ArrayList names = new ArrayList(m_groups.Count);

						foreach (string name in m_groups.Keys)
						{
							names.Add(name);
						}

						ppUnk = new EnumString(names);
                        return;
					}

					throw ComUtils.CreateComException("E_NOINTERFACE", ResultIds.E_NOINTERFACE);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        ///<summary>
        /// IOPCServer::AddGroup - Adds a group to the Server.
        ///</summary>
		public void AddGroup(
			string        szName, 
			int           bActive, 
			int           dwRequestedUpdateRate, 
			int           hClientGroup, 
			System.IntPtr pTimeBias, 
			System.IntPtr pPercentDeadband, 
			int           dwLCID, 
			out int       phServerGroup, 
			out int       pRevisedUpdateRate, 
			ref Guid      riid,
			out object    ppUnk)
		{
			lock (m_lock)
			{
				try
				{
                    if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    // validate other arguments.
                    float deadband = 0;

                    if (pPercentDeadband != IntPtr.Zero)
                    {
                        float[] array = new float[1];
                        Marshal.Copy(pPercentDeadband, array, 0, 1);
                        deadband = array[0];
                    }

                    // check if locale is supported.
                    if (dwLCID != 0)
                    {
                        if (!IsLocaleSupported(dwLCID))
                        {
                            // throw ComUtils.CreateComException("E_INVALIDARG", ResultIds.E_INVALIDARG);
                        }
                    }
                    
					// look up default time zone.
					int timebias = m_timebias;

					// use specifed time zone bias.
					if (pTimeBias != IntPtr.Zero)
					{
						timebias = Marshal.ReadInt32(pTimeBias);
					}

                    // throttle the update rates.
                    if (dwRequestedUpdateRate < 100)
                    {
                        dwRequestedUpdateRate = 100;
                    }

					// create a new group.
                    int serverHandle = 0;
                    int revisedUpdateRate = 0;

					Group group = CreateGroup(
                        szName,
                        hClientGroup,
                        dwRequestedUpdateRate,
                        (bActive != 0),
                        deadband,
                        dwLCID, 
                        timebias,
                        out serverHandle,
                        out revisedUpdateRate);

                    // return results.
					phServerGroup      = serverHandle;
					pRevisedUpdateRate = revisedUpdateRate;
                    ppUnk = group;                    
				}
				catch (Exception e)
                {
                    Utils.Trace(e, "Error creating group.");
                    throw ComUtils.CreateComException(e);
				}
			}
		}
        
		/// <summary>
		/// Creates a new group.
		/// </summary>
		internal Group CreateGroup(
            string       name,
            int          clientHandle,
            int          updateRate,
            bool         active,
            float        deadband,
			int          lcid,
			int          timebias,
            out int      serverHandle,
            out int      revisedUpdateRate)
		{
			lock (m_lock)
			{
                Group newGroup = null;
                
                // assign a unique handle.
                serverHandle = ++m_nextHandle;

                // generate a unique name.
                if (String.IsNullOrEmpty(name))
                {
                    name = String.Format("Group_{0:D4}", serverHandle);
                }

                // make sure that the group name is unique.
                if (m_groups.ContainsKey(name))
                {
                    throw ComUtils.CreateComException("E_DUPLICATENAME", ResultIds.E_DUPLICATENAME);
                }
                
                // create UA subscription.
                Subscription subscription = new Subscription(m_session.DefaultSubscription);

                // fill in the subscription parameters
                subscription.DisplayName        = name;
                subscription.PublishingInterval = updateRate;
                subscription.KeepAliveCount     = 10; // may be overridden by SetKeepAlive.
                subscription.PublishingEnabled  = false; 
                subscription.TimestampsToReturn = TimestampsToReturn.Both;

                // create UA subscripton.
                m_session.AddSubscription(subscription);
                subscription.Create();
                
                // return revised update rate.
                revisedUpdateRate = (int)subscription.CurrentPublishingInterval;

                // create the cache.
                if (m_cache == null)
                {
                    m_cache = new NodeIdDictionary<CachedValue>();
                }

                // create new group object.
                newGroup = new Group(
                    this, 
                    m_lock,
                    name,
                    serverHandle, 
                    clientHandle,
                    revisedUpdateRate,
                    active,
                    deadband,
                    lcid, 
                    timebias, 
                    subscription,
                    m_cache);

                // save group object.
                m_groups.Add(name, newGroup);

                // start the update thread.
                if (m_updateThread == null)
                {
                    m_updateThread = new Thread(new ThreadStart(OnUpdateGroups));
                    m_updateThread.IsBackground = true;
                    m_updateThread.Name = m_session.SessionName;
                    m_updateThread.Start();
                }

                // return the new group.
                return newGroup;
			}
		}

        /// <summary>
        /// A thread that periodically polls the groups to see if they have callbacks to send.
        /// </summary>
        private void OnUpdateGroups()
        {
            try
            {
                while (true)
                {
                    lock (m_lock)
                    {
                        // loop until session disconnected.
                        if (!m_session.Connected)
                        {
                            return;
                        }
                        
                        // check all groups.
                        foreach  (KeyValuePair<string,Group> group in m_groups)
                        {
                            try
                            {
                                group.Value.Update(DateTime.UtcNow.Ticks);
                            }
                            catch (Exception e)
                            {
                                Utils.Trace(e, "Unexpected error updating group: {0}", group.Key);
                            }
                        }
                    }

                    // wait before checking again.
                    Thread.Sleep(100);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating groups for Session: {0}", m_session.SessionName);
            }
        }

        ///<summary>
        /// IOPCServer::GetStatus - Gets a current status information about the Server.
        ///</summary>
		public void GetStatus(out System.IntPtr ppServerStatus)
		{
			lock (m_lock)
			{
                // intialize default values.
                OpcRcw.Da.OPCSERVERSTATUS status = new OPCSERVERSTATUS();

                status.ftStartTime      = ComUtils.GetFILETIME(m_startTime);
                status.ftCurrentTime    = ComUtils.GetFILETIME(DateTime.UtcNow);
                status.ftLastUpdateTime = ComUtils.GetFILETIME(m_lastUpdateTime);  
			    status.dwBandWidth      = -1;
			    status.dwGroupCount     = m_groups.Count;
			    status.wReserved        = 0;
                status.dwServerState    = OPCSERVERSTATE.OPC_STATUS_FAILED;
                status.wMajorVersion    = 0;
                status.wMinorVersion    = 0;
                status.wBuildNumber     = 0;
                status.szVendorInfo     = null;
                
				try
				{
                    UpdateServerStatus(ref status);
				}
				catch (Exception e)
				{
                    Utils.Trace(e, "Error reading server status.");
                    // return a status structure with a failed state.
				}

                // marshal parameters.
				ppServerStatus = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OpcRcw.Da.OPCSERVERSTATUS)));
				Marshal.StructureToPtr(status, ppServerStatus, false);
			}
		}

        /// <summary>
        /// Updates the server status structure with the values from the server.
        /// </summary>
        private void UpdateServerStatus(ref OpcRcw.Da.OPCSERVERSTATUS status)
        {
            // read the current status.
            ReadValueId nodeToRead  = new ReadValueId();

            nodeToRead.NodeId       = Opc.Ua.Variables.Server_ServerStatus;
            nodeToRead.AttributeId  = Attributes.Value;
            nodeToRead.DataEncoding = null;
            nodeToRead.IndexRange   = null;

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Read(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                out values,
                out diagnosticInfos);
            
            // validate response from the UA server.
            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // check for read error.
            if (StatusCode.IsBad(values[0].StatusCode))
            {
                return;
            }

            // get remote status.
            ServerStatusDataType remoteStatus = ExtensionObject.ToEncodeable(values[0].Value as ExtensionObject) as ServerStatusDataType;

            if (remoteStatus == null)
            {
                return;
            }

            // extract the times.
            status.ftStartTime   = ComUtils.GetFILETIME(remoteStatus.StartTime);
            status.ftCurrentTime = ComUtils.GetFILETIME(remoteStatus.CurrentTime);

            // convert the server state.          
            switch (remoteStatus.State)
            {
                case ServerState.Running:               { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_RUNNING; break; }
                case ServerState.Suspended:             { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_SUSPENDED; break; }
                case ServerState.CommunicationFault:    { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_COMM_FAULT; break; }
                case ServerState.Failed:                { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_FAILED; break; }
                case ServerState.NoConfiguration:       { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_NOCONFIG; break; }
                case ServerState.Test:                  { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_TEST; break; }
                default:                                { status.dwServerState = OPCSERVERSTATE.OPC_STATUS_FAILED; break; }
            }

            BuildInfo buildInfo = remoteStatus.BuildInfo;

            // construct the vendor info.
            status.szVendorInfo = String.Format("{0}, {1}", buildInfo.ProductName, buildInfo.ManufacturerName);

            // parse the software version.
            if (!String.IsNullOrEmpty(buildInfo.SoftwareVersion))
            {
                string[] fields = buildInfo.SoftwareVersion.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

                if (fields.Length > 0)
                {
                    status.wMajorVersion = ExtractVersionFromString(fields[0]);
                }
                
                if (fields.Length > 1)
                {
                    status.wMinorVersion = ExtractVersionFromString(fields[1]);
                }
            }

            if (!String.IsNullOrEmpty(buildInfo.BuildNumber))
            {                        
                string[] fields = buildInfo.SoftwareVersion.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

                if (fields.Length > 0)
                {
                    status.wBuildNumber = ExtractVersionFromString(fields[0]);
                }
            }
        }

        /// <summary>
        /// Extracts the version number from a string.
        /// </summary>
        private short ExtractVersionFromString(string version)
        {
            if (String.IsNullOrEmpty(version))
            {
                return 0;
            }

            int count = 0;

            while (count < version.Length && Char.IsDigit(version[count]))
            {
                count++;
            }

            try
            {
                return System.Convert.ToInt16(version.Substring(0, count));
            }
            catch
            {
                return 0;
            }
        }
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		#region IOPCBrowse Members
		/// <summary>
        /// IOPCBrowse::Browse - Browses a single branch of the address space and returns zero or more OPCBROWSEELEMENT structures.
		/// </summary>
		public void Browse(
			string            szItemID, 
			ref System.IntPtr pszContinuationPoint, 
			int               dwMaxElementsReturned, 
			OPCBROWSEFILTER   dwBrowseFilter,
			string            szElementNameFilter, 
			string            szVendorFilter,
			int               bReturnAllProperties, 
			int               bReturnPropertyValues, 
			int               dwPropertyCount, 
			int[]             pdwPropertyIDs, 
			out int           pbMoreElements,
			out int           pdwCount, 
			out System.IntPtr ppBrowseElements)
		{
            try
            {
                NodeId startId = ItemIdToNodeId(szItemID);

                if (startId == null)
                {
                    throw new COMException("E_INVALIDITEMID", ResultIds.E_INVALIDITEMID);
                }

        	    // unmarshal continuation point.
			    string continuationPoint = null;

			    if (pszContinuationPoint != IntPtr.Zero)
			    {
				    continuationPoint = Marshal.PtrToStringUni(pszContinuationPoint);
			    }

                // lookup continuation point.
                ContinuationPoint cp = null;

                if (!String.IsNullOrEmpty(continuationPoint))
                {
                    // find existing continuation point.
                    lock (m_lock)
                    {
                        for (LinkedListNode<ContinuationPoint> current = m_continuationPoints.First; current != null; current = current.Next)
                        {
                            if (current.Value.Id == continuationPoint)
                            {
                                cp = current.Value;
                                m_continuationPoints.Remove(current);
                                break;
                            }
                        }
                    }

                    // no longer valid.
                    if (cp == null)
                    {
                        throw new COMException("E_INVALIDCONTINUATIONPOINT", ResultIds.E_INVALIDCONTINUATIONPOINT);
                    }
                }                    

                // find references.
                IList<BrowseElement> elements = Browse(
                    startId,
                    ref cp, 
                    dwMaxElementsReturned,
                    dwBrowseFilter,
                    szElementNameFilter);

                // save continuation point.
                if (cp != null)
                {
                    cp.Id = Guid.NewGuid().ToString();
                    m_continuationPoints.AddLast(cp);
				    pszContinuationPoint = Marshal.StringToCoTaskMemUni(cp.Id);
                }
                
			    // return a valid the continution point.
			    if (pszContinuationPoint == IntPtr.Zero)
			    {
				    pszContinuationPoint = Marshal.StringToCoTaskMemUni(String.Empty);
			    }

                // fetch any properties.
                if (bReturnAllProperties != 0 || dwPropertyCount > 0)
                {                        
                    for (int ii = 0; ii < elements.Count; ii++)
                    { 
                        elements[ii].Properties = GetItemProperties(
                            elements[ii].Node,  
                            bReturnAllProperties != 0, 
                            pdwPropertyIDs, 
                            bReturnPropertyValues != 0);
                    }
                }
		
				// marshal return arguments.
				pbMoreElements   = 0;
				pdwCount         = 0;
				ppBrowseElements = IntPtr.Zero;

				if (elements != null)
				{
					pdwCount         = elements.Count;
					ppBrowseElements = GetBrowseElements(elements);
				}
			}
			catch (Exception e)
			{
				throw ComUtils.CreateComException(e);
			}
		}

        /// <summary>
        /// Finds the references that meet the browse criteria.
        /// </summary>
        private IList<BrowseElement> Browse(
			NodeId                startId, 
            ref ContinuationPoint cp,
			int                   dwMaxElementsReturned, 
			OPCBROWSEFILTER       dwBrowseFilter,
			string                szElementNameFilter)
        {
            IList<BrowseElement> filteredResults = new List<BrowseElement>();
            IList<BrowseElement> unprocessedResults = new List<BrowseElement>();

            byte[] continuationPoint = null;

            while (dwMaxElementsReturned == 0 || filteredResults.Count < dwMaxElementsReturned)
            {
                ReferenceDescriptionCollection currentBatch = null;

                if (cp == null)
                {
                    currentBatch = Browse(startId, dwMaxElementsReturned, out continuationPoint);

                    if (continuationPoint != null)
                    {
                        cp = new ContinuationPoint();
                        cp.LastCP = continuationPoint;
                        cp.UnprocessedElements = null;
                    }
                }
                else
                {
                    if (cp.UnprocessedElements != null)
                    {
                        // add any unprocessed results from the previous call.
                        for (int ii = 0; ii < cp.UnprocessedElements.Count; ii++)
                        {
                            if (dwMaxElementsReturned == 0 || filteredResults.Count < dwMaxElementsReturned)
                            {
                                filteredResults.Add(cp.UnprocessedElements[ii]);
                            }
                            else
                            {
                                unprocessedResults.Add(cp.UnprocessedElements[ii]);
                            }
                        }

                        // check if all done.
                        if (unprocessedResults.Count == 0 && cp.LastCP == null)
                        {
                            cp = null;
                            break;
                        }

                        // check for max results.
                        if (unprocessedResults.Count > 0)
                        {
                            cp = new ContinuationPoint();
                            cp.LastCP = null;
                            cp.UnprocessedElements = unprocessedResults;
                            break;                     
                        }
                    }

                    // get the next batch.
                    if (cp != null && cp.LastCP != null)
                    {
                        continuationPoint = cp.LastCP;
                        currentBatch = BrowseNext(ref continuationPoint);

                        if (continuationPoint != null)
                        {
                            cp = new ContinuationPoint();
                            cp.LastCP = continuationPoint;
                            cp.UnprocessedElements = null;
                        }
                    }
                }

                // apply the filters.
                ApplyFilters(
                    currentBatch, 
                    dwBrowseFilter, 
                    szElementNameFilter, 
                    dwMaxElementsReturned,
                    filteredResults,
                    unprocessedResults);

                // check if all done.
                if (unprocessedResults.Count == 0 && (cp == null || cp.LastCP == null))
                {
                    cp = null;
                    break;
                }

                // check for max results.
                if (unprocessedResults.Count > 0)
                {
                    cp = new ContinuationPoint();
                    cp.LastCP = continuationPoint;
                    cp.UnprocessedElements = unprocessedResults;
                    break;                     
                }
            }

            return filteredResults;
        }       

        /// <summary>
        /// Gets the properties for the browse element.
        /// </summary>
        private List<ItemProperty> GetItemProperties(
            INode node,
            bool  returnAllProperties,
            int[] propertyIds,
            bool  returnPropertyValues)
        {
            int[] availablePropertyIds = GetAvailableProperties(node);

            List<ItemProperty> properties = new List<ItemProperty>();
            
            // determine whether any of the request properties are supported.
            if (propertyIds != null && propertyIds.Length > 0)
            {
                for (int ii = 0; ii < propertyIds.Length; ii++)
                {
                    bool exists = false;

                    for (int jj = 0; jj < availablePropertyIds.Length; jj++)
                    {
                        if (availablePropertyIds[jj] == propertyIds[ii])
                        {
                            exists = true;
                            break;
                        }
                    }

                    ItemProperty property = new ItemProperty();
                    
                    property.Id = propertyIds[ii];
                    property.ErrorId = ResultIds.S_OK;

                    if (!exists)
                    {
                        property.ErrorId = ResultIds.E_INVALID_PID;
                    }

                    properties.Add(property);
                }
            } 

            // return all available properties.
            else
            {
                for (int ii = 0; ii < availablePropertyIds.Length; ii++)
                {
                    ItemProperty property = new ItemProperty();
                    
                    property.Id = availablePropertyIds[ii];
                    property.ErrorId = ResultIds.S_OK;

                    properties.Add(property);
                }
            }

            // get description and datatype.
            List<int> valuesToFetch = new List<int>(properties.Count);
            List<int> indexesForValues = new List<int>(properties.Count);

            for (int ii = 0; ii < properties.Count; ii++)
            {
                ItemProperty property = properties[ii];

                if (property.ErrorId == 0)
                {
                    property.Description = PropertyIds.GetDescription(property.Id);
                    property.DataType = (short)PropertyIds.GetVarType(property.Id);

                    if (returnPropertyValues)
                    {
                        valuesToFetch.Add(property.Id);
                        indexesForValues.Add(ii);
                    }
                }
            }

            // fetch any property values.
            if (valuesToFetch.Count > 0)
            {
                try
                {
                    int[] errors = null;

                    object[] values = ReadPropertyValues(node, valuesToFetch, out errors);
                    
                    // update property objects.
                    for (int ii = 0; ii < errors.Length; ii++)
                    {
                        ItemProperty property = properties[indexesForValues[ii]];

                        property.Value = values[ii];
                        property.ErrorId = errors[ii];

                        if (property.ErrorId < 0)
                        {
                            property.Value = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    // update property objects with errors.
                    int error = Marshal.GetHRForException(e);

                    for (int ii = 0; ii < indexesForValues.Count; ii++)
                    {
                        ItemProperty property = properties[indexesForValues[ii]];

                        property.Value = null;
                        property.ErrorId = error;
                    }
                }
            }

            // save the properties in the element.
            return properties;
        }

        /// <summary>
        /// Applies the browse filters to a set of references.
        /// </summary>
        private void ApplyFilters(
            ReferenceDescriptionCollection currentBatch, 
			OPCBROWSEFILTER                dwBrowseFilter,
			string                         szElementNameFilter, 
            int                            maxReferencesReturned,
            IList<BrowseElement>           filteredResults,
            IList<BrowseElement>           unprocessedResults)
        {
            // apply the filters to each item in the batch.
            foreach (ReferenceDescription reference in currentBatch)
            {
                // check for a valid node.
                INode target = m_session.NodeCache.FetchNode(reference.NodeId);

                if (target == null)
                {
                    continue;
                }
                
                // create the browse element.
                BrowseElement element = new BrowseElement();
                
                element.Node   = target;
                element.Name   = target.BrowseName.Name;
                element.ItemId = NodeIdToItemId(target.NodeId);
    
                // apply the element name filter.
                if (!String.IsNullOrEmpty(szElementNameFilter))
                {
                    if (!ComUtils.Match(element.Name, szElementNameFilter, true))
                    {
                        continue;
                    }
                }

                element.IsItem = IsItem(target);
                element.HasChildren = HasChildren(target);

                // apply the browse filter.
                if (dwBrowseFilter != OPCBROWSEFILTER.OPC_BROWSE_FILTER_ALL)
                {
                    if (dwBrowseFilter == OPCBROWSEFILTER.OPC_BROWSE_FILTER_ITEMS)
                    {
                        if (!element.IsItem)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (!element.HasChildren)
                        {
                            continue;
                        }
                    }
                }               

                // must save the results to return in a subsequent browse.
                if (maxReferencesReturned != 0 && filteredResults.Count >= maxReferencesReturned)
                {
                    unprocessedResults.Add(element);
                }

                // can return the results in this browse.
                else
                {
                    filteredResults.Add(element);
                }
            }
        }

        /// <summary>
        /// Browses the specified node.
        /// </summary>
        private ReferenceDescriptionCollection Browse(
            NodeId     nodeId, 
            int        maxReferencesReturned,
            out byte[] continuationPoint)
        {  
            try
            {
                // fetch initial set of references.
                ReferenceDescriptionCollection references;

                m_session.Browse(
                    null,
                    null,
                    nodeId,
                    (uint)maxReferencesReturned,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)(NodeClass.Object | NodeClass.Variable),
                    out continuationPoint,
                    out references);

                // return the results.
                return references;
            }
            catch (Exception e)
            {
                throw new COMException(e.Message, MapBrowseStatusToErrorCode(e));
            }
        }

        /// <summary>
        /// Continues the browse operation.
        /// </summary>
        private ReferenceDescriptionCollection BrowseNext(ref byte[] continuationPoint)
        {  
            try
            {
                ReferenceDescriptionCollection references;
                byte[] revisedContuinationPoint;

                m_session.BrowseNext(
                    null,
                    false,
                    continuationPoint,
                    out revisedContuinationPoint,
                    out references);

                // return the results.
                return references;
            }
            catch (Exception e)
            {
                throw new COMException(e.Message, MapBrowseStatusToErrorCode(e));
            }
        }

		/// <summary>
		/// Allocates and marshals an array of OPCBROWSEELEMENT structures.
		/// </summary>
		internal static IntPtr GetBrowseElements(IList<BrowseElement> input)
		{
			IntPtr output = IntPtr.Zero;
			
			if (input != null && input.Count > 0)
			{
				output = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OpcRcw.Da.OPCBROWSEELEMENT))*input.Count);

				IntPtr pos = output;

				for (int ii = 0; ii < input.Count; ii++)
				{
					OpcRcw.Da.OPCBROWSEELEMENT element = GetBrowseElement(input[ii]); 
					Marshal.StructureToPtr(element, pos, false);
					pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCBROWSEELEMENT)));
				}
			}

			return output;
		}

		/// <summary>
		/// Allocates and marshals an OPCBROWSEELEMENT structure.
		/// </summary>
		internal static OpcRcw.Da.OPCBROWSEELEMENT GetBrowseElement(BrowseElement input)
		{
			OpcRcw.Da.OPCBROWSEELEMENT output = new OpcRcw.Da.OPCBROWSEELEMENT();
			
			if (input != null)
			{
				output.szName         = input.Name;
				output.szItemID       = input.ItemId;
				output.dwFlagValue    = 0;
				output.ItemProperties = GetItemProperties(input.Properties);

				if (input.IsItem)
				{
					output.dwFlagValue |= OpcRcw.Da.Constants.OPC_BROWSE_ISITEM;
				}

				if (input.HasChildren)
				{
					output.dwFlagValue |= OpcRcw.Da.Constants.OPC_BROWSE_HASCHILDREN;
				}
			}

			return output;
		}

		/// <summary>
		/// Allocates and marshals an array of OPCITEMPROPERTIES structures.
		/// </summary>
		internal static OpcRcw.Da.OPCITEMPROPERTIES GetItemProperties(IList<ItemProperty> input)
		{
			OpcRcw.Da.OPCITEMPROPERTIES output = new OpcRcw.Da.OPCITEMPROPERTIES();
			
			if (input != null && input.Count > 0)
			{
				output.hrErrorID       = ResultIds.S_OK;
				output.dwReserved      = 0;
				output.dwNumProperties = input.Count;
				output.pItemProperties = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMPROPERTY))*input.Count);

				bool error = false;

				IntPtr pos = output.pItemProperties;

				for (int ii = 0; ii < input.Count; ii++)
				{
					OpcRcw.Da.OPCITEMPROPERTY property = GetItemProperty(input[ii]); 
					Marshal.StructureToPtr(property, pos, false);
					pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMPROPERTY)));

					if (input[ii].ErrorId < 0)
					{
						error = true;
					}
				}

				// set flag indicating one or more properties contained errors.
				if (error)
				{
					output.hrErrorID = ResultIds.S_FALSE;
				}
			}

			return output;
		}
        
		/// <summary>
		/// Allocates and marshals an arary of OPCITEMPROPERTY structures.
		/// </summary>
		internal static OpcRcw.Da.OPCITEMPROPERTY GetItemProperty(ItemProperty input)
		{
			OpcRcw.Da.OPCITEMPROPERTY output = new OpcRcw.Da.OPCITEMPROPERTY();

			if (input != null)
			{
				output.dwPropertyID  = input.Id;
				output.szDescription = input.Description;
				output.vtDataType    = input.DataType;
				output.vValue        = ComUtils.GetVARIANT(input.Value);
				output.wReserved     = 0;
				output.hrErrorID     = input.ErrorId;
			}

			return output;
		}

		/// <summary>
		/// Allocates and marshals an array of OPCITEMPROPERTIES structures.
		/// </summary>
		internal static IntPtr GetItemProperties(IList<OpcRcw.Da.OPCITEMPROPERTIES> input)
		{
            IntPtr output = IntPtr.Zero;
			
			if (input != null && input.Count > 0)
			{
                output = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMPROPERTIES))*input.Count);

				IntPtr pos = output;

				for (int ii = 0; ii < input.Count; ii++)
				{
					Marshal.StructureToPtr(input[ii], pos, false);
					pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMPROPERTIES)));
				}
			}

			return output;
		}
		        
		/// <summary>
        /// IOPCBrowse::GetProperties - Returns an array of OPCITEMPROPERTIES, one for each ItemID.
		/// </summary>
		public void GetProperties(
            int               dwItemCount, 
            string[]          pszItemIDs, 
            int               bReturnPropertyValues, 
            int               dwPropertyCount, 
            int[]             pdwPropertyIDs, 
            out System.IntPtr ppItemProperties)
		{
			lock (m_lock)
			{
				try
				{
					if (dwItemCount == 0 || pszItemIDs == null)
					{
						throw ComUtils.CreateComException("E_INVALIDARG", ResultIds.E_INVALIDARG);
					}

					ppItemProperties = IntPtr.Zero;

                    OPCITEMPROPERTIES[] results = new OPCITEMPROPERTIES[dwItemCount];
                                                           
                    for (int ii = 0; ii < dwItemCount; ii++)
                    {
                        // initialize result.
                        OPCITEMPROPERTIES result = new OPCITEMPROPERTIES();

				        result.hrErrorID       = ResultIds.S_OK;
				        result.dwReserved      = 0;
				        result.dwNumProperties = 0;
				        result.pItemProperties = IntPtr.Zero;
                        
                        // lookup item id.
                        NodeId nodeId = ItemIdToNodeId(pszItemIDs[ii]);
                        
                        if (nodeId == null)
                        { 
                            result.hrErrorID = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        // find node.
                        INode node = m_session.NodeCache.FetchNode(nodeId);
                        
                        if (node == null)
                        { 
                            result.hrErrorID = ResultIds.E_UNKNOWNITEMID;
                            continue;
                        }
                        
                        // get properties.
                        List<ItemProperty> properties = GetItemProperties(
                            node,  
                            false, 
                            pdwPropertyIDs, 
                            bReturnPropertyValues != 0);

                        // marshal properties.
                        result = GetItemProperties(properties);
                    }
                    
                    ppItemProperties = GetItemProperties(results);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		#region IOPCBrowseServerAddressSpace Members

        /// <summary>
        /// IOPCBrowseServerAddressSpace::GetItemID - Provides a way to assemble a 'fully qualified' ITEM ID in a hierarchical space.
        /// </summary>
		public void GetItemID(
            string szItemDataID, 
            out string szItemID)
		{
			lock (m_lock)
			{
				try
                {
                    // find the current node.
                    INode node = null;

                    // ensure browse stack has been initialized.
                    if (m_browseStack.Count == 0)
                    {
                        node = m_session.NodeCache.Find(Objects.ObjectsFolder);
                        m_browseStack.Push(node);
                    }

                    node = m_browseStack.Peek();
                    
                    // check if looking for the current node.
                    if (String.IsNullOrEmpty(szItemDataID))
                    {
                        szItemID = NodeIdToItemId(node.NodeId);
                        return;
                    }
                    
                    // attempt to find the child name.
                    node = FindChildByName(node.NodeId, szItemDataID);
                    
                    if (node != null)
                    {
                        szItemID = NodeIdToItemId(node.NodeId); 
                        return;
                    }

                    // check for a fully qualified item id.
                    NodeId nodeId = ItemIdToNodeId(szItemDataID);

                    if (nodeId != null)
                    {
                        node = m_session.NodeCache.Find(nodeId);

                        if (node != null)
                        {
                            szItemID = NodeIdToItemId(node.NodeId);
                            return;
                        }
                    }

                    // not found.
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        // AMaksumov: replaces characters that CTT cannot handle
        internal string ConvertToItemID(string myString)
        {
            string myNewString = "";

            if (myString != null && myString != String.Empty)
            {
                myNewString = myString.Replace("=", "*");
            }

            return myNewString;
        }

        // AMaksumov: puts replaced characters back to string
        internal string ConvertFromItemID(string myString)
        {
            string myNewString = "";

            if (myString != null && myString != String.Empty)
            {
                myNewString = myString.Replace("*", "=");
            }

            return myNewString;
        }
                
        /// <summary>
        /// Parses an ItemId (returns null if parsing fails).
        /// </summary>
        internal static NodeId ItemIdToNodeId(string itemId)
        {
            // check for root.
            if (String.IsNullOrEmpty(itemId))
            {
                return Objects.ObjectsFolder;
            }

            // replace then '=' signs in item id because the CTT does not like them.
            StringBuilder buffer = new StringBuilder(itemId.Length);

            for (int ii = 0; ii < itemId.Length; ii++)
            {
                if (itemId[ii] == '*')
                {
                    buffer.Append('=');
                }
                else
                {
                    buffer.Append(itemId[ii]);
                }
            }

            // parse the node id.
            try
            {
                return NodeId.Parse(buffer.ToString());
            }
            catch 
            {
                return null;
            }
        }
                        
        /// <summary>
        /// Converts a NodeId to an ItemId
        /// </summary>
        internal static string NodeIdToItemId(ExpandedNodeId nodeId)
        {
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            if (nodeId == Objects.ObjectsFolder)
            {
                return String.Empty;
            }

            // remove '=' signs from item id because the CTT does not like them.
            string itemId = nodeId.ToString();
            StringBuilder buffer = new StringBuilder(itemId.Length);

            for (int ii = 0; ii < itemId.Length; ii++)
            {
                if (itemId[ii] == '=')
                {
                    buffer.Append('*');
                }
                else
                {
                    buffer.Append(itemId[ii]);
                }
            }

            return buffer.ToString();
        }

        /// <summary>
        /// IOPCBrowseServerAddressSpace::BrowseAccessPaths - Provides a way to browse the available AccessPaths for an item ID - not supported.
        /// </summary>
		public void BrowseAccessPaths(
            string                      szItemID, 
            out OpcRcw.Comn.IEnumString ppIEnumString)
		{
			lock (m_lock)
			{
				// access paths not supported.
				try
				{
					throw ComUtils.CreateComException("BrowseAccessPaths", ResultIds.E_NOTIMPL);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCBrowseServerAddressSpace::QueryOrganization - Determines if the ServerAddressSpace is flat or hierarchical.
        /// </summary>
		public void QueryOrganization(out OpcRcw.Da.OPCNAMESPACETYPE pNameSpaceType)
		{
			lock (m_lock)
			{
				// only hierarchial spaces supported.
				try
				{
					pNameSpaceType = OPCNAMESPACETYPE.OPC_NS_HIERARCHIAL;
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}

            // return ResultIds.S_OK;
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
            foreach (INode node in m_session.NodeCache.Find(startNode.NodeId, ReferenceTypeIds.HierarchicalReferences, true, true))
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

                // check if objects folder found.
                if (node.NodeId == Objects.ObjectsFolder)
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

            // path does not lead to the objects folder.
            return false;
        }

        /// <summary>
        /// Finds the child node with the specified browse name.
        /// </summary>
        private INode FindChildByName(ExpandedNodeId startId, string browseName)
        {
            // find all parent nodes.
            foreach (INode child in m_session.NodeCache.Find(startId, ReferenceTypeIds.HierarchicalReferences, false, true))
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
                if (browseName == child.BrowseName.Name)
                {
                    return child;
                }
            }

            // child with specified browse name does not exist.
            return null;
        }

        /// <summary>
        /// IOPCBrowseServerAddressSpace::ChangeBrowsePosition - Provides a way to move UP or DOWN or TO in a hierarchical space.
        /// </summary>
		public void ChangeBrowsePosition(OpcRcw.Da.OPCBROWSEDIRECTION dwBrowseDirection, string szString)
		{
			lock (m_lock)
			{
				try
                {
                    switch (dwBrowseDirection)
                    {
                        // move to a specified position or root.
                        case OPCBROWSEDIRECTION.OPC_BROWSE_TO:
                        {
                            // move to root.
                            if (String.IsNullOrEmpty(szString))
                            {
                                m_browseStack.Clear();
                                m_browseStack.Push(m_session.NodeCache.Find(Objects.ObjectsFolder));
                                break;
                            }
                            
                            // parse the item id.
                            NodeId nodeId = ItemIdToNodeId(szString);

                            if (nodeId == null)
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }
                            
                            // find the current node.
                            INode node = m_session.NodeCache.Find(nodeId);

                            if (node == null)
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }

                            // only can browse to branch
                            if (!HasChildren(node))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }

                            // build a new browse stack.
                            Stack<INode> browseStack = new Stack<INode>();

                            if (!FindPathToNode(node, browseStack))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }

                            // push target node onto stack.
                            browseStack.Push(m_session.NodeCache.Find(nodeId));
                            
                            m_browseStack = browseStack;
                            break;
                        }

                        // move to a child branch.
                        case OPCBROWSEDIRECTION.OPC_BROWSE_DOWN:
                        {
                            // check for invalid name.
                            if (String.IsNullOrEmpty(szString))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
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
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }
                            
                            // only can browse to branch
                            if (!HasChildren(child))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                            }

                            // save the new position.
                            m_browseStack.Push(child);
                            break;
                        }

                        // move to a parent branch.
                        case OPCBROWSEDIRECTION.OPC_BROWSE_UP:
                        {
                            // check for invalid name.
                            if (!String.IsNullOrEmpty(szString))
                            {
                                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
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
                            throw ComUtils.CreateComException(ResultIds.E_FAIL);
                        }
                    }
                }
                catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Applies the browse filters to a node.
        /// </summary>
        private bool ApplyFilters(
            INode                     child,
            OpcRcw.Da.OPCBROWSETYPE   dwBrowseFilterType, 
            string                    szFilterCriteria, 
            short                     vtDataTypeFilter, 
            int                       dwAccessRightsFilter)
        {
            bool hasChildren = HasChildren(child);   

            // apply branch filter.
            if (dwBrowseFilterType == OPCBROWSETYPE.OPC_BRANCH && !hasChildren)
            {
                return false;
            }

            // apply leaf filter.
            if (dwBrowseFilterType == OPCBROWSETYPE.OPC_LEAF && hasChildren)
            {
                return false;
            }

            // check for items.                                
            bool isItem = IsItem(child);

            if (!isItem && !hasChildren)
            {
                return false;
            }

            // apply flat filter.
            if (dwBrowseFilterType == OPCBROWSETYPE.OPC_FLAT && !isItem)
            {
                return false;
            }

            // apply name filter.
            if (dwBrowseFilterType != OPCBROWSETYPE.OPC_FLAT)
            {
                if (!String.IsNullOrEmpty(szFilterCriteria))
                {
                    if (!ComUtils.Match(child.BrowseName.Name, szFilterCriteria, true))
                    {
                        return false;
                    }
                }
            }
            
            // check for variable.
            VariableNode variable = child as VariableNode;

            if (variable != null)
            {
				// apply access right filter. 
				if (dwAccessRightsFilter != 0) 
				{ 
					if (dwAccessRightsFilter == OpcRcw.Da.Constants.OPC_READABLE && ((variable.AccessLevel & AccessLevels.CurrentRead) == 0)) 
					{ 
                        return false;
					} 
                    
					if (dwAccessRightsFilter == OpcRcw.Da.Constants.OPC_WRITEABLE && ((variable.AccessLevel & AccessLevels.CurrentWrite) == 0)) 
					{ 
                        return false;
					} 
				} 

                // lookup the requested datatype.
                if (vtDataTypeFilter != 0)
                {
                    bool isArray = false;
                    NodeId requestedDataTypeId = ComUtils.GetDataTypeId(vtDataTypeFilter, out isArray);

                    VarEnum varType = DataTypeToVarType(variable.DataType, variable.ValueRank);

                    if (varType != VarEnum.VT_VARIANT && (short)varType != vtDataTypeFilter)
                    {
                        return false;
                    }
                }

                // must match item id rather than name when browsing flat.
                if (dwBrowseFilterType == OPCBROWSETYPE.OPC_FLAT)
                {
                    if (!String.IsNullOrEmpty(szFilterCriteria))
                    {
                        string itemId = NodeIdToItemId(child.NodeId);

                        if (!ComUtils.Match(itemId, szFilterCriteria, true))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Browses the children of a node.
        /// </summary>
        private void BrowseChildren(
            INode                     parent,
            OpcRcw.Da.OPCBROWSETYPE   dwBrowseFilterType, 
            string                    szFilterCriteria, 
            short                     vtDataTypeFilter, 
            int                       dwAccessRightsFilter,
            List<INode>               hits)
        {
            Dictionary<string,INode> nodes = new Dictionary<string,INode>();

            // find children.
            IList<INode> children = m_session.NodeCache.Find(parent.NodeId, ReferenceTypeIds.HierarchicalReferences, false, true);

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
                if (nodes.ContainsKey(child.BrowseName.Name))
                {
                    continue;
                }

                bool match = ApplyFilters(
                    child,
                    dwBrowseFilterType,
                    szFilterCriteria,
                    vtDataTypeFilter,
                    dwAccessRightsFilter);

                // add child.
                if (match)
                {
                    hits.Add(child);
                    nodes.Add(child.BrowseName.Name, child);
                }                 
                
                // recursively follow children when browsing flat.
                if (dwBrowseFilterType == OPCBROWSETYPE.OPC_FLAT)
                {
                    BrowseChildren(
                        child,
                        dwBrowseFilterType,
                        szFilterCriteria,
                        vtDataTypeFilter,
                        dwAccessRightsFilter,
                        hits);
                }
            }
        }

        /// <summary>
        /// IOPCBrowseServerAddressSpace::BrowseOPCItemIDs - Gets an IENUMString for a list of ItemIDs as determined by the passed parameters.
        /// </summary>
		public void BrowseOPCItemIDs(
            OpcRcw.Da.OPCBROWSETYPE     dwBrowseFilterType, 
            string                      szFilterCriteria, 
            short                       vtDataTypeFilter, 
            int                         dwAccessRightsFilter, 
            out OpcRcw.Comn.IEnumString ppIEnumString)
		{
			lock (m_lock)
			{
				try
				{
                    // find the current node.
                    INode parent = null;

                    // ensure browse stack has been initialized.
                    if (m_browseStack.Count == 0)
                    {
                        parent = m_session.NodeCache.Find(Objects.ObjectsFolder);
                        m_browseStack.Push(parent);
                    }

                    parent = m_browseStack.Peek();

                    if (parent == null)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_FAIL);
                    }

                    // apply filters.
                    List<INode> hits = new List<INode>();
                    
                    BrowseChildren(
                        parent,
                        dwBrowseFilterType,
                        szFilterCriteria,
                        vtDataTypeFilter,
                        dwAccessRightsFilter,
                        hits);

                    // build list of names to return.
                    Dictionary<string,INode> nodes = new Dictionary<string,INode>();
                    List<string> names = new List<string>();

                    foreach (INode child in hits)
                    {
                        string name = child.BrowseName.Name;

                        if (dwBrowseFilterType == OPCBROWSETYPE.OPC_FLAT)
                        {
                            name = NodeIdToItemId(child.NodeId);
                        }

                        if (!nodes.ContainsKey(name))
                        {
                            nodes.Add(name, child);
                            names.Add(name);
                        }
                    }

					// create enumerator.
					ppIEnumString = (OpcRcw.Comn.IEnumString)new EnumString(names);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        private List<string> m_names = new List<string>();
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //-----------------------------------------------------------------------------------------------------------//
        #region IOPCItemProperties Members
		
        /// <summary>
        /// IOPCItemProperties::QueryAvailableProperties - Returns a list of ID codes and descriptions for the available 
        ///                                                properties for this ITEMID
        /// </summary>
		public void QueryAvailableProperties(
            string            szItemID, 
            out int           pdwCount, 
            out System.IntPtr ppPropertyIDs, 
            out System.IntPtr ppDescriptions, 
            out System.IntPtr ppvtDataTypes)
		{			
			lock (m_lock)
			{
				try
				{
					// validate item id.
					if (String.IsNullOrEmpty(szItemID))
					{
						throw ComUtils.CreateComException(ResultIds.E_INVALIDITEMID);
					}

                    // find the node id.
                    NodeId nodeId = ItemIdToNodeId(szItemID);
 
                    if (nodeId == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // find the node.
                    INode node = m_session.NodeCache.Find(nodeId);
                     
                    if (node == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // find the available properties.
                    int[] propertyIds = GetAvailableProperties(node);
                    string[] descriptions = new string[propertyIds.Length];
                    short[] datatypes = new short[propertyIds.Length];

                    for (int ii = 0; ii < propertyIds.Length; ii++)
                    {
                        descriptions[ii] = PropertyIds.GetDescription(propertyIds[ii]);
                        datatypes[ii] = (short)PropertyIds.GetVarType(propertyIds[ii]);
                    }

					// marshal the results.
					pdwCount       = propertyIds.Length;
					ppPropertyIDs  = ComUtils.GetInt32s(propertyIds);
					ppDescriptions = ComUtils.GetUnicodeStrings(descriptions);
					ppvtDataTypes  = ComUtils.GetInt16s(datatypes);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
        
        /// <summary>
        /// Converts a exception returned during a Browse to an HRESULT.
        /// </summary>
        private int MapBrowseStatusToErrorCode(Exception e)
        { 
            COMException ce = e as COMException;

            if (ce != null)
            {
                return ce.ErrorCode;
            }

            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
            {
                return MapBrowseStatusToErrorCode(sre.StatusCode);
            }

            return ResultIds.E_FAIL;
        }

        /// <summary>
        /// Converts a StatusCode returned during a Browse to an HRESULT.
        /// </summary>
        private int MapBrowseStatusToErrorCode(StatusCode statusCode)
        {  
            // map good status.
            if (StatusCode.IsGood(statusCode))
            {
                return ResultIds.S_OK;
            }
            
            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadContinuationPointInvalid: { return ResultIds.E_INVALIDCONTINUATIONPOINT; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_UNKNOWNITEMID; }
                }

                return ResultIds.E_FAIL;
            }

            // uncertain values for property reads are errors.
            return ResultIds.E_FAIL;
        }
        
        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        private int MapPropertyReadStatusToErrorCode(StatusCode statusCode)
        {  
            // map good status.
            if (StatusCode.IsGood(statusCode))
            {
                return ResultIds.S_OK;
            }
            
            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory:        { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid:      { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNodeIdUnknown:      { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNotReadable:        { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied:   { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadTypeMismatch:       { return ResultIds.E_BADTYPE; }
                }

                return ResultIds.E_FAIL;
            }

            // uncertain values for property reads are errors.
            return ResultIds.E_FAIL;
        }
        
        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        internal static int MapReadStatusToErrorCode(StatusCode statusCode)
        {  
            // map bad well known status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory:        { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid:      { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown:      { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotReadable:        { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied:   { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadUnexpectedError:    { return ResultIds.E_FAIL; }
                    case StatusCodes.BadInternalError:      { return ResultIds.E_FAIL; }
                    case StatusCodes.BadSessionClosed:      { return ResultIds.E_FAIL; }
                    case StatusCodes.BadTypeMismatch:       { return ResultIds.E_BADTYPE; }
                }
            }

            // all other values are mapped to quality codes.
            return ResultIds.S_OK;
        }
        
        /// <summary>
        /// Converts a StatusCode returned during a Write to an HRESULT.
        /// </summary>
        internal static int MapWriteStatusToErrorCode(StatusCode statusCode)
        {  
            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory:        { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid:      { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown:      { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotWritable:        { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied:   { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadTypeMismatch:       { return ResultIds.E_BADTYPE; }
                    case StatusCodes.BadWriteNotSupported:  { return ResultIds.E_NOTSUPPORTED; }
                    case StatusCodes.BadOutOfRange:         { return ResultIds.E_RANGE; }
                }
                
                return ResultIds.E_FAIL;
            }

            // ignore uncertain and success codes.
            return ResultIds.S_OK;
        }

        /// <summary>
        /// Converts a VARIANT value to a Builtin Type.
        /// </summary>
        private object VariantValueToScalarValue(object value, NodeId builtinTypeId)
        {        
            switch ((uint)builtinTypeId.Identifier)
            {
                case DataTypes.Guid:
                {
                    return new Uuid((string)value);
                }

                case DataTypes.XmlElement:
                {    
                    XmlDocument document = new XmlDocument();
                    document.InnerXml = (string)value;
                    return document.DocumentElement;
                }

                case DataTypes.NodeId:
                {
                    return NodeId.Parse((string)value);
                }

                case DataTypes.ExpandedNodeId:
                {
                    return ExpandedNodeId.Parse((string)value);
                }

                case DataTypes.QualifiedName:
                {
                    return QualifiedName.Parse((string)value);
                }

                case DataTypes.LocalizedText:
                {
                    return new LocalizedText(ComUtils.GetLocale(m_lcid), (string)value);
                }

                case DataTypes.StatusCode:
                {
                     return new StatusCode((uint)value);
                }

                case DataTypes.DiagnosticInfo:
                {
                    BinaryDecoder decoder = new BinaryDecoder((byte[])value, m_session.MessageContext);
                    DiagnosticInfo decodedValue = decoder.ReadDiagnosticInfo(null);
                    decoder.Close();
                    return decodedValue; 
                }

                case DataTypes.DataValue:
                {
                    BinaryDecoder decoder = new BinaryDecoder((byte[])value, m_session.MessageContext);
                    DataValue decodedValue = decoder.ReadDataValue(null);
                    decoder.Close();
                    return decodedValue; 
                }

                case DataTypes.Structure:
                {
                    BinaryDecoder decoder = new BinaryDecoder((byte[])value, m_session.MessageContext);
                    ExtensionObject decodedValue = decoder.ReadExtensionObject(null);
                    decoder.Close();
                    return decodedValue; 
                }
            }

            return value;
        }

        /// <summary>
        /// Converts a VARIANT value to a Builtin Type.
        /// </summary>
        private object VariantValueToArrayValue(Array value, NodeId builtinTypeId)
        {
            if (value == null)
            {
                return null;
            }

            Type elementType = TypeInfo.GetSystemType(builtinTypeId, m_session.Factory);

            if (elementType == typeof(Variant))
            {
                elementType = typeof(object);
            }

            Array result = Array.CreateInstance(elementType, value.Length);

            for (int ii = 0; ii < value.Length; ii++)
            {
                object elementValue = VariantValueToValue(value.GetValue(ii), builtinTypeId);
                result.SetValue(elementValue, ii);
            }
            
            return result;
        }
        
        /// <summary>
        /// Converts a VARIANT value to specified built-in type.
        /// </summary>
        private object VariantValueToValue(object value, NodeId builtinTypeId)
        {
            if (value == null)
            {
                return null;
            }

            Array array = value as Array;

            if (array != null && !typeof(byte[]).IsInstanceOfType(value))
            {
                return VariantValueToArrayValue(array, builtinTypeId);
            }

            return VariantValueToScalarValue(value, builtinTypeId);
        }

        /// <summary>
        /// Converts a value to the canonical type.
        /// </summary>
        internal object VariantValueToValue(INode node, object value, out int error)
        {
            error = ResultIds.E_BADTYPE;

            lock (m_lock)
            {
                // VT_EMPTY is never accepted as a valid value.
                if (value == null)
                {
                    error = ResultIds.E_BADTYPE;
                    return null;
                }

                // check for variable.
                VariableNode variable = node as VariableNode;

                if (variable == null)
                {
                    return null;
                }

                try
                {
                    error = ResultIds.S_OK;

                    // lookup canonical type.
                    VarEnum canonicalType = DataTypeToVarType(variable.DataType, variable.ValueRank);

                    if (canonicalType == VarEnum.VT_EMPTY)
                    {
                        error = ResultIds.S_OK;
                        return value;
                    }                             

                    // change type.
                    object canonicalValue = null;
                    error = ComUtils.ChangeTypeForCOM(value, canonicalType, out canonicalValue);

                    if (error < 0)
                    {
                        return null;
                    }

                    // find the target built-in type.
                    BuiltInType builtInType = GetBuiltInType(variable.DataType);
                    
                    // convert to UA value.
                    return VariantValueToValue(canonicalValue, new NodeId((uint)(int)builtInType));                
                }
                catch (COMException e)
                {
                    error = e.ErrorCode;
                }
                catch (Exception)
                {
                    error = ResultIds.E_FAIL;
                }

                return null;
            }
        }

        /// <summary>
        /// Converts a value to a VARIANT compatible object.
        /// </summary>
        private object ValueToVariantValue(object value, out int error)
        {
            error = ResultIds.S_OK;

            try
            {
                return ComUtils.GetVARIANT(value);
            }
            catch
            {
                error = ResultIds.DISP_E_TYPEMISMATCH;
                return null;
            }
        }
        
        /// <summary>
        /// Converts a value to a VARIANT compatible object.
        /// </summary>
        private object ValueToVariantValue(object value)
        {
            return ComUtils.GetVARIANT(value);
        }

        /// <summary>
        /// Extracts a property value from a data value.
        /// </summary>
        private object DataValueToPropertyValue(DataValue value, Type expectedType, out int error)
        {
            error = ResultIds.E_INVALID_PID;

            // check for null.
            if (value == null)
            {
                return null;
            }

            // check status code.
            error = MapPropertyReadStatusToErrorCode(value.StatusCode);

            if (error < 0)
            {
                return null;
            }

            if (expectedType != null)
            {
                // check for scalar extension object.
                ExtensionObject extension = value.Value as ExtensionObject;

                if (extension != null)
                {
                    if (expectedType.IsInstanceOfType(extension.Body))
                    {
                        return extension.Body;
                    }
                    
                    error = ResultIds.E_BADTYPE;
                    return null;
                }

                // check for array of extension objects.
                ExtensionObject[] extensions = value.Value as ExtensionObject[];
                
                if (extensions != null)
                {
                    Type elementType = value.GetType().GetElementType();

                    // handle explicit type conversion.
                    Array result = Array.CreateInstance(elementType, extensions.Length);

                    for (int ii = 0; ii < extensions.Length; ii++)
                    {
                        result.SetValue(extensions[ii].Body, ii);
                    }

                    return result;
                }
                
                // check if a specific type was requested.
                if (!expectedType.IsInstanceOfType(value.Value))
                {
                    error = ResultIds.E_BADTYPE;
                    return null;
                }

                return value.Value;
            }

            // convert value.
            return ValueToVariantValue(value.Value, out error);
        }
        
        /// <summary>
        /// Checks if the datatype id is a built-in type.
        /// </summary>
        private bool IsBuiltInType(ExpandedNodeId nodeId, out BuiltInType builtinType)
        {
            builtinType = BuiltInType.Null;

            if (nodeId.IsAbsolute || nodeId.NamespaceIndex != 0 || nodeId.IdType != IdType.Numeric)
            {
                return false;
            }

            uint id = (uint)nodeId.Identifier;

            if (id > 0 && id <= DataTypes.BaseDataType)
            {
                builtinType = (BuiltInType)(int)id;
                return true;
            }

            switch (id)
            {
                case DataTypes.Enumeration: { builtinType = BuiltInType.Int32;  break; }
                case DataTypes.Number:      { builtinType = BuiltInType.Double; break; }
                case DataTypes.Integer:     { builtinType = BuiltInType.Int64;  break; }
                case DataTypes.UInteger:    { builtinType = BuiltInType.UInt64; break; }

                default:
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds the built-in type for the datatype.
        /// </summary>
        private BuiltInType GetBuiltInType(NodeId datatypeId)
        {
            BuiltInType builtinType = BuiltInType.Variant;
 
            ExpandedNodeId subtypeId = datatypeId;

            while (!IsBuiltInType(subtypeId, out builtinType))
            {     
                IList<INode> supertypeIds = m_session.NodeCache.Find(subtypeId, ReferenceTypeIds.HasSubtype, true, true);

                if (supertypeIds.Count == 0)
                {
                    break;
                }

                subtypeId = supertypeIds[0].NodeId;
            }

            return builtinType;
        }

        /// <summary>
        /// Converts a datatype to a vartype.
        /// </summary>
        internal VarEnum DataTypeToVarType(NodeId datatypeId, int valueRank)
        {
            // variables which can be scalars or arrays must be VT_VARIANT.
            if (valueRank < ValueRanks.Scalar)
            {
                return VarEnum.VT_EMPTY;
            }
 
            VarEnum varType = VarEnum.VT_EMPTY;
            
            // find built-in type for data type.
            BuiltInType builtinType = GetBuiltInType(datatypeId);

            switch (builtinType)
            {
                case BuiltInType.Boolean:         { varType = VarEnum.VT_BOOL; break; }
                case BuiltInType.SByte:           { varType = VarEnum.VT_I1;   break; }
                case BuiltInType.Byte:            { varType = VarEnum.VT_UI1;  break; }
                case BuiltInType.Int16:           { varType = VarEnum.VT_I2;   break; }
                case BuiltInType.UInt16:          { varType = VarEnum.VT_UI2;  break; }
                case BuiltInType.Int32:           { varType = VarEnum.VT_I4;   break; }
                case BuiltInType.UInt32:          { varType = VarEnum.VT_UI4;  break; }
                case BuiltInType.Int64:           { varType = VarEnum.VT_I8;   break; }
                case BuiltInType.UInt64:          { varType = VarEnum.VT_UI8;  break; }
                case BuiltInType.Float:           { varType = VarEnum.VT_R4;   break; }
                case BuiltInType.Double:          { varType = VarEnum.VT_R8;   break; }
                case BuiltInType.String:          { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.DateTime:        { varType = VarEnum.VT_DATE; break; }
                case BuiltInType.Guid:            { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.ByteString:      { varType = VarEnum.VT_UI1 | VarEnum.VT_ARRAY; break; }
                case BuiltInType.XmlElement:      { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.NodeId:          { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.ExpandedNodeId:  { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.StatusCode:      { varType = VarEnum.VT_UI4;  break; }
                case BuiltInType.QualifiedName:   { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.LocalizedText:   { varType = VarEnum.VT_BSTR; break; }
                case BuiltInType.ExtensionObject: { varType = VarEnum.VT_UI1 | VarEnum.VT_ARRAY; break; }
                case BuiltInType.DataValue:       { varType = VarEnum.VT_EMPTY; break; }                  
                case BuiltInType.Variant:         { varType = VarEnum.VT_VARIANT; break; }
            }
            
            // check for array values.
            if (valueRank >= ValueRanks.OneOrMoreDimensions)
            {
                if ((varType & VarEnum.VT_ARRAY) != 0)
                {
                    varType = VarEnum.VT_VARIANT;
                }
  
                return varType | VarEnum.VT_ARRAY;
            }

            // return scalar value.
            return varType;
        }

        /// <summary>
        /// Reads a set of property values for a node.
        /// </summary>
        private object[] ReadPropertyValues(INode node, IList<int> propertyIds, out int[] errors)
        {
            errors = new int[propertyIds.Count];
            object[] values = new object[propertyIds.Count];

            VariableNode variable = node as VariableNode;

            if (variable == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALID_PID);
            }

            Dictionary<uint,DataValue> attributes = new Dictionary<uint,DataValue>();
            Dictionary<string,DataValue> properties = new Dictionary<string,DataValue>();

            for (int ii = 0; ii < propertyIds.Count; ii++)
            {
                switch (propertyIds[ii])
                {
                    case PropertyIds.DataType:
                    {
                        attributes[Attributes.DataType] = null;
                        attributes[Attributes.ValueRank] = null;
                        break;
                    }

                    case PropertyIds.Value:
                    case PropertyIds.Quality:
                    case PropertyIds.Timestamp:
                    {
                        attributes[Attributes.Value] = null;
                        break;
                    }

                    case PropertyIds.AccessRights:
                    {
                        attributes[Attributes.AccessLevel] = null;
                        break;
                    }
                        
                    case PropertyIds.ScanRate:
                    {
                        attributes[Attributes.MinimumSamplingInterval] = null;
                        break;
                    }
                        
                    case PropertyIds.EuType:
                    {
                        attributes[Attributes.DataType] = null;
                        properties[Opc.Ua.BrowseNames.EURange] = null;
                        properties[Opc.Ua.BrowseNames.EnumStrings] = null;
                        break;
                    }
                        
                    case PropertyIds.EuInfo:
                    {
                        properties[Opc.Ua.BrowseNames.EnumStrings] = null;
                        break;
                    }
                        
                    case PropertyIds.Description:
                    {
                        attributes[Attributes.Description] = null;
                        break;
                    }
                        
                    case PropertyIds.EngineeringUnits:
                    {
                        properties[Opc.Ua.BrowseNames.EngineeringUnits] = null;
                        break;
                    }                        
                            
                    case PropertyIds.HighEU:
                    case PropertyIds.LowEU:
                    {
                        properties[Opc.Ua.BrowseNames.EURange] = null;
                        break;
                    }
                        
                    case PropertyIds.HighIR:
                    case PropertyIds.LowIR:
                    {
                        properties[Opc.Ua.BrowseNames.InstrumentRange] = null;
                        break;
                    }
                        
                    case PropertyIds.OpenLabel:
                    {
                        properties[Opc.Ua.BrowseNames.FalseState] = null;
                        break;
                    }
                        
                    case PropertyIds.CloseLabel:
                    {
                        properties[Opc.Ua.BrowseNames.TrueState] = null;
                        break;
                    }

                    case PropertyIds.TimeZone:
                    {
                        properties[Opc.Ua.BrowseNames.LocalTime] = null;
                        break;
                    }
                }
            }

            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            // build list of attributes to read.
            foreach (uint attributeId in attributes.Keys)
            {
                ReadValueId nodeToRead = new ReadValueId();

                nodeToRead.NodeId      = ExpandedNodeId.ToNodeId(node.NodeId, m_session.NamespaceUris);
                nodeToRead.AttributeId = attributeId;

                valuesToRead.Add(nodeToRead);

                // save a handle to correlate the results with the request.
                nodeToRead.Handle = attributeId;
            }
            
            // build list of properties to read.
            foreach (string propertyName in properties.Keys)
            {
                VariableNode propertyNode = m_session.NodeCache.Find(
                    node.NodeId, 
                    ReferenceTypeIds.HasProperty, 
                    false, 
                    true, 
                    new QualifiedName(propertyName)) as VariableNode;
                
                if (propertyNode == null)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();

                nodeToRead.NodeId      = propertyNode.NodeId;
                nodeToRead.AttributeId = Attributes.Value;

                valuesToRead.Add(nodeToRead);

                // save a handle to correlate the results with the request.
                nodeToRead.Handle = propertyName;
            }

            // read the values.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            if (valuesToRead.Count > 0)
            {
                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    valuesToRead,
                    out results,
                    out diagnosticInfos);

                // validate response from the UA server.
                ClientBase.ValidateResponse(results, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);
            }

            // update tables.
            for (int ii = 0; ii < valuesToRead.Count; ii++)
            {
                ReadValueId valueToRead = valuesToRead[ii];

                string propertyName = valueToRead.Handle as string;

                if (propertyName != null)
                {
                    properties[propertyName] = results[ii];
                }

                uint? attributeId = valueToRead.Handle as uint?;

                if (attributeId != null)
                {
                    attributes[attributeId.Value] = results[ii];
                }
            }

            // calculate DA property values.
            for (int ii = 0; ii < propertyIds.Count; ii++)
            {
                int errorId = 0;
                int propertyId = propertyIds[ii];

                switch (propertyId)
                {
                    case PropertyIds.DataType:
                    {
                        NodeId datatypeId = (NodeId)DataValueToPropertyValue(attributes[Attributes.DataType], typeof(NodeId), out errorId);

                        if (errorId < 0)
                        {
                            errors[ii] = errorId;
                            break;
                        }
                        
                        int? valueRank = (int?)DataValueToPropertyValue(attributes[Attributes.ValueRank], typeof(int), out errorId);

                        if (errorId < 0)
                        {
                            valueRank = ValueRanks.Scalar;
                            break;
                        }

                        VarEnum varType = DataTypeToVarType(datatypeId, valueRank.Value);

                        if (varType == VarEnum.VT_VARIANT)
                        {
                            varType = VarEnum.VT_EMPTY;
                        }

                        values[ii] = (short)varType;
                        break;
                    }

                    case PropertyIds.Value:
                    case PropertyIds.Quality:
                    case PropertyIds.Timestamp:
                    {
                        DataValue dataValue = attributes[Attributes.Value];

                        if (dataValue == null)
                        {                            
                            errors[ii] = ResultIds.E_INVALID_PID;
                            break;
                        }

                        errors[ii] = MapPropertyReadStatusToErrorCode(dataValue.StatusCode);

                        if (errors[ii] < 0)
                        {
                            break;
                        }

                        if (propertyId == PropertyIds.Timestamp)
                        {
                            values[ii] = dataValue.SourceTimestamp;
                            errors[ii] = ResultIds.S_OK;
                            break;
                        }

                        if (propertyId == PropertyIds.Quality)
                        {
                            values[ii] = ComUtils.GetQualityCode(dataValue.StatusCode);
                            errors[ii] = ResultIds.S_OK;
                            break;
                        }
                        
                        values[ii] = ValueToVariantValue(dataValue.Value, out errorId);
                        errors[ii] = errorId;
                        break;
                    }

                    case PropertyIds.AccessRights:
                    {
                        byte? accessLevel = (byte?)DataValueToPropertyValue(attributes[Attributes.AccessLevel], typeof(byte), out errorId);

                        if (errorId < 0)
                        {
                            errors[ii] = errorId;
                            break;
                        }
                    
                        values[ii] = ComUtils.GetAccessRights(accessLevel.Value);
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.ScanRate:
                    {
                        double? samplingInterval = (double?)DataValueToPropertyValue(attributes[Attributes.MinimumSamplingInterval], typeof(double), out errorId);

                        if (errorId < 0)
                        {
                            errors[ii] = errorId;
                            break;
                        }

                        if (samplingInterval.Value == MinimumSamplingIntervals.Indeterminate)
                        {
                            values[ii] = (float)0;
                        }
                        else
                        {
                            values[ii] = (float)samplingInterval.Value;
                        }

                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.EuType:
                    {
                        // check if datatype attribute supported.
                        DataValue dataValue = attributes[Attributes.DataType];

                        if (dataValue == null || StatusCode.IsBad(dataValue.StatusCode))
                        {                            
                            errors[ii] = ResultIds.E_INVALID_PID;
                            break;
                        }

                        errors[ii] = ResultIds.S_OK;

                        // check for analog items.
                        Range euRange = (Range)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.EURange], typeof(Range), out errorId);

                        if (errorId == 0)
                        {                            
                            values[ii] = (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG;
                            break;
                        }
                        
                        // check for enumerated items.
                        LocalizedText[] enumStrings = (LocalizedText[])DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.EnumStrings], typeof(LocalizedText[]), out errorId);

                        if (errorId == 0)
                        {                            
                            values[ii] = (int)OpcRcw.Da.OPCEUTYPE.OPC_ENUMERATED;
                            break;
                        }

                        // normal item.
                        values[ii] = (int)OpcRcw.Da.OPCEUTYPE.OPC_NOENUM;
                        break;
                    }
                        
                    case PropertyIds.EuInfo:
                    {
                        // check if datatype attribute supported.
                        DataValue dataValue = attributes[Attributes.DataType];

                        if (dataValue == null || StatusCode.IsBad(dataValue.StatusCode))
                        {                            
                            errors[ii] = ResultIds.E_INVALID_PID;
                            break;
                        }

                        errors[ii] = ResultIds.S_OK;

                        // check for enumerated items.
                        LocalizedText[] enumStrings = (LocalizedText[])DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.EnumStrings], typeof(LocalizedText[]), out errorId);

                        if (errorId == 0)
                        {                            
                            values[ii] = ValueToVariantValue(enumStrings);
                            break;
                        }
                        break;
                    }
                        
                    case PropertyIds.Description:
                    {
                        LocalizedText description = (LocalizedText)DataValueToPropertyValue(attributes[Attributes.Description], typeof(LocalizedText), out errorId);

                        if (errorId < 0)
                        {
                            errors[ii] = errorId;
                            break;
                        }

                        values[ii] = ValueToVariantValue(description);
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.EngineeringUnits:
                    {
                        EUInformation euInfo = (EUInformation)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.EngineeringUnits], typeof(EUInformation), out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }                        

                        values[ii] = ValueToVariantValue(euInfo.DisplayName);
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }                        
                            
                    case PropertyIds.HighEU:
                    case PropertyIds.LowEU:
                    {
                        Range euRange = (Range)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.EURange], typeof(Range), out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }

                        if (propertyId == PropertyIds.HighEU)
                        {
                            values[ii] = euRange.High;
                        }
                        else
                        {
                            values[ii] = euRange.Low;
                        }

                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.HighIR:
                    case PropertyIds.LowIR:
                    {
                        Range euRange = (Range)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.InstrumentRange], typeof(Range), out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }

                        if (propertyId == PropertyIds.HighIR)
                        {
                            values[ii] = euRange.High;
                        }
                        else
                        {
                            values[ii] = euRange.Low;
                        }

                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.OpenLabel:
                    {
                        LocalizedText label = (LocalizedText)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.FalseState], typeof(LocalizedText), out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }
                        
                        values[ii] = ValueToVariantValue(label);
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }
                        
                    case PropertyIds.CloseLabel:
                    {
                        LocalizedText label = (LocalizedText)DataValueToPropertyValue(properties[Opc.Ua.BrowseNames.TrueState], typeof(LocalizedText), out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }

                        values[ii] = ValueToVariantValue(label);
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }

                    case PropertyIds.TimeZone:
                    {
                        ExtensionObject extension = (ExtensionObject)DataValueToPropertyValue(
                            properties[Opc.Ua.BrowseNames.LocalTime], 
                            typeof(ExtensionObject), 
                            out errorId);

                        if (errorId < 0)
                        {                            
                            errors[ii] = errorId;
                            break;
                        }

                        TimeZoneDataType timezone = extension.Body as TimeZoneDataType;

                        if (timezone == null)
                        {
                            errors[ii] = ResultIds.DISP_E_TYPEMISMATCH;
                            break;
                        }

                        values[ii] = (int)timezone.Offset;
                        errors[ii] = ResultIds.S_OK;
                        break;
                    }

                    default:
                    {
                        errors[ii] = ResultIds.E_INVALID_PID;
                        break;
                    }
                }
            }           

            return values;
        }

        /// <summary>
        /// IOPCItemProperties::GetItemProperties - Returns a list of the current data values for the passed ID codes
        /// </summary>
		public void GetItemProperties(
            string            szItemID, 
            int               dwCount, 
            int[]             pdwPropertyIDs, 
            out System.IntPtr ppvData, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				try
				{
					// validate arguments.
					if (dwCount == 0 || pdwPropertyIDs == null || dwCount != pdwPropertyIDs.Length)
					{
						throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
					}

                    // no properties at root.
                    if (String.IsNullOrEmpty(szItemID))
                    {
						throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                    }

                    // find the node id.
                    NodeId nodeId = ItemIdToNodeId(szItemID);
 
                    if (nodeId == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // find the node.
                    INode node = m_session.NodeCache.Find(nodeId);
                     
                    if (node == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // read the properties.
                    int[] errors = null;
                    object[] values = null;

                    try
                    {
                        values = ReadPropertyValues(node, pdwPropertyIDs, out errors);
                    }
                    catch (Exception e)
                    {
                        for (int ii = 0; ii < errors.Length; ii++)
                        {
                            errors[ii] = Marshal.GetHRForException(e);
                        }

                        values = new object[errors.Length];
                    }
                    
                    // marshal the return parameters.
				    ppvData  = ComUtils.GetVARIANTs(values, false);					
				    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Returns the available properties for the node.
        /// </summary>
        private int[] GetAvailableProperties(INode node)
        {
            VariableNode variable = node as VariableNode;

            if (variable == null)
            {
                return new int[0];
            }

            List<int> propertyIds = new List<int>();

            propertyIds.Add(PropertyIds.DataType);
            propertyIds.Add(PropertyIds.Value);
            propertyIds.Add(PropertyIds.Quality);
            propertyIds.Add(PropertyIds.Timestamp);
            propertyIds.Add(PropertyIds.AccessRights);
            propertyIds.Add(PropertyIds.ScanRate);
            propertyIds.Add(PropertyIds.EuType);
            propertyIds.Add(PropertyIds.EuInfo);

            if (!LocalizedText.IsNullOrEmpty(variable.Description))
            {
                propertyIds.Add(PropertyIds.Description);
            }

            IList<INode> properties = m_session.NodeCache.Find(node.NodeId, ReferenceTypeIds.HasProperty, false, true);

            foreach (INode property in properties)
            {
                if (property.BrowseName == Opc.Ua.BrowseNames.EURange)
                {
                    propertyIds.Add(PropertyIds.HighEU);
                    propertyIds.Add(PropertyIds.LowEU);
                    continue;
                }
                
                if (property.BrowseName == Opc.Ua.BrowseNames.EngineeringUnits)
                {
                    propertyIds.Add(PropertyIds.EngineeringUnits);
                    continue;
                }        

                if (property.BrowseName == Opc.Ua.BrowseNames.InstrumentRange)
                {
                    propertyIds.Add(PropertyIds.HighIR);
                    propertyIds.Add(PropertyIds.LowIR);
                    continue;
                }        

                if (property.BrowseName == Opc.Ua.BrowseNames.TrueState)
                {
                    propertyIds.Add(PropertyIds.CloseLabel);
                    continue;
                }        

                if (property.BrowseName == Opc.Ua.BrowseNames.FalseState)
                {
                    propertyIds.Add(PropertyIds.OpenLabel);
                    continue;
                }        
                
                if (property.BrowseName == Opc.Ua.BrowseNames.LocalTime)
                {
                    propertyIds.Add(PropertyIds.TimeZone);
                    continue;
                }
            }

            // return as array.
            return propertyIds.ToArray();
        }

        /// <summary>
        /// IOPCItemProperties::LookupItemIDs - Returns a list of ITEMIDs (if available) for each of the passed ID codes
        /// </summary>
        public void LookupItemIDs(
            string szItemID,
            int dwCount,
            int[] pdwPropertyIDs,
            out System.IntPtr ppszNewItemIDs,
            out System.IntPtr ppErrors)
        {
            lock (m_lock)
            {
                try
                {
                    // validate arguments.
                    if (szItemID == null || szItemID.Length == 0 || dwCount == 0 || pdwPropertyIDs == null || dwCount != pdwPropertyIDs.Length)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                    }
                    
					// validate item id.
					if (String.IsNullOrEmpty(szItemID))
					{
						throw ComUtils.CreateComException(ResultIds.E_INVALIDITEMID);
					}

                    // find the node id.
                    NodeId nodeId = ItemIdToNodeId(szItemID);
 
                    if (nodeId == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // find the node.
                    INode node = m_session.NodeCache.Find(nodeId);
                     
                    if (node == null)
                    {
						throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                    }

                    // get the available properties.
                    int[] availableProperties = GetAvailableProperties(node);

                    // no item ids conversions supports but must return the correct error code.
                    string[] itemIds = new string[dwCount];
                    int[] errors = new int[dwCount];

                    for (int ii = 0; ii < itemIds.Length; ii++)
                    {
                        errors[ii] = ResultIds.E_INVALID_PID;

                        for (int jj = 0; jj < availableProperties.Length; jj++)
                        {
                            if (pdwPropertyIDs[ii] > PropertyIds.EuInfo && availableProperties[jj] == pdwPropertyIDs[ii])
                            {
                                errors[ii] = ResultIds.E_FAIL;
                                break;
                            }
                        }
                    }

                    // marshal the return parameters.
				    ppszNewItemIDs  = ComUtils.GetUnicodeStrings(itemIds);					
				    ppErrors = ComUtils.GetInt32s(errors);
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
        }
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		#region IOPCItemIO Members

		/// <summary>
        /// IOPCItemIO::WriteVQT - Writes one or more values, qualities and timestamps for the items specified. 
        ///                        This is functionally similar to the IOPCSyncIO2::WriteVQT except that there is no associated group.
		/// </summary>
		public void WriteVQT(
            int dwCount, 
            string[] pszItemIDs, 
            OPCITEMVQT[] pItemVQT, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || pszItemIDs == null || pItemVQT == null || dwCount != pszItemIDs.Length || dwCount != pItemVQT.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    int[] errors = new int[dwCount];
                    
                    // build list of values to write.
                    WriteValueCollection valuesToWrite = new WriteValueCollection();

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        NodeId nodeId = ItemIdToNodeId(pszItemIDs[ii]);
                        
                        if (nodeId == null)
                        { 
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        VariableNode variable = m_session.NodeCache.Find(nodeId) as VariableNode;

                        if (variable == null)
                        {
                            errors[ii] = ResultIds.E_UNKNOWNITEMID;
                            continue;
                        }

                        WriteValue valueToWrite = new WriteValue();

                        valueToWrite.NodeId      = nodeId;
                        valueToWrite.IndexRange  = null;
                        valueToWrite.AttributeId = Attributes.Value;

                        DataValue value = new DataValue();
                        
                        int error = 0;
                        value.Value = VariantValueToValue(variable, pItemVQT[ii].vDataValue, out error);

                        if (error != ResultIds.S_OK)
                        {
                            errors[ii] = error;
                            continue;
                        }

                        if (pItemVQT[ii].bQualitySpecified != 0)
                        {
                            value.StatusCode = ComUtils.GetQualityCode(pItemVQT[ii].wQuality);
                        }
                        
                        if (pItemVQT[ii].bTimeStampSpecified != 0)
                        {
                            value.SourceTimestamp = ComUtils.GetDateTime(pItemVQT[ii].ftTimeStamp);
                        }

                        valueToWrite.Value = value;
                        
                        // needed to correlate results to input.
                        valueToWrite.Handle = ii;
                        
                        valuesToWrite.Add(valueToWrite);
                    }
                    
                    // write values from server.
                    StatusCodeCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;
                    
                    if (valuesToWrite.Count > 0)
                    {
                        m_session.Write(
                            null,
                            valuesToWrite,
                            out results,
                            out diagnosticInfos);
                    
                        // validate response from the UA server.
                        ClientBase.ValidateResponse(results, valuesToWrite);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);
                    }
                    
                    for (int ii = 0; ii < valuesToWrite.Count; ii++)
                    {
                        // get index in original array.
                        int index = (int)valuesToWrite[ii].Handle;

                        // map UA code to DA code. 
                        errors[index] = MapWriteStatusToErrorCode(results[ii]);
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

		/// <summary>
        /// IOPCItemIO::Read - Reads one or more values, qualities and timestamps for the items specified. 
        ///                    This is functionally similar to the IOPCSyncIO::Read method.
		/// </summary>
		public void Read(
            int               dwCount, 
            string[]          pszItemIDs, 
            int[]             pdwMaxAge, 
            out System.IntPtr ppvValues,
            out System.IntPtr ppwQualities, 
            out System.IntPtr ppftTimeStamps, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_session == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || pszItemIDs == null || pdwMaxAge == null || dwCount != pszItemIDs.Length || dwCount != pdwMaxAge.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    object[] values = new object[dwCount];
                    short[] qualities = new short[dwCount];
                    DateTime[] timestamps = new DateTime[dwCount];
                    int[] errors = new int[dwCount];
                    
                    // use the minimum max age for all items.
                    int maxAge = Int32.MaxValue;

                    // build list of values to read.
                    ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        values[ii]     = null;
                        qualities[ii]  = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;                            
                        timestamps[ii] = DateTime.MinValue;

                        NodeId nodeId = ItemIdToNodeId(pszItemIDs[ii]);
                        
                        if (nodeId == null)
                        { 
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = nodeId;
                        nodeToRead.AttributeId = Attributes.Value;

                        // needed to correlate results to input.
                        nodeToRead.Handle = ii;

                        nodesToRead.Add(nodeToRead);

                        // calculate max age.
                        if (maxAge > pdwMaxAge[ii])
                        {
                            maxAge = pdwMaxAge[ii];
                        }
                    }
                    
                    // read values from server.
                    DataValueCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;
                    
                    if (nodesToRead.Count > 0)
                    {
                        m_session.Read(
                            null,
                            maxAge,
                            TimestampsToReturn.Both,
                            nodesToRead,
                            out results,
                            out diagnosticInfos);
                    
                        // validate response from the UA server.
                        ClientBase.ValidateResponse(results, nodesToRead);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                    }
                    
                    for (int ii = 0; ii < nodesToRead.Count; ii++)
                    {
                        // get index in original array.
                        int index = (int)nodesToRead[ii].Handle;

                        DataValue dataValue = results[ii];

                        if (dataValue == null)
                        {                            
                            errors[index] = ResultIds.E_FAIL;
                            continue;
                        }

                        errors[index] = MapReadStatusToErrorCode(dataValue.StatusCode);

                        if (errors[index] < 0)
                        {
                            continue;
                        }
                    
                        values[index]     = ValueToVariantValue(dataValue.Value);
                        qualities[index]  = ComUtils.GetQualityCode(dataValue.StatusCode);
                        timestamps[index] = dataValue.SourceTimestamp;
                        errors[index]     = ResultIds.S_OK;
                    }

					// marshal results.
					ppvValues      = ComUtils.GetVARIANTs(values, false);
					ppwQualities   = ComUtils.GetInt16s(qualities);
					ppftTimeStamps = ComUtils.GetFILETIMEs(timestamps);

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
		#endregion       

        #region Private Methods
		/// <summary>
		/// Removes all expired continuation points.
		/// </summary>
		private void CleanupContinuationPoints(object state)
		{
            ByteStringCollection continuationPoints = new ByteStringCollection();
            
            lock (m_lock)
            {
                LinkedListNode<ContinuationPoint> node = m_continuationPoints.First;

                while (node != null)
                {
				    if (DateTime.UtcNow.Ticks - node.Value.Timestamp.Ticks > TimeSpan.TicksPerMinute*10)
				    {
                        if (node.Value.LastCP != null)
                        {
                            continuationPoints.Add(node.Value.LastCP);
                        }
                        
                        LinkedListNode<ContinuationPoint> next = node.Next;
                        m_continuationPoints.Remove(node);
                        node = next;
                        continue;
				    }

                    node = node.Next;
                }

                // start a 5 minute timer to check for expired continuation points.
                if (m_continuationPoints.Count > 0)
                {
                    if (m_continuationPointTimer != null)
                    {
                        m_continuationPointTimer.Dispose();
                    }

                    m_continuationPointTimer = new Timer(CleanupContinuationPoints, null, 600000, Timeout.Infinite);
                }
            }

            // release the continuation points on the server.
            if (continuationPoints.Count > 0)
            {
                try
                {
                    BrowseResultCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    m_session.BrowseNext(
                        null,
                        true,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Error releasing continuation points.");
                }
            }
		}

        /// <summary>
        /// Checks is the node is a property that is not visible in the address space. 
        /// </summary>
        private bool IsHiddenProperty(INode property)
        {
            // ignore invalid properties.
            if (property == null || property.BrowseName == null)
            {
                return true;
            }

            // only UA defined properties are hidden.
            if (property.BrowseName.NamespaceIndex != 0)
            {
                return false;
            }

            // check browse name.
            switch (property.BrowseName.Name)
            {
                case Opc.Ua.BrowseNames.EnumStrings:
                case Opc.Ua.BrowseNames.EngineeringUnits:
                case Opc.Ua.BrowseNames.TrueState:
                case Opc.Ua.BrowseNames.FalseState:
                case Opc.Ua.BrowseNames.EURange:
                case Opc.Ua.BrowseNames.InstrumentRange:
                case Opc.Ua.BrowseNames.LocalTime:
                {
                    return true;
                }
            }
              
            // anything else is visable as a property.
            return false;
        }

        /// <summary>
        /// Determines if it's a Branch Node.
        /// </summary>
        private bool HasChildren(INode node)
        {
            // objects are always branches.
            if (node.NodeClass == NodeClass.Object)
            {
                return true;
            }

            // variables could be branches.
            VariableNode variable = node as VariableNode;

            if (variable == null)
            {
                return false;
            }

            // look for children
            IList<IReference> references = variable.ReferenceTable.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_session.TypeTree);

            if (references.Count == 0)
            {
                return false;
            }

            // check for non-hidden children.
            foreach (IReference reference in references)
            {
                if (!m_session.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasProperty))
                {
                    return true;
                }
                
                INode property = m_session.NodeCache.Find(reference.TargetId);

                if (!IsHiddenProperty(property))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if it's a Leaf Node.
        /// </summary>
        private bool IsItem(INode node)
        {           
            // objects are never leaves.
            if (node.NodeClass == NodeClass.Object)
            {
                return false;
            }

            // variables could be leaves.
            VariableNode variable = node as VariableNode;

            if (variable == null)
            {
                return false;
            }

            // check if the node is not visible.
            if (IsHiddenProperty(variable))
            {
                return false;
            }

            // all other variables are leaves.
            return true;
        }
        
		#region ContinuationPoint Class
		/// <summary>
		/// Stores information about a continuation point.
		/// </summary>
		private class ContinuationPoint
		{
			public string Id;
			public DateTime Timestamp;
            public byte[] LastCP;
            public IList<BrowseElement> UnprocessedElements;
			public int MaxElementsReturned;

			public ContinuationPoint()
			{
				Timestamp = DateTime.UtcNow;
				MaxElementsReturned = 0;
			}
		}
		#endregion
        #endregion

		#region Private Fields
        private static object m_staticLock = new object();
        private static ApplicationConfiguration m_configuration;
        private static ConfiguredEndpointCollection m_endpointCache;
        private static Dictionary<Guid,ConfiguredEndpoint> m_verifiedEndpoints;

        private object m_lock = new object();
        private Opc.Ua.Client.Session m_session;
        private string m_clientName;
        private Dictionary<string, Group> m_groups = new Dictionary<string, Group>(); 
		private LinkedList<ContinuationPoint> m_continuationPoints = new LinkedList<ContinuationPoint>();
		private Stack<INode> m_browseStack = new Stack<INode>();
		private int m_lcid = ComUtils.LOCALE_SYSTEM_DEFAULT;
        private List<int> m_localeIds = null;
        private int m_timebias = 0;
		private int m_nextHandle = 1;
        private DateTime m_startTime = DateTime.MinValue;
        private DateTime m_lastUpdateTime = DateTime.MinValue;
        private Thread m_updateThread;
        private NodeIdDictionary<CachedValue> m_cache;
        private Timer m_continuationPointTimer;
        #endregion
	}
                
	#region BrowseFilters Class
    /// <summary>
    /// Defines a set of filters to apply when browsing.
    /// </summary>
    public class BrowseFilters
    {
        /// <summary>
        /// The maximum number of elements to return. Zero means no limit.
        /// </summary>
        public int MaxElementsReturned
        {
	        get { return m_maxElementsReturned;  } 
	        set { m_maxElementsReturned = value; } 
        } 

        /// <summary>
        /// The type of element to return.
        /// </summary>
        public OPCBROWSEFILTER BrowseFilter
        {
	        get { return m_browseFilter;  } 
	        set { m_browseFilter = value; } 
        }

        /// <summary>
        /// An expression used to match the name of the element.
        /// </summary>
        public string ElementNameFilter
        {
	        get { return m_elementNameFilter;  } 
	        set { m_elementNameFilter = value; } 
        }

        /// <summary>
        /// A filter which has semantics that defined by the server.
        /// </summary>
        public string VendorFilter
        {
	        get { return m_vendorFilter;  } 
	        set { m_vendorFilter = value; } 
        }

        /// <summary>
        /// Whether all supported properties to return with each element.
        /// </summary>
        public bool ReturnAllProperties
        {
	        get { return m_returnAllProperties;  } 
	        set { m_returnAllProperties = value; } 
        }

        /// <summary>
        /// A list of names of the properties to return with each element.
        /// </summary>
        public int[] PropertyIds
        {
	        get { return m_propertyIds;  } 
	        set { m_propertyIds = value; } 
        }

        /// <summary>
        /// Whether property values should be returned with the properties.
        /// </summary>
        public bool ReturnPropertyValues
        {
	        get { return m_returnPropertyValues;  } 
	        set { m_returnPropertyValues = value; } 
        }

        #region Private Members
        private int m_maxElementsReturned = 0; 
        private OPCBROWSEFILTER m_browseFilter = OPCBROWSEFILTER.OPC_BROWSE_FILTER_ALL;
        private string m_elementNameFilter = null; 
        private string m_vendorFilter = null; 
        private bool m_returnAllProperties = false; 
        private int[] m_propertyIds = null;
        private bool m_returnPropertyValues = false; 
        #endregion
    }
	#endregion
    
    #region ItemProperty Class
    /// <summary>
    /// Contains a description of a single item property.
    /// </summary>
    public class ItemProperty
    {
	    /// <summary>
	    /// The property identifier.
	    /// </summary>
	    public int Id
	    {
		    get { return m_id;  } 
		    set { m_id = value; } 
	    }

	    /// <summary>
	    /// A short description of the property.
	    /// </summary>
	    public string Description
	    {
		    get { return m_description;  } 
		    set { m_description = value; } 
	    }

	    /// <summary>
	    /// The data type of the property.
	    /// </summary>
	    public short DataType
	    {
		    get { return m_datatype;  } 
		    set { m_datatype = value; } 
	    }

	    /// <summary>
	    /// The value of the property.
	    /// </summary>
	    public object Value
	    {
		    get { return m_value;  } 
		    set { m_value = value; } 
	    }

	    /// <summary>
	    /// The primary identifier for the property if it is directly accessible as an item.
	    /// </summary>
	    public string ItemId
	    {
		    get { return m_itemId;  } 
		    set { m_itemId = value; } 
	    }

	    /// <summary>
	    /// The error id for the result of an operation on an property.
	    /// </summary>
	    public int ErrorId 
	    {
		    get { return m_errorId;  }
		    set { m_errorId = value; }
	    }	

	    #region Private Members
	    private int m_id;
	    private string m_description = null;
	    private short m_datatype = 0;
	    private object m_value = null; 
	    private string m_itemId = null;
	    private int m_errorId = 0;
	    #endregion
    }
	#endregion
    
    #region BrowseElement Class
    /// <summary>
    /// Contains a description of an element in the server address space.
    /// </summary>
    [Serializable]
    public class BrowseElement
    {
        /// <summary>
        /// The node associated withe the element.
        /// </summary>
        public INode Node
        {
            get { return m_node;  }  
            set { m_node = value; }
        }

	    /// <summary>
	    /// A descriptive name for element that is unique within a branch.
	    /// </summary>
	    public string Name
	    {
		    get { return m_name;  } 
		    set { m_name = value; } 
	    }

	    /// <summary>
	    /// The primary identifier for the element within the server namespace.
	    /// </summary>
	    public string ItemId
	    {
		    get { return m_itemId;  } 
		    set { m_itemId = value; } 
	    }

	    /// <summary>
	    /// Whether the element refers to an item with data that can be accessed.
	    /// </summary>
	    public bool IsItem
	    {
		    get { return m_isItem;  } 
		    set { m_isItem = value; } 
	    }

	    /// <summary>
	    /// Whether the element has children.
	    /// </summary>
	    public bool HasChildren
	    {
		    get { return m_hasChildren;  } 
		    set { m_hasChildren = value; } 
	    }

	    /// <summary>
	    /// The set of properties for the element.
	    /// </summary>
	    public IList<ItemProperty> Properties
	    {
		    get { return m_properties;  } 
		    set { m_properties = value; } 
	    }

	    #region Private Members
        private INode m_node = null;
	    private string m_name = null;
	    private string m_itemId = null;
	    private bool m_isItem = false;
	    private bool m_hasChildren = false; 
	    private IList<ItemProperty> m_properties = null;
	    #endregion
    };		
    #endregion;
}

/// <summary>
/// Stores the last value received for an item.
/// </summary>
public class CachedValue
{
    #region Constructors
    /// <summary>
    /// Constructs the object with a NodeId.
    /// </summary>
    public CachedValue(NodeId nodeId)
    {
        m_nodeId = nodeId;
    }
    #endregion
    
    #region Public Members
    /// <summary>
    /// The NodeId for the cached value.
    /// </summary>
    public NodeId NodeId
    {
        get { return m_nodeId; }
    }

    /// <summary>
    /// When the value was placed in the cache.
    /// </summary>
    public DateTime Timestamp
    {
        get { return m_timestamp; }
    }

    /// <summary>
    /// The value in the cache.
    /// </summary>
    public DataValue LastValue
    {
        get { return m_lastValue; }
    }

    /// <summary>
    /// Adds a reference to the item.
    /// </summary>
    public int AddRef()
    {
        return ++m_refs;
    }

    /// <summary>
    /// Releases the item.
    /// </summary>
    public int Release()
    {
        return --m_refs;
    }

    /// <summary>
    /// Updates the value in the cache.
    /// </summary>
    public void Update(DataValue value)
    {
        if (value != null)
        {
            // don't replace entries with old data.
            if (m_lastValue != null && m_lastValue.SourceTimestamp > value.SourceTimestamp)
            {
                return;
            }
       
            m_lastValue = value;
            m_timestamp = DateTime.UtcNow;
        }
    }
    #endregion

    #region Private Fields
    private NodeId m_nodeId;
    private DateTime m_timestamp;
    private DataValue m_lastValue;
    private int m_refs;
    #endregion
}

namespace Opc.Ua.Com
{
    /// <summary>
    /// A class declared in the default namespace for the assembly. Necessary to allow the server to be marked as COM visible.
    /// </summary>
	[ComVisible(true)]
	[Guid("25501B7C-2E39-4fd8-BA5A-FAB081375498")]
	[ProgId("OpcUa.DaProxy")]
	public class DaProxy : Opc.Ua.Com.Server.Da.Server
    {
    }
}
