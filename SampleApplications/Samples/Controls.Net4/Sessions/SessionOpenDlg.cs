/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class SessionOpenDlg : Form
    {
        #region Constructors
        public SessionOpenDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private const string m_BrowseCertificates = "<Browse...>";
        private static long m_Counter = 0;
        private IList<string> m_preferredLocales;
        private X509Certificate2 m_clientCertificate;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(Session session, IList<string> preferredLocales)
        {
            if (session == null) throw new ArgumentNullException("session");
            
            m_session = session;
            m_preferredLocales = preferredLocales;
            
            UserIdentityTypeCB.Items.Clear();

            foreach (UserTokenPolicy policy in session.Endpoint.UserIdentityTokens)
            {
                UserIdentityTypeCB.Items.Add(policy.TokenType);
            }

            if (UserIdentityTypeCB.Items.Count == 0)
            {
                UserIdentityTypeCB.Items.Add(UserTokenType.UserName);
            }

            UserIdentityTypeCB.SelectedIndex = 0;

            SessionNameTB.Text = session.SessionName;

            if (String.IsNullOrEmpty(SessionNameTB.Text))
            {
                SessionNameTB.Text = Utils.Format("MySession {0}", Utils.IncrementIdentifier(ref m_Counter));
            }
            
            if (session.Identity != null)
            {
                UserIdentityTypeCB.SelectedItem = session.Identity.TokenType;
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }
            
            return true;
        }
        public bool ShowDialog(Session session, IList<string> preferredLocales, X509Certificate2 clientCertificate)
        {
            m_clientCertificate = clientCertificate;
            return ShowDialog(session, preferredLocales);
        }
            #endregion

            private void UserIdentityTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {            
            try
            {
                UserTokenType tokenType = (UserTokenType)UserIdentityTypeCB.SelectedItem;

                UserNameCB.Items.Clear();
                
                UserNameCB.Enabled = true;
                PasswordTB.Enabled = true;

                // allow use to browse certificate stores.
                if (tokenType == UserTokenType.Certificate)
                {
                    UserNameCB.Items.Add(m_BrowseCertificates);
                    UserNameCB.SelectedIndex = 0;
                }

                // populate list.
                foreach (IUserIdentity identity in m_session.IdentityHistory)
                {
                    if (identity.TokenType == tokenType)
                    {
                        UserNameCB.Items.Add(identity.DisplayName);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                // construct the user identity.
                IUserIdentity identity = null;

                if ((UserTokenType)UserIdentityTypeCB.SelectedItem == UserTokenType.UserName)
                {
                    string username = (string)UserNameCB.SelectedItem;

                    if (String.IsNullOrEmpty(username))
                    {
                        username = UserNameCB.Text;
                    }
                    
                    if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(PasswordTB.Text))
                    {
                        identity = new UserIdentity(username, PasswordTB.Text);
                    }
                }
               else if((UserTokenType)UserIdentityTypeCB.SelectedItem == UserTokenType.Certificate)
                {
                    identity = new UserIdentity(m_clientCertificate);
                }
                Cursor = Cursors.WaitCursor;

                ThreadPool.QueueUserWorkItem(Open, new object[] { m_session, SessionNameTB.Text, identity, m_preferredLocales });
                
                CancelBTN.Enabled = false;
                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }        

        /// <summary>
        /// Reports the results of the open session operation.
        /// </summary>
        private void OpenComplete(object e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new WaitCallback(OpenComplete), e);
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            try
            {
                Cursor = Cursors.Default;

                if (e != null)
                {
                    GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), (Exception)e);
                }

                if (m_session.Connected && m_session.SessionTimeout < 1000)
                {
                    DialogResult result = MessageBox.Show(
                        "Warning: the session time out might be too small: " + m_session.SessionTimeout,
                        "Session revised timeout",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                DialogResult = DialogResult.OK;
            }
            finally
            {
                CancelBTN.Enabled = true;
                OkBTN.Enabled = true;
            }
        }

        /// <summary>
        /// Asynchronously open the session.
        /// </summary>
        private void Open(object state)
        {
            try
            {
                Session session = ((object[])state)[0] as Session;
                string sessionName = ((object[])state)[1] as string;
                IUserIdentity identity = ((object[])state)[2] as IUserIdentity;
                IList<string> preferredLocales = ((object[])state)[3] as IList<string>;

                // open the session.
                session.Open(sessionName, (uint)session.SessionTimeout, identity, preferredLocales);
                 
                OpenComplete(null);
            }
            catch (Exception exception)
            {
                OpenComplete(exception);
            }
        }
    }
}
