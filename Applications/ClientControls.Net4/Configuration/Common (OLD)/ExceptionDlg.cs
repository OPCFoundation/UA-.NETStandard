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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A dialog that displays an exception trace in an HTML page.
    /// </summary>
    public partial class ExceptionDlg : Form
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
            Text = caption;
   
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
            
            ExceptionBrowser.DocumentText = buffer.ToString();

            ShowDialog();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
