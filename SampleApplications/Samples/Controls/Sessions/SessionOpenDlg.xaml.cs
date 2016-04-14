/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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

using System;
using System.Collections.Generic;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml;
using Windows.UI.Core;

namespace Opc.Ua.Sample.Controls
{
    public partial class SessionOpenDlg : Page
    {
        #region Constructors
        public SessionOpenDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Popup dialogPopup = new Popup();
        private Session m_session;
        private const string m_BrowseCertificates = "<Browse...>";
        private static long m_Counter = 0;
        private IList<string> m_preferredLocales;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<Session> ShowDialog(Session session, IList<string> preferredLocales)
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

            if (String.IsNullOrEmpty(session.SessionName))
            {
                SessionNameTB.Text = Utils.Format("MySession {0}", Utils.IncrementIdentifier(ref m_Counter));
            }
            else
            {
                SessionNameTB.Text = session.SessionName;
            }

            if (session.Identity != null)
            {
                UserIdentityTypeCB.SelectedItem = session.Identity.TokenType;
            }

            // display dialog
            dialogPopup.Child = this;
            dialogPopup.IsOpen = true;

            CancelBTN.IsEnabled = true;
            OkBTN.IsEnabled = true;

            TaskCompletionSource<Session> tcs = new TaskCompletionSource<Session>();
            // display dialog and wait for close event
            dialogPopup.Child = this;
            dialogPopup.IsOpen = true;
            dialogPopup.Closed += (o, e) =>
            {
                tcs.SetResult(m_session);
            };

            return await tcs.Task;
        }
        #endregion
        private void UserIdentityTypeCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                UserTokenType tokenType = (UserTokenType)UserIdentityTypeCB.SelectedItem;

                UserNameCB.Items.Clear();
                
                UserNameCB.IsEnabled = true;
                PasswordTB.IsEnabled = true;

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
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void OkBTN_Click(object sender, RoutedEventArgs e)
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
                        username = UserNameCB.PlaceholderText;
                    }

                    if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(PasswordTB.Password))
                    {
                        identity = new UserIdentity(username, PasswordTB.Password);
                    }
                }

                CancelBTN.IsEnabled = false;
                OkBTN.IsEnabled = false;
                object state = new object[] { m_session, SessionNameTB.Text, identity, m_preferredLocales };
                await Task.Run(() => m_session = Open(state));
                dialogPopup.IsOpen = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        private void CancelBTN_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                m_session = null;
                dialogPopup.IsOpen = false;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        /// <summary>
        /// Asynchronously open the session.
        /// </summary>
        private Session Open(object state)
        {
            try
            {
                Session session = ((object[])state)[0] as Session;
                string sessionName = ((object[])state)[1] as string;
                IUserIdentity identity = ((object[])state)[2] as IUserIdentity;
                IList<string> preferredLocales = ((object[])state)[3] as IList<string>;

                // open the session.
                session.Open(sessionName, (uint)session.SessionTimeout, identity, preferredLocales);

                return session;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), (Exception)exception);
                return null;
            }
        }


    }
}
