
using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

[assembly: Dependency(typeof(XamarinClient.PathService))]

namespace XamarinClient
{
    public class PathService : IPathService
    {
        public string InternalFolder
        {
            get
            {
                return null;
            }
        }

        public string PublicExternalFolder
        {
            get
            {
                return @"%CommonApplicationData%\CertificateStores\";
            }
        }

        public string PrivateExternalFolder
        {
            get
            {
                return null;
            }
        }
    }
}
