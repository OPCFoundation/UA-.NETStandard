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
using System.Threading;
using System.IdentityModel.Selectors;
using System.IdentityModel.Tokens;
using System.Security.Principal;
using OpcRcw.Comn;
using OpcRcw.Da;
using OpcRcw.Security;
using Opc.Ua;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Provides access to a COM server.
    /// </summary>
    public class ComClient : ComObject
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with the ProgID of the server being accessed.
        /// </summary>
        public ComClient(ComClientConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            m_url = configuration.ServerUrl;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// A key that combines the user name and locale id.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The locale id associated with the instance.
        /// </summary>
        public int LocaleId { get; set; }

        /// <summary>
        /// The user identity associated with the instance.
        /// </summary>
        public IUserIdentity UserIdentity { get; set; }

        /// <summary>
        /// Creates an instance of the COM server.
        /// </summary>
        public void CreateInstance()
        {        
            // multiple calls are not allowed - may block for a while due to network operation.
            lock (m_lock)
            {
                ServerFactory factory = new ServerFactory();
                
                try
                {
                    // create the server.
                    Unknown = factory.CreateServer(new Uri(m_url), null);

                    // set the locale.
                    SetLocale(LocaleId);

                    if (UserIdentity != null)
                    {
                        SetUserIdentity(UserIdentity);
                    }

                    // do any post-connect processing.
                    OnConnected();
                }
                catch (Exception e)
                {
                    ComUtils.TraceComError(e, "Could not connect to server ({0}).", m_url);

                    // cleanup on error.
                    Close();
                }
                finally
                {
                    factory.Dispose();
                }
            }
        }

        /// <summary>
        /// Fetches the error string from the server.
        /// </summary>
        public string GetErrorString(int error)
        {
            string methodName = "IOPCCommon.GetErrorString";

            try
            {
                IOPCCommon server = BeginComCall<IOPCCommon>(methodName, true);
                string ppString = null;
                server.GetErrorString(error, out ppString);
                return ppString;
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Sets the current locale.
        /// </summary>
        public void SetLocale(int localeId)
        {
            string methodName = "IOPCCommon.SetLocaleID";

            try
            {
                IOPCCommon server = BeginComCall<IOPCCommon>(methodName, true);
                server.SetLocaleID(localeId);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Sets the current user identity.
        /// </summary>
        public void SetUserIdentity(IUserIdentity identity)
        {
            string methodName = "IOPCSecurityPrivate.Logon";

            try
            {
                IOPCSecurityPrivate server = BeginComCall<IOPCSecurityPrivate>(methodName, true);

                if (server != null)
                {
                    int bAvailable = 0;
                    server.IsAvailablePriv(out bAvailable);

                    if (bAvailable != 0)
                    {
                        bool logoff = true;

                        if (identity != null && identity.TokenType == UserTokenType.UserName)
                        {
                            UserNameIdentityToken identityToken = identity.GetIdentityToken() as UserNameIdentityToken;

                            if (identityToken != null)
                            {
                                server.Logon(identityToken.UserName, identityToken.Password.ToString());
                                logoff = false;
                            }
                        }

                        if (logoff)
                        {
                            server.Logoff();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Fetches the available locales.
        /// </summary>
        public int[] QueryAvailableLocales()
        {
            string methodName = "IOPCCommon.QueryAvailableLocales";

            try
            {
                IOPCCommon server = BeginComCall<IOPCCommon>(methodName, true);

                // query for available locales.
                int count = 0;
                IntPtr pLocaleIDs = IntPtr.Zero;

                server.QueryAvailableLocaleIDs(out count, out pLocaleIDs);

                // unmarshal results.
                return ComUtils.GetInt32s(ref pLocaleIDs, count, true);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Selects the best matching locale id.
        /// </summary>
        public static int SelectLocaleId(IList<int> availableLocaleIds, IList<string> preferredLocales)
        {
            // choose system default if no available locale ids.
            if (availableLocaleIds == null || availableLocaleIds.Count == 0)
            {
                return ComUtils.LOCALE_SYSTEM_DEFAULT;
            }

            // choose system default if no preferred locales.
            if (preferredLocales == null || preferredLocales.Count == 0)
            {
                return availableLocaleIds[0];
            }

            // look for an exact match.
            for (int ii = 0; ii < preferredLocales.Count; ii++)
            {
                for (int jj = 0; jj < availableLocaleIds.Count; jj++)
                {
                    if (ComUtils.CompareLocales(availableLocaleIds[jj], preferredLocales[ii], false))
                    {
                        return availableLocaleIds[jj];
                    }
                }
            }

            // look for a match on the language only.
            for (int ii = 0; ii < preferredLocales.Count; ii++)
            {
                for (int jj = 0; jj < availableLocaleIds.Count; jj++)
                {
                    if (ComUtils.CompareLocales(availableLocaleIds[jj], preferredLocales[ii], true))
                    {
                        return availableLocaleIds[jj];
                    }
                }
            }

            // return the first avialable locale.
            return availableLocaleIds[0];
        }   
        
        /// <summary>
        /// Gracefully closes the connection to the server.
        /// </summary>
        public void Close()
        {
            Dispose();
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Called immediately after connecting to the server.
        /// </summary>
        protected virtual void OnConnected()
        {
        }
        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private string m_url;
        #endregion
    }
}
