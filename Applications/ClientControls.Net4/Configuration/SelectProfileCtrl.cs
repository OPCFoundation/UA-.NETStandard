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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control with button that displays edit array dialog.
    /// </summary>
    public partial class SelectProfileCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public SelectProfileCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private event EventHandler m_ProfilesChanged;
        private Opc.Ua.Security.ListOfSecurityProfiles m_profiles;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// The list of available security profiles.
        /// </summary>
        public Opc.Ua.Security.ListOfSecurityProfiles Profiles 
        {
            get 
            { 
                return m_profiles; 
            }

            set
            {
                if (CurrentProfilesControl != null)
                {
                    StringBuilder builder = new StringBuilder();

                    if (value != null)
                    {
                        for (int ii = 0; ii < value.Count; ii++)
                        {
                            if (value[ii].Enabled)
                            {
                                if (builder.Length > 0)
                                {
                                    builder.Append(", ");
                                }

                                builder.Append(SecurityPolicies.GetDisplayName(value[ii].ProfileUri));
                            }
                        }
                    }

                    CurrentProfilesControl.Text = builder.ToString();
                }

                m_profiles = value;

            }
        }
        
        /// <summary>
        /// Gets or sets the control that is stores with the current file path.
        /// </summary>
        public Control CurrentProfilesControl { get; set; }

        /// <summary>
        /// Raised when the profiles are changed.
        /// </summary>
        public event EventHandler ProfilesChanged
        {
            add { m_ProfilesChanged += value; }
            remove { m_ProfilesChanged -= value; }
        }
        #endregion

        #region Event Handlers
        private void BrowseBTN_Click(object sender, EventArgs e)
        {
            if (CurrentProfilesControl == null)
            {
                return;
            }

            Opc.Ua.Security.ListOfSecurityProfiles profiles = new SelectProfileDlg().ShowDialog(Profiles, null);

            if (profiles == null)
            {
                return;
            }

            Profiles = profiles;

            if (m_ProfilesChanged != null)
            {
                m_ProfilesChanged(this, e);
            }
        }
        #endregion
    }
}
