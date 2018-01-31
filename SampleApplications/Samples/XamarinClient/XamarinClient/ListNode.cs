using System;
using System.Collections.Generic;
using System.Text;

namespace XamarinClient
{
    public class ListNode
    {
        public string id;
        public string nodeClass;
        public string accessLevel;
        public string executable;
        public string eventNotifier;
        public bool children;
        
        public string ImageUrl { get; set; }

        public string NodeName { get; set; }

        public ListNode()
        {
        }
    }
}
