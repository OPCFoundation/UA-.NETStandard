/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

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

using System.ComponentModel;
using System.Windows;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// Base class for notification property changes
    /// </summary>
    public class BaseViewModel: INotifyPropertyChanged
    {
        #region INotifyPropertyChanged Members

        private PropertyChangedEventHandler m_handler;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add { m_handler += value; }
            remove
            {
                // ReSharper disable once DelegateSubtraction
                if (m_handler != null) m_handler -= value;
            }
        }

        protected void OnPropertyChanged(string info)
        {
            m_handler?.Invoke(this, new PropertyChangedEventArgs(info));
        }

        #endregion

        public Window OwnerWindow { get; set; }
    }
}
