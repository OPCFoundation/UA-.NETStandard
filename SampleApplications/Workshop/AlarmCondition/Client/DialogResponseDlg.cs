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
using Opc.Ua;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Prompts the select a response for a Dialog Condition.
    /// </summary>
    public partial class DialogResponseDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public DialogResponseDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Prompts the user to enter a comment.
        /// </summary>
        public int ShowDialog(DialogConditionState dialog)
        {
            // set the prompt.
            PromptLB.Text = Utils.Format("{0}", BaseVariableState.GetValue(dialog.Prompt));

            Dictionary<DialogResult,int> resultMapping = new Dictionary<DialogResult, int>();

            // configure the buttons.
            LocalizedText[] options = BaseVariableState.GetValue(dialog.ResponseOptionSet);
            
            switch (options.Length)
            {
                case 1:
                {
                    OkBTN.Text = Utils.Format("{0}", options[0]);

                    ButtonsPN.ColumnStyles[0].Width = 50;
                    ButtonsPN.ColumnStyles[1].Width = 0;
                    ButtonsPN.ColumnStyles[2].Width = 0;
                    ButtonsPN.ColumnStyles[3].Width = 0;
                    ButtonsPN.ColumnStyles[4].Width = 50;
                    
                    resultMapping.Add(DialogResult.OK, 0);
                    break;
                }

                case 2:
                {
                    OkBTN.Text = Utils.Format("{0}", options[0]);
                    Response2BTN.Text = Utils.Format("{0}", options[1]);

                    ButtonsPN.ColumnStyles[0].Width = 33;
                    ButtonsPN.ColumnStyles[1].Width = 0;
                    ButtonsPN.ColumnStyles[2].Width = 34;
                    ButtonsPN.ColumnStyles[3].Width = 0;
                    ButtonsPN.ColumnStyles[4].Width = 33;

                    resultMapping.Add(DialogResult.OK, 0);
                    resultMapping.Add(DialogResult.Retry, 1);
                    break;
                }

                case 3:
                {
                    OkBTN.Text = Utils.Format("{0}", options[0]);
                    Response1BTN.Text = Utils.Format("{0}", options[1]);
                    Response3BTN.Text = Utils.Format("{0}", options[2]);

                    ButtonsPN.ColumnStyles[0].Width = 25;
                    ButtonsPN.ColumnStyles[1].Width = 25;
                    ButtonsPN.ColumnStyles[2].Width = 0;
                    ButtonsPN.ColumnStyles[3].Width = 25;
                    ButtonsPN.ColumnStyles[4].Width = 25;

                    resultMapping.Add(DialogResult.OK, 0);
                    resultMapping.Add(DialogResult.Abort, 1);
                    resultMapping.Add(DialogResult.Ignore, 2);
                    break;
                }

                case 4:
                {
                    OkBTN.Text = Utils.Format("{0}", options[0]);
                    Response1BTN.Text = Utils.Format("{0}", options[1]);
                    Response2BTN.Text = Utils.Format("{0}", options[2]);
                    Response3BTN.Text = Utils.Format("{0}", options[3]);

                    ButtonsPN.ColumnStyles[0].Width = 20;
                    ButtonsPN.ColumnStyles[1].Width = 20;
                    ButtonsPN.ColumnStyles[2].Width = 20;
                    ButtonsPN.ColumnStyles[3].Width = 20;
                    ButtonsPN.ColumnStyles[4].Width = 20;

                    resultMapping.Add(DialogResult.OK, 0);
                    resultMapping.Add(DialogResult.Abort, 1);
                    resultMapping.Add(DialogResult.Retry, 2);
                    resultMapping.Add(DialogResult.Ignore, 3);
                    break;
                }
            }

            // display the dialog.
            DialogResult result = ShowDialog();

            // map the response.
            int selectedResponse = -1;

            if (!resultMapping.TryGetValue(result, out selectedResponse))
            {
                return -1;
            }

            return selectedResponse;
        }
        #endregion
    }
}
