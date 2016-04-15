/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
