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
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditComplexValueDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditComplexValueDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            for (BuiltInType ii = BuiltInType.Boolean; ii <= BuiltInType.StatusCode; ii++)
            {
                SetTypeCB.Items.Add(ii);
            }

            SetTypeCB.SelectedItem = BuiltInType.String;
        }
        #endregion
      
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to view or edit the value.
        /// </summary>
        public object ShowDialog(
            Session session, 
            NodeId nodeId,
            uint attributeId,
            string name, 
            object value, 
            bool readOnly,
            string caption)
        {
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = !readOnly;

            ValueCTRL.ChangeSession(session);
            ValueCTRL.ShowValue(nodeId, attributeId, name, value, readOnly);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return ValueCTRL.GetValue();
        }
        
        /// <summary>
        /// Prompts the user to edit the value.
        /// </summary>
        public object ShowDialog(
            Session session,
            string name,
            NodeId dataType,
            int valueRank,
            object value,
            string caption)
        {
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = true;

            ValueCTRL.ChangeSession(session);
            ValueCTRL.ShowValue(name, dataType, valueRank, value);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return ValueCTRL.GetValue();
        }

        /// <summary>
        /// Prompts the user to edit the value.
        /// </summary>
        public object ShowDialog(
            TypeInfo expectedType,
            string name,
            object value,
            string caption)
        {
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            OkBTN.Visible = true;

            ValueCTRL.ChangeSession(null);
            ValueCTRL.ShowValue(expectedType, name, value);

            if (base.ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return ValueCTRL.GetValue();
        }

        /// <summary>
        /// Changes the session used.
        /// </summary>
        public void ChangeSession(Session session)
        {
            ValueCTRL.ChangeSession(session);
        }

        /// <summary>
        /// Updates the value shown in the control.
        /// </summary>
        public void UpdateValue(
            NodeId nodeId,
            uint attributeId,
            string name,
            object value)
        {
            ValueCTRL.ShowValue(nodeId, attributeId, name, value, true);
        }
        #endregion

        #region Event Handlers
        private void ValueCTRL_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                BackBTN.Visible = ValueCTRL.CanGoBack;
                SetTypeCB.Visible = ValueCTRL.CanChangeType;
                SetTypeCB.SelectedItem = ValueCTRL.CurrentType;
                SetArraySizeBTN.Visible = ValueCTRL.CanSetArraySize;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void BackBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.Back();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.EndEdit();
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void SetTypeBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.SetArraySize();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void SetTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ValueCTRL.SetType((BuiltInType)SetTypeCB.SelectedItem);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
