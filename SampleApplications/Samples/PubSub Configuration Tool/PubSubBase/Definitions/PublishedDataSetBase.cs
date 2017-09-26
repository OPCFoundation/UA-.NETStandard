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

using System.Collections.ObjectModel;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// definition of Published data set
    /// </summary>
    public class PublishedDataSetBase : BaseViewModel
    {
        #region Private Fields
        string m_name = string.Empty;
        private ObservableCollection<PublishedDataSetBase> m_children = new ObservableCollection<PublishedDataSetBase>();
        #endregion

        #region Public Properties
        /// <summary>
        /// defines name of Published data set base
        /// </summary>
        public string Name
        {
            get
            {
                return m_name;
            }
            set
            {
                m_name = value;
                OnPropertyChanged("Name");
            }
        }

        /// <summary>
        /// collection of children definition of published data set base.
        /// </summary>
        public ObservableCollection<PublishedDataSetBase> Children
        {
            get { return m_children; }
            set
            {
                m_children = value;
                OnPropertyChanged("Children");
            }
        }

        /// <summary>
        ///defines Parent node of definition
        /// </summary>
        public PublishedDataSetBase ParentNode { get; set; }
        #endregion
    }
}
