using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Opc.Ua
{

    public static class ApplicationData
    {
        //public static AppData ApplicationData;

        public static class Current
        {
            public static class LocalFolder
            {
                public static string Path;
            }
            public static class LocalSettings
            {
                public static ApplicationDataContainer CreateContainer(string application, ApplicationDataCreateDisposition adcd)
                {
                    return new ApplicationDataContainer();
                }
            }
        }
    }
    public enum ApplicationDataCreateDisposition
    {
        Always = 0,
        Existing = 1
    }

    public class ApplicationDataContainer
    {
        public IDictionary Values;
    }
}
