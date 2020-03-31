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

namespace TutorialClient
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class CallMethodDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public CallMethodDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private NodeId m_objectId;
        private NodeId m_methodId;
        private int m_firstOutputArgument;
        #endregion
        
        #region Public Interface
        public void Show(Session session, NodeId objectId, NodeId methodId)
        {
            m_session = session;
            m_objectId = objectId;
            m_methodId = methodId;

            Show();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the list control.
        /// </summary>
        private void UpdateList(Session session, Argument[] arguments, string browseName)
        {
            for (int ii = 0; ii < arguments.Length; ii++)
            {
                Argument argument = arguments[ii];
                Variant defaultValue = new Variant(TypeInfo.GetDefaultValue(argument.DataType, argument.ValueRank));

                ListViewItem item = new ListViewItem(arguments[ii].Name);

                if (browseName == BrowseNames.InputArguments)
                {
                    item.SubItems.Add("IN");
                    m_firstOutputArgument++;
                }
                else
                {
                    item.SubItems.Add("OUT");
                }

                string dataType = session.NodeCache.GetDisplayText(arguments[ii].DataType);

                if (arguments[ii].ValueRank >= 0)
                {
                    dataType += "[]";
                }

                item.SubItems.Add(defaultValue.ToString());
                item.SubItems.Add(dataType);
                item.SubItems.Add(Utils.Format("{0}", arguments[ii].Description));
                item.Tag = defaultValue;

                ArgumentsLV.Items.Add(item);
            }
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CancelBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

    }
}
