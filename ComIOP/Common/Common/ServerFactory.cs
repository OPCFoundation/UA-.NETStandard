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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

using OpcRcw.Comn;

namespace Opc.Ua.Com
{
	/// <summary>
	/// A unique identifier for the result of an operation of an item.
	/// </summary>
	public class ServerFactory : IDisposable
	{
		#region Constructors
		/// <summary>
		/// Initializes an empty instance.
		/// </summary>
		public ServerFactory()
		{
			Initialize();
		}

		/// <summary>
		/// Sets private members to default values.
		/// </summary>
		private void Initialize()
		{
			m_server = null;
			m_host   = null;
		}
		#endregion
        
        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ServerFactory() 
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
            if (m_server != null)
            {
                ComUtils.ReleaseServer(m_server);
                m_server = null;
            }
        }
        #endregion

		#region Public Methods
		/// <summary>
		/// Connects to OPCEnum on the local machine.
		/// </summary>
		public void Connect()
		{
			Connect(null, null);
		}

		/// <summary>
		/// Connects to OPCEnum on the specified machine.
		/// </summary>
		public void Connect(string host, UserIdentity identity)
		{
            // disconnect from current server.
            Disconnect();

            // create in the instance.
            object unknown = null;
			
            try
			{                
				unknown = ComUtils.CreateInstance(OPCEnumCLSID, host, identity);
			}
			catch (Exception e)
			{
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not connect to OPCEnum server.");
			}

            m_server = unknown as IOPCServerList2;

            if (m_server == null)
            {
                ComUtils.ReleaseServer(unknown);

                StringBuilder error = new StringBuilder();

                error.Append("Server does not support IOPCServerList2. ");
                error.Append("The OPC proxy/stubs may not be installed properly or the client or server machine. ");
                error.Append("The also could be a problem with DCOM security configuration.");
                
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, error.ToString());
            }

			m_host = host;

            if (String.IsNullOrEmpty(m_host))
            {
                m_host = "localhost";
            }
		}

        /// <summary>
        /// Releases the active server.
        /// </summary>
        public void Disconnect()
        {
			try
			{
                if (m_server != null)
                {
                    ComUtils.ReleaseServer(m_server);
                    m_server = null;
                }
			}
			catch (Exception e)
			{
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not release OPCEnum server.");
			}
        }

		/// <summary>
		/// Enumerates hosts that may be accessed for server discovery.
		/// </summary>
		public string[] EnumerateHosts()
		{
			return ComUtils.EnumComputers();
		}

        /// <summary>
        /// Parses the URL and fetches the information about the server.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>The server description.</returns>
        public ComServerDescription ParseUrl(Uri uri)
        {
            // parse path to find prog id and clsid.
            string progID = uri.LocalPath;
            string clsid  = null;

            while (progID.StartsWith("/"))
            {
                progID = progID.Substring(1);
            }

            int index = progID.IndexOf('/');

            if (index >= 0)
            {
                clsid  = progID.Substring(index+1);
                progID = progID.Substring(0, index);
            }

            // look up prog id if clsid not specified in the uri.
            Guid guid = Guid.Empty;

            if (String.IsNullOrEmpty(clsid))
            {
                // use OpcEnum to lookup the prog id.
                guid = CLSIDFromProgID(progID);

                // check if prog id is actually a clsid string.
                if (guid == Guid.Empty)
                {
                    clsid = progID;
                }
            }

            // convert CLSID to a GUID.
            if (guid == Guid.Empty)
            {
                try
                {
                    guid = new Guid(clsid);
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "COM server URI does not contain a valid CLSID or ProgID.");
                }
            }

            ComServerDescription server = new ComServerDescription();

            server.Url = uri.ToString();
            server.Clsid = guid;
            server.ProgId = progID;
            server.VersionIndependentProgId = server.ProgId;
            server.Description = progID;

			try
			{
				// fetch class details from the enumerator.
				string description  = null;
				string verIndProgID = null;

				m_server.GetClassDetails(
					ref guid, 
					out progID, 
					out description, 
					out verIndProgID);                
				
				// use version independent prog id if available.
				if (!String.IsNullOrEmpty(verIndProgID))
				{
                    progID = verIndProgID;
                }

                server.Url = uri.ToString();
                server.Clsid = guid;
                server.ProgId = progID;
                server.VersionIndependentProgId = verIndProgID;
                server.Description = description;
			}
			catch
			{
                server.Url = uri.ToString();
                server.Clsid = guid;
                server.ProgId = progID;
                server.VersionIndependentProgId = server.ProgId;
                server.Description = guid.ToString();
			}

			// return the server uri.
            return server;
        }

		/// <summary>
		/// Returns a list of servers that support the specified specification.
		/// </summary>
		public Uri[] GetAvailableServers(params Specification[] specifications)
		{
			// enumerate servers on specified machine.
			try
			{                				
				// convert the interface version to a guid.
                Guid[] catids = new Guid[specifications.Length];

                for (int ii = 0; ii < catids.Length; ii++)
                {
                    catids[ii] = new Guid(specifications[ii].Id);
                }
		
				// get list of servers in the specified specification.
				IOPCEnumGUID enumerator = null;

				m_server.EnumClassesOfCategories(
                    catids.Length,
                    catids,
					0,
					null,
					out enumerator);

				// read clsids.
				List<Guid> clsids = ReadClasses(enumerator);

				// release enumerator object.					
				ComUtils.ReleaseServer(enumerator);
				enumerator = null;

				// fetch class descriptions.
				Uri[] uris = new Uri[clsids.Count];

				for (int ii = 0; ii < uris.Length; ii++)
				{
					uris[ii] = CreateUri(clsids[ii]);
				}

				return uris;
			}
			catch (Exception e)
			{
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not enumerate COM servers.");
			}
		}
		
		/// <summary>
		/// Looks up the CLSID for the specified prog id on a remote host.
		/// </summary>
		public Guid CLSIDFromProgID(string progID)
		{
			// lookup prog id.
			Guid clsid;

			try
			{
				m_server.CLSIDFromProgID(progID, out clsid);
			}
			catch
			{
				clsid = Guid.Empty;
			}

			// return empty guid if prog id not found.
			return clsid;
		}

		/// <summary>
		/// Connects to the specified COM server server.
		/// </summary>
		public object CreateServer(Uri uri, UserIdentity identity)
		{
			// parse path to find prog id and clsid.
			string progID = uri.LocalPath;
			string clsid  = null;

            while (progID.StartsWith("/"))
            {
                progID = progID.Substring(1);
            }

			int index = progID.IndexOf('/');

			if (index >= 0)
			{
				clsid  = progID.Substring(index+1);
				progID = progID.Substring(0, index);
			}

			// look up prog id if clsid not specified in the uri.
			Guid guid = Guid.Empty;

			if (String.IsNullOrEmpty(clsid))
			{
				// connect to enumerator.
				Connect(uri.Host, identity);

				// use OpcEnum to lookup the prog id.
				guid = CLSIDFromProgID(progID);

				// check if prog id is actually a clsid string.
				if (guid == Guid.Empty)
				{
                    clsid = progID;
				}
			}

            // convert CLSID to a GUID.
            if (guid == Guid.Empty)
            {
			    try 
			    { 
				    guid = new Guid(clsid); 
			    }
			    catch (Exception e)
			    {
                    throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "COM server URI does not contain a valid CLSID or ProgID.");
			    }
            }
                       
            // use normal activation.
            return ComUtils.CreateInstance(guid, uri.Host, identity);

            #if COM_IMPERSONATION_SUPPORT
            // set the current thread token.
            IPrincipal existingPrincipal = Thread.CurrentPrincipal;
            WindowsPrincipal principal = ComUtils.GetPrincipalFromUserIdentity(identity);
            
            try
            {
                if (principal != null)
                {
                    Thread.CurrentPrincipal = principal;
                }

                // activate with a license key if one provided.
                if (identity != null && !String.IsNullOrEmpty(identity.LicenseKey))
                {
                    return ComUtils.CreateInstanceWithLicenseKey(guid, uri.Host, identity, identity.LicenseKey);
                }

                // use normal activation.
                return ComUtils.CreateInstance(guid, uri.Host, identity);
            }
            finally
            {
                Thread.CurrentPrincipal = existingPrincipal;
            }
            #endif
		}
		#endregion

		#region Private Methods
		/// <summary>
		/// Reads the guids from the enumerator.
		/// </summary>
		private List<Guid> ReadClasses(IOPCEnumGUID enumerator)
		{
			List<Guid> guids = new List<Guid>();

			int fetched = 0;
			Guid[] buffer = new Guid[10];

			do
			{
				try
				{ 
                    IntPtr pGuids = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(Guid))*buffer.Length);

                    try
                    {
                        enumerator.Next(buffer.Length, pGuids, out fetched);

                        if (fetched > 0)
                        {
                            IntPtr pos = pGuids;

                            for (int ii = 0; ii < fetched; ii++)
                            {
                                buffer[ii] = (Guid)Marshal.PtrToStructure(pos, typeof(Guid));
                                pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(Guid)));
                                guids.Add(buffer[ii]);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pGuids);
                    }
				}
				catch
				{
					break;
				}
			}
			while (fetched > 0);

			return guids;
		}

		/// <summary>
		/// Reads the server details from the enumerator.
		/// </summary>
		private Uri CreateUri(Guid clsid)
		{
			// initialize the server uri.
			StringBuilder uri = new StringBuilder();

			// append scheme and host.
            uri.Append("opc.com://");
            uri.Append(m_host);
            
		    string progID = null;

			try
			{
				// fetch class details from the enumerator.
				string description  = null;
				string verIndProgID = null;

				m_server.GetClassDetails(
					ref clsid, 
					out progID, 
					out description, 
					out verIndProgID);                
				
				// use version independent prog id if available.
				if (!String.IsNullOrEmpty(verIndProgID))
				{
                    progID = verIndProgID;
                }				
			}
			catch
			{
                // cannot get prog id.
                progID = null;
			}
                    
			// append prog id.
			if (!String.IsNullOrEmpty(progID))
			{
                uri.AppendFormat("/{0}", progID);
			}

			// append prog id.
            uri.AppendFormat("/{0}", clsid);

			// return the server uri.
			return new Uri(uri.ToString());
		}
		#endregion

		#region Private Members
		private IOPCServerList2 m_server = null;
		private string m_host = null;
		#endregion

		#region Private Constants
		private static readonly Guid OPCEnumCLSID = new Guid("13486D51-4821-11D2-A494-3CB306C10000");
		#endregion
	}

    /// <summary>
	/// The user identity to use when connecting to the COM server.
	/// </summary>
	public class UserIdentity
	{
		#region Constructors
		/// <summary>
		/// Sets the username and password (extracts the domain from the username if a '\' is present).
		/// </summary>
		public UserIdentity(string username, string password)
		{
			m_username = username;
			m_password = password;

			if (!String.IsNullOrEmpty(m_username))
			{
                int index = m_username.IndexOf('\\');
                
                if (index != -1)
                {
                    m_domain   = m_username.Substring(0, index);
                    m_username = m_username.Substring(index+1);
                }
			}
		}
		#endregion
        
		#region Static Methods
		/// <summary>
		/// Whether the identity represents an the default identity.
		/// </summary>
		public static bool IsDefault(UserIdentity identity)
		{
            if (identity != null)
            {
			    return String.IsNullOrEmpty(identity.m_username);
            }

            return true;
		}
		#endregion

		#region Public Properties
		/// <summary>
		/// The windows domain name.
		/// </summary>
		public string Domain
		{
			get { return m_domain; }
		}

		/// <summary>
		/// The user name. 
		/// </summary>
		public string Username
		{
			get { return m_username; }
		}

		/// <summary>
		/// The password. 
		/// </summary>
		public string Password
		{
			get { return m_password; }
		}

		/// <summary>
		/// Gets or sets a license key to use when activating the server.
		/// </summary>
		public string LicenseKey
		{
			get { return m_licenseKey;  }
			set { m_licenseKey = value; }
		}
		#endregion

		#region Comparison Operators
		/// <summary>
		/// Determines if the object is equal to the specified value.
		/// </summary>
		public override bool Equals(object target)
		{
            object identity = target as UserIdentity;

            if (identity == null)
            {
                return false;
            }

			return false;
		}

		/// <summary>
		/// Converts the object to a string used for display.
		/// </summary>
		public override string ToString()
		{
            if (!String.IsNullOrEmpty(m_domain))
            {
                return Utils.Format("{0}\\{1}", m_domain, m_username);
            }

			return m_username;
		}
		
		/// <summary>
		/// Returns a suitable hash code for the result.
		/// </summary>
		public override int GetHashCode()
		{
            if (!String.IsNullOrEmpty(m_username))
            {
                return m_username.GetHashCode();
            }

			return 0;
		}

		/// <summary>
		/// Returns true if the objects are equal.
		/// </summary>
		public static bool operator==(UserIdentity a, UserIdentity b) 
		{
            if (Object.ReferenceEquals(a, null))
            {
                return Object.ReferenceEquals(b, null);
            }

			return a.Equals(b);
		}

		/// <summary>
		/// Returns true if the objects are not equal.
		/// </summary>
		public static bool operator!=(UserIdentity a, UserIdentity b) 
		{
            if (Object.ReferenceEquals(a, null))
            {
                return !Object.ReferenceEquals(b, null);
            }

			return !a.Equals(b);
		}
		#endregion

		#region Private Members
		private string m_domain;
		private string m_username;
		private string m_password;
        private string m_licenseKey;
		#endregion
    }

    /// <summary>
    /// Stores the description of a COM server.
    /// </summary>
    public class ComServerDescription
    {
        /// <summary>
        /// The  URL for the server.
        /// </summary>
        public string Url;

        /// <summary>
        /// The programmatic identifier for the server.
        /// </summary>
        public string ProgId;

        /// <summary>
        /// The version independent programmatic identifier for the server.
        /// </summary>
        public string VersionIndependentProgId;

        /// <summary>
        /// The description for thr server.
        /// </summary>
        public string Description;

        /// <summary>
        /// The CLSID for thr server.
        /// </summary>
        public Guid Clsid;
    }
}
