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

using Opc.Ua;
using System;
using System.Text;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to provide a user name and password.
    /// </summary>
    public sealed partial class UsernameTokenDlg : Page
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public UsernameTokenDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(UserNameIdentityToken token)
        {
            if (token != null)
            {
                UserNameCB.SelectedItem = token.UserName;

                if (token.Password != null && token.Password.Length > 0)
                {
                    PasswordTB.Password = new UTF8Encoding().GetString(token.Password);
                }
            }

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            token.UserName = UserNameCB.SelectedItem.ToString();

            if (!String.IsNullOrEmpty(PasswordTB.Password))
            {
                token.Password = new UTF8Encoding().GetBytes(PasswordTB.Password);
            }
            else
            {
                token.Password = null;
            }

            return true;
        }
        #endregion
    }
}
