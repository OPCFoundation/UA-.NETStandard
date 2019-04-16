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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample
{
    public partial class HistoryReadDetails : UserControl
    {
        public HistoryReadDetails()
        {
            InitializeComponent();
            
            QueryTypeCB.Items.Clear();
            QueryTypeCB.Items.Add("Read Raw or Modified");
        }

        private Session m_session;
        private ReadRawModifiedDetails m_details;
        
        #region Private Methods
        /// <summary>
        /// Initializes the control
        /// </summary>
        /// <param name="session"></param>
        /// <param name="details"></param>
        /// <param name="nodes"></param>
        public void Initialize(
            Session session,
            ReadRawModifiedDetails details,
            IList<ILocalNode> nodes)
        {
            m_session = session;
            m_details = details;

            StartTimeCTRL.Value = ToControlDateTime(details.StartTime);
            StartTimeSpecifiedCHK.Checked = details.StartTime != DateTime.MinValue;
            EndTimeCTRL.Value = ToControlDateTime(details.EndTime);
            EndTimeSpecifiedCHK.Checked = details.EndTime != DateTime.MinValue;
            MaxValuesCTRL.Value = details.NumValuesPerNode;
            IncludeBoundsCHK.Checked = details.ReturnBounds;
            IsModifiedCHK.Checked = details.IsReadModified;
        }
        #endregion
        
        #region Private Methods
        private DateTime ToControlDateTime(DateTime value)
        {
            if (value < new DateTime(1900,1,1))
            {
                return new DateTime(1900,1,1);
            }

            if (value > new DateTime(2100,1,1))
            {
                return new DateTime(2100,1,1);
            }

            return value;
        }

        private DateTime FromControlDateTime(DateTime value)
        {
            if (value <= new DateTime(1900,1,1))
            {
                return DateTime.MinValue;
            }

            if (value >= new DateTime(2100,1,1))
            {
                return DateTime.MaxValue;
            }

            return value;
        }
        #endregion
    }
}
