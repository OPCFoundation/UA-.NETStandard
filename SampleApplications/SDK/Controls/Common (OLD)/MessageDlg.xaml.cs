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


using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls.Primitives;
using Opc.Ua.Configuration;
using System;

namespace Opc.Ua.Client.Controls
{
    public class ApplicationMessageDlg:IApplicationMessageDlg
    {
        private MessageDlg dialog = new MessageDlg("");
        public override void Message(string text, bool ask)
        {
            if (ask)
            {
                dialog.MessageText(text, MessageDlgButton.Yes, MessageDlgButton.No);
            }
            else
            {
                dialog.MessageText(text);
            }
        }
        public override async Task<bool> ShowAsync()
        {
            return await dialog.ShowAsync() == MessageDlgButton.Yes;
        }
    }

    public enum MessageDlgButton
    {
        None,
        Ok,
        Cancel,
        Yes,
        No
    }

    public partial class MessageDlg : Page
    {
        private string Text;
        private Popup dialogPopup = new Popup();
        TaskCompletionSource<MessageDlgButton> tcs;
        MessageDlgButton Result = MessageDlgButton.None;
        MessageDlgButton Left = MessageDlgButton.None;
        MessageDlgButton Right = MessageDlgButton.None;

        #region Constructors
        public MessageDlg(
            string text, 
            MessageDlgButton left = MessageDlgButton.Ok, 
            MessageDlgButton right = MessageDlgButton.None)
        {
            InitializeComponent();
            Text = text;
            Left = left;
            Right = right;
            dialogPopup.Child = this;
            dialogPopup.Closed += ClosedEvent;
        }
        #endregion

        public void MessageText(
            string text,
            MessageDlgButton left = MessageDlgButton.Ok,
            MessageDlgButton right = MessageDlgButton.None)
        {
            Text = text;
            Left = left;
            Right = right;
        }


        public async Task<MessageDlgButton> ShowAsync()
        {
            // configure dialog
            SetButtonText(LeftButton, Left);
            SetButtonText(RightButton, Right);
            Message.Text = Text;

            tcs = new TaskCompletionSource<MessageDlgButton>();

            // display dialog and wait for close event
            dialogPopup.IsOpen = true;
            return await tcs.Task;
        }

        private void ClosedEvent( object o, object e)
        { 
            tcs.SetResult(Result);
        }

        private void SetButtonText(Button button, MessageDlgButton item)
        {
            button.Content = item.ToString();
            button.Visibility = (item == MessageDlgButton.None) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LeftButton_Click(object sender, RoutedEventArgs e)
        {
            Result = Left;
            dialogPopup.IsOpen = false;
        }

        private void RightButton_Click(object sender, RoutedEventArgs e)
        {
            Result = Right;
            dialogPopup.IsOpen = false;
        }
    }

}

