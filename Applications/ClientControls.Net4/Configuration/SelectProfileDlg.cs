/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Drawing;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Data;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class SelectProfileDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SelectProfileDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit an array value.
        /// </summary>
        public Opc.Ua.Security.ListOfSecurityProfiles ShowDialog(Opc.Ua.Security.ListOfSecurityProfiles profiles, string caption)
        {
            if (caption != null)
            {
                this.Text = caption;
            }

            ProfilesLV.Items.Clear();

            if (profiles != null)
            {
                for (int ii = 0; ii < profiles.Count; ii++)
                {
                    ProfilesLV.Items.Add(profiles[ii].ProfileUri, profiles[ii].Enabled);
                }
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            Opc.Ua.Security.ListOfSecurityProfiles results = new Opc.Ua.Security.ListOfSecurityProfiles();

            for (int ii = 0; ii < ProfilesLV.Items.Count; ii++)
            {
                Opc.Ua.Security.SecurityProfile profile = new Opc.Ua.Security.SecurityProfile();
                profile.ProfileUri = ProfilesLV.Items[ii] as string;
                profile.Enabled = ProfilesLV.CheckedIndices.Contains(ii);
                results.Add(profile);
            }

            return results;
        }
        #endregion
    }
}
