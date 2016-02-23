/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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

