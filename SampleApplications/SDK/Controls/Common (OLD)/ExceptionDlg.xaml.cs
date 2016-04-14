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

using Opc.Ua;
using System;
using System.Text;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog that displays an exception trace in an HTML page.
    /// </summary>
    public sealed partial class ExceptionDlg : Page
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExceptionDlg"/> class.
        /// </summary>
        public ExceptionDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Replaces all special characters in the message.
        /// </summary>
        private string ReplaceSpecialCharacters(string message)
        {
            message = message.Replace("&", "&#38;");
            message = message.Replace("<", "&lt;");
            message = message.Replace(">", "&gt;");
            message = message.Replace("\"", "&#34;");
            message = message.Replace("'", "&#39;");
            message = message.Replace("\r\n", "<br/>");

            return message;
        }

        /// <summary>
        /// Display the exception in the dialog.
        /// </summary>
        public void ShowDialog(string caption, Exception e)
        {
            Text.Text = caption;
   
            StringBuilder buffer = new StringBuilder();

            buffer.Append("<html><body style='margin:0'>");

            while (e != null)
            {
                string message = e.Message;
                
                ServiceResultException exception = e as ServiceResultException;

                if (exception != null)
                {
                    message = exception.ToLongString();
                }
                
                message = ReplaceSpecialCharacters(message);

                if (exception != null)
                {
                    buffer.Append("<p>");
                    buffer.Append("<font style='font:9pt/12pt verdana;color:black'>");
                    buffer.Append(message);
                    buffer.Append("</font>");
                    buffer.Append("</p>");
                }
                else
                {
                    buffer.Append("<font style='font:9pt/12pt verdana;color:red'><b>");
                    buffer.Append(message);
                    buffer.Append("</b></font><br>");
                }

                message = e.StackTrace;

                if (!String.IsNullOrEmpty(message))
                {
                    message = ReplaceSpecialCharacters(message);

                    buffer.Append("<p>");
                    buffer.Append("<font style='font:9pt/12pt verdana;color:black'>");
                    buffer.Append(message);
                    buffer.Append("</font>");
                    buffer.Append("</p>");
                }

                e = e.InnerException;
            }
            
            buffer.Append("</body></html>");
            
            ExceptionBrowser.NavigateToString(buffer.ToString());

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;
        }
    }
}
