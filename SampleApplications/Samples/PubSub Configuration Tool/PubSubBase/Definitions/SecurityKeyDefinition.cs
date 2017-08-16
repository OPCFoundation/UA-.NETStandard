using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{

    public class SecurityKeys : SecurityBase
    {
        private string _securityPolicyUri;

        public string SecurityPolicyUri
        {
            get
            {
                return _securityPolicyUri;
            }
            set
            {
                _securityPolicyUri = value;
                OnPropertyChanged("SecurityPolicyUri");
            }
        }


        private uint _currentTokenId;
        public uint CurrentTokenId
        {
            get
            {
                return _currentTokenId;
            }
            set
            {
                _currentTokenId = value;
                OnPropertyChanged("CurrentTokenId");
            }
        }


        private double _TimeToNextKey;
        public double TimeToNextKey
        {
            get
            {
                return _TimeToNextKey;
            }
            set
            {
                _TimeToNextKey = value;
                OnPropertyChanged("TimeToNextKey");
            }
        }

        private double _KeyLifetime;
        public double KeyLifetime
        {
            get
            {
                return _KeyLifetime;
            }
            set
            {
                _KeyLifetime = value;
                OnPropertyChanged("KeyLifetime");
            }
        }
        private string _CurrentKey;
        public string CurrentKey
        {
            get
            {
                return _CurrentKey;
            }

            set
            {
                Name = _CurrentKey = value;

                OnPropertyChanged("CurrentKey");
            }
        }
        private List<string> _FeatureKeys=new List<string>();
        public List<string> FeatureKeys
        {
            get
            {
                return _FeatureKeys;
            }

            set
            {
                _FeatureKeys = value;
                OnPropertyChanged("FeatureKeys");
            }
        }

        
    }

}
