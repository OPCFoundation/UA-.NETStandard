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

using Opc.Ua.Client;
using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Sample.Controls
{
    public partial class SubscriptionEditDlg : Page
    {
        private bool dialogResult = true;
        public SubscriptionEditDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public async Task<bool> ShowDialog(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            DisplayNameTB.Text = subscription.DisplayName;
            PublishingIntervalNC.Value = (double)Convert.ToDecimal(subscription.PublishingInterval);
            KeepAliveCountNC.Value = subscription.KeepAliveCount;
            LifetimeCountCTRL.Value = subscription.LifetimeCount;
            MaxNotificationsCTRL.Value = subscription.MaxNotificationsPerPublish;
            PriorityNC.Value = subscription.Priority;
            PublishingEnabledCK.IsChecked = subscription.PublishingEnabled;

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Popup dialog = new Popup();
            dialog.Child = this;
            dialog.IsOpen = true;
            dialog.Closed += (o, e) =>
            {
                tcs.SetResult(dialogResult);
            };

            bool result = await tcs.Task;

            subscription.DisplayName = DisplayNameTB.Text;
            subscription.PublishingInterval = (int)PublishingIntervalNC.Value;
            subscription.KeepAliveCount = (uint)KeepAliveCountNC.Value;
            subscription.LifetimeCount = (uint)LifetimeCountCTRL.Value;
            subscription.MaxNotificationsPerPublish = (uint)MaxNotificationsCTRL.Value;
            subscription.Priority = (byte)PriorityNC.Value;
            subscription.PublishingEnabled = (bool)PublishingEnabledCK.IsChecked;

            return result;
        }
    }
}
