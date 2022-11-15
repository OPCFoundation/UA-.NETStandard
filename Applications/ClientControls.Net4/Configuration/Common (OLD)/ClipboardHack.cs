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
using System.Text;
using System.Windows.Forms;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// This class is used to work around a bug in the MS VPC implementation. 
    /// </summary>    
    /// <remarks>
    /// Clipborad operations will fail if this class is not used on VPCs with the 
    /// virtual machine additions installed.
    /// </remarks>
    public static class ClipboardHack
    {
        #region Public Methods
        /// <summary>
        /// Retrieves the data from the clipboard.
        /// </summary>
        public static object GetData(string format)
        {
            lock (m_lock)
            {
                m_format = format;
                m_data = null;
                m_error = null;

                Thread thread = new Thread(new ThreadStart(GetClipboardPrivate));
                thread.IsBackground = true;
                
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (m_error != null)
                {
                    throw new ServiceResultException(m_error, StatusCodes.BadUnexpectedError);
                }

                return m_data;
            }
        }
        
        /// <summary>
        /// Saves the data in the clipboard.
        /// </summary>
        public static void SetData(string format, object data)
        {
            lock (m_lock)
            {
                m_format = format;
                m_data = data;
                m_error = null;

                Thread thread = new Thread(new ThreadStart(SetClipboardPrivate));
                thread.IsBackground = true;
                
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (m_error != null)
                {
                    throw new ServiceResultException(m_error, StatusCodes.BadUnexpectedError);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the data in the clipboard if it is the correct format.
        /// </summary>
        private static void GetClipboardPrivate()
        {
            try
            {
                m_error = null;

                if (String.IsNullOrEmpty(m_format) || !Clipboard.ContainsData(m_format))
                { 
                    m_data = null;
                    return;
                }
                
                m_data = Clipboard.GetData(m_format);
            }
            catch (Exception e)
            {
                m_error = e;
            }
        }
        
        /// <summary>
        /// Saves the data in the clipboard if it is the correct format.
        /// </summary>
        private static void SetClipboardPrivate()
        {
            try
            {
                m_error = null;

                if (String.IsNullOrEmpty(m_format) || m_data == null)
                { 
                    return;
                }

                Clipboard.SetData(m_format, m_data);
            }
            catch (Exception e)
            {
                m_error = e;
            }
        }
        #endregion

        #region Private Fields
        private static object m_lock = new object();
        private static string m_format = null;
        private static object m_data = null;
        private static Exception m_error = null;
        #endregion
    }
}
