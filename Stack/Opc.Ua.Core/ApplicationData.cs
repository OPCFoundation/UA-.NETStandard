using Microsoft.Extensions.PlatformAbstractions;
using System.Collections;

namespace Opc.Ua
{

    public static class ApplicationData
    {

        public static class Current
        {
            public static class LocalFolder
            {
                public static string Path = PlatformServices.Default.Application.ApplicationBasePath;
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
