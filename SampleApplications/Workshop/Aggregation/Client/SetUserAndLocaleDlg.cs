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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace AggregationClient
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SetUserAndLocaleDlg: Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetUserAndLocaleDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Prompts the user to specify the user name and locale.
        /// </summary>
        public bool ShowDialog(Session session)
        {
            m_session = session;

            #region Task #D3 - Change Locale and User Identity
            UpdateUserIdentity(session);
            UpdateLocale(session);
            #endregion
            
            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Task #D3 - Change Locale and User Identity
        /// <summary>
        /// Updates the local displayed in the control.
        /// </summary>
        private void UpdateUserIdentity(Session session)
        {
            UserNameTB.Text = null;
            PasswordTB.Text = null;

            // get the current identity.
            IUserIdentity identity = session.Identity;

            if (identity != null && identity.TokenType == UserTokenType.UserName)
            {
                UserNameIdentityToken token = identity.GetIdentityToken() as UserNameIdentityToken;

                if (token != null)
                {
                    UserNameTB.Text = token.UserName;
                    PasswordTB.Text = token.DecryptedPassword;
                }
            }
        }

        /// <summary>
        /// Updates the local displayed in the control.
        /// </summary>
        private void UpdateLocale(Session session)
        {
            LocaleCB.Items.Clear();

            // get the locales from the server.
            DataValue value = m_session.ReadValue(VariableIds.Server_ServerCapabilities_LocaleIdArray);

            if (value != null)
            {
                string[] availableLocales = value.GetValue<string[]>(null);

                if (availableLocales != null)
                {
                    for (int ii = 0; ii < availableLocales.Length; ii++)
                    {
                        LocaleCB.Items.Add(availableLocales[ii]);
                    }
                }
            }

            // select the default locale.
            if (LocaleCB.Items.Count > 0)
            {
                LocaleCB.SelectedIndex = 0;
            }

            // select the cutrren locale for the session.
            if (session.PreferredLocales != null)
            {
                for (int ii = 0; ii < session.PreferredLocales.Count; ii++)
                {
                    int index = LocaleCB.FindStringExact(session.PreferredLocales[ii]);

                    if (index >= 0)
                    {
                        LocaleCB.SelectedIndex = index;
                        break;
                    }
                }
            }
        }
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                #region Task #D3 - Change Locale and User Identity
                UserIdentity identity = null;

                // use the anonymous identity of the user name is not provided.
                if (String.IsNullOrEmpty(UserNameTB.Text))
                {
                    identity = new UserIdentity();
                }

                // could add check for domain name in user name and use a kerberos token instead.
                else
                {
                    identity = new UserIdentity(UserNameTB.Text, PasswordTB.Text);
                }

                // can specify multiple locales but just use one here to keep the UI simple.
                StringCollection preferredLocales = new StringCollection();
                preferredLocales.Add(LocaleCB.SelectedItem as string);

                // override the default diagnostics to get error messages.
                DiagnosticsMasks returnDiagnostics = m_session.ReturnDiagnostics;

                try
                {
                    // update the session.
                    m_session.ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicIdAndText;
                    m_session.UpdateSession(identity, preferredLocales);
                }
                finally
                {
                    m_session.ReturnDiagnostics = returnDiagnostics;
                }
                #endregion

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
