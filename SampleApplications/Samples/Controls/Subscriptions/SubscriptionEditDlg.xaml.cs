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
