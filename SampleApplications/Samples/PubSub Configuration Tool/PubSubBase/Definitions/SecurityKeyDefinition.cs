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
using System.Collections.Generic;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// definition of Security keys
    /// </summary>
    public class SecurityKeys : SecurityBase
    {
        #region Private Fields

        private string m_securityPolicyUri;
        private uint m_currentTokenId;
        private double m_timeToNextKey;
        private double m_keyLifetime;
        private string m_currentKey;
        private List<string> m_featureKeys = new List<string>();

        #endregion

        #region Public Properties
        /// <summary>
        /// defines security policu uri
        /// </summary>
        public string SecurityPolicyUri
        {
            get
            {
                return m_securityPolicyUri;
            }
            set
            {
                m_securityPolicyUri = value;
                OnPropertyChanged("SecurityPolicyUri");
            }
        }

        /// <summary>
        /// defines current token ID
        /// </summary>
        public uint CurrentTokenId
        {
            get
            {
                return m_currentTokenId;
            }
            set
            {
                m_currentTokenId = value;
                OnPropertyChanged("CurrentTokenId");
            }
        }

        /// <summary>
        /// defines time for next key
        /// </summary>
        public double TimeToNextKey
        {
            get
            {
                return m_timeToNextKey;
            }
            set
            {
                m_timeToNextKey = value;
                OnPropertyChanged("TimeToNextKey");
            }
        }

        /// <summary>
        /// defines key life time 
        /// </summary>
        public double KeyLifetime
        {
            get
            {
                return m_keyLifetime;
            }
            set
            {
                m_keyLifetime = value;
                OnPropertyChanged("KeyLifetime");
            }
        }

        /// <summary>
        /// defines current key of security
        /// </summary>
        public string CurrentKey
        {
            get
            {
                return m_currentKey;
            }

            set
            {
                Name = m_currentKey = value;

                OnPropertyChanged("CurrentKey");
            }
        }

        /// <summary>
        /// defines Feature keys 
        /// </summary>
        public List<string> FeatureKeys
        {
            get
            {
                return m_featureKeys;
            }
            set
            {
                m_featureKeys = value;
                OnPropertyChanged("FeatureKeys");
            }
        }
        #endregion
    }

}
