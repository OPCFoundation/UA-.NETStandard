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

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditSubscriptionDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditSubscriptionDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion
      
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the monitored item.
        /// </summary>
        public bool ShowDialog(Subscription subscription)
        {
            PublishingIntervalUP.Value = subscription.PublishingInterval;
            KeepAliveCountUP.Value = subscription.KeepAliveCount;
            LifetimeCountUP.Value = subscription.LifetimeCount;
            MaxNotificationsPerPublishUP.Value = subscription.MaxNotificationsPerPublish;
            PriorityTB.Value = subscription.Priority;
            PublishingEnabledCK.Checked = subscription.PublishingEnabled;

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            subscription.PublishingInterval = (int)PublishingIntervalUP.Value;
            subscription.KeepAliveCount = (uint)KeepAliveCountUP.Value;
            subscription.LifetimeCount = (uint)LifetimeCountUP.Value;
            subscription.MaxNotificationsPerPublish = (uint)MaxNotificationsPerPublishUP.Value;
            subscription.Priority = (byte)PriorityTB.Value;
            subscription.PublishingEnabled  = PublishingEnabledCK.Checked;

            return true;
        }
        #endregion
        
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
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
