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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;

namespace Opc.Ua.Sample.Controls
{
    public partial class SubscriptionEditDlg : Form
    {
        public SubscriptionEditDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public bool ShowDialog(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            DisplayNameTB.Text          = subscription.DisplayName;
            PublishingIntervalNC.Value  = subscription.Created ? (decimal)subscription.CurrentPublishingInterval : (decimal)subscription.PublishingInterval;
            KeepAliveCountNC.Value      = subscription.Created ? subscription.CurrentKeepAliveCount : subscription.KeepAliveCount;
            LifetimeCountCTRL.Value     = subscription.Created ? subscription.CurrentLifetimeCount: subscription.LifetimeCount;
            MaxNotificationsCTRL.Value  = subscription.MaxNotificationsPerPublish;
            PriorityNC.Value            = subscription.Created ? subscription.CurrentPriority : subscription.Priority;
            PublishingEnabledCK.Checked = subscription.Created ? subscription.CurrentPublishingEnabled : subscription.PublishingEnabled;
            
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            subscription.DisplayName                = DisplayNameTB.Text;
            subscription.PublishingInterval         = (int)PublishingIntervalNC.Value;
            subscription.KeepAliveCount             = (uint)KeepAliveCountNC.Value;
            subscription.LifetimeCount              = (uint)LifetimeCountCTRL.Value;
            subscription.MaxNotificationsPerPublish = (uint)MaxNotificationsCTRL.Value;
            subscription.Priority                   = (byte)PriorityNC.Value;
            if (subscription.Created)
            {
                subscription.SetPublishingMode(PublishingEnabledCK.Checked);            
            }
            else
            {
                subscription.PublishingEnabled = PublishingEnabledCK.Checked;
            }
            return true;
        }
    }
}
