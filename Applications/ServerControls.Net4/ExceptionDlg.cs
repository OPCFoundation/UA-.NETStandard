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
using System.Text;
using System.Windows.Forms;

namespace Opc.Ua.Server.Controls
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

        private Exception m_exception;

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

        private void AddBlock(StringBuilder buffer, string text)
        {
            AddBlock(buffer, text, 0);
        }

        private void AddBlock(StringBuilder buffer, string text, int level)
        {
            if (!String.IsNullOrEmpty(text))
            {
                if (level > 0)
                {
                    if (level == 1)
                    {
                        buffer.Append("<tr style='background-color:#990000;");
                    }
                    else if (level == 2)
                    {
                        buffer.Append("<tr style='background-color:#CC6600;");
                    }
                    else
                    {
                        buffer.Append("<tr style='background-color:#999999;");
                    }

                    buffer.Append("color:#FFFFFF;font-weight:bold;font-size:10pt;font-family:Verdana'><td>");
                    buffer.Append("<p>"); 
                }
                else
                {
                    buffer.Append("<tr style='font-size:10pt;font-family:Verdana'><td>");
                    buffer.Append("<p>");
                }

                buffer.Append(ReplaceSpecialCharacters(text));
                buffer.Append("</p>");
                buffer.Append("</td></tr>");
            }
        }

        private void Add(StringBuilder buffer, Exception e, bool showStackTrace)
        {
            AddBlock(buffer, "EXCEPTION (" + e.GetType().Name + ")", 1);
            AddBlock(buffer, e.Message);

            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
            {
                ServiceResult sr = new ServiceResult(sre);

                while (sr != null)
                {
                    AddBlock(buffer, "SERVICE RESULT (" + new StatusCode(sr.Code).ToString() + ")", 2);

                    string text = (sr.LocalizedText != null) ? sr.LocalizedText.Text : null;

                    if (text != e.Message)
                    {
                        AddBlock(buffer, text);
                    }

                    AddBlock(buffer, sr.SymbolicId);
                    AddBlock(buffer, sr.NamespaceUri);

                    if (showStackTrace)
                    {
                        if (!String.IsNullOrEmpty(sre.AdditionalInfo))
                        {
                            AddBlock(buffer, "ADDITIONAL INFO (" + new StatusCode(sr.Code).ToString() + ")", 3);
                            AddBlock(buffer, sre.AdditionalInfo);
                        }
                    }

                    sr = sr.InnerResult;
                }
            }

            if (showStackTrace)
            {
                AddBlock(buffer, "STACK TRACE", 3);
                AddBlock(buffer, e.StackTrace);
            }
        }

        private void Show(bool showStackTrace)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.Append("<html><body style='margin:0;width:100%'>");
            //buffer.Append(ExceptionBrowser.Parent.Width);
            //buffer.Append("px'>");
            buffer.Append("<table border='1' style='width:100%'>");

            Exception e = m_exception;

            while (e != null)
            {
                Add(buffer, e, showStackTrace);
                e = e.InnerException;
            }

            buffer.Append("</table>");
            buffer.Append("</body></html>");

            ExceptionBrowser.DocumentText = buffer.ToString();
        }

        /// <summary>
        /// Displays the exception in a dialog.
        /// </summary>
        public static void Show(string caption, Exception e)
        {
            // check if running as a service.
            if (!Environment.UserInteractive)
            {
                Utils.Trace(e, "Unexpected error in '{0}'.", caption);
                return;
            }
            
            new ExceptionDlg().ShowDialog(caption, e);
        }

        /// <summary>
        /// Display the exception in the dialog.
        /// </summary>
        public void ShowDialog(string caption, Exception e)
        {
            if (!String.IsNullOrEmpty(caption))
            {
                Text = caption;
            }

            m_exception = e;
            
            #if _DEBUG
            ShowStackTracesCK.Checked = true;
            #else
            ShowStackTracesCK.Checked = false;
            #endif

            Show(ShowStackTracesCK.Checked);
            ShowDialog();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ShowStackTracesCK_CheckedChanged(object sender, EventArgs e)
        {
            Show(ShowStackTracesCK.Checked);
        }
    }
}
