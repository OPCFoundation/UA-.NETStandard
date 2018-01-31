using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

[assembly: Dependency(typeof(XamarinClient.PathService))]

namespace XamarinClient
{
    class PathService : IPathService
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
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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
