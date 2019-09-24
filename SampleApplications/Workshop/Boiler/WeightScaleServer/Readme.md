# WeightScaleServer
Is based on Boiler Server. The purpose of this project is to enable custom _information model_ based on DI companion specification.
The DI information model is based on [OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset) v1.04, because the current NETStandard stack is using same version.
## Loading DI namespace
The DI nodeset is loaded in method [WeightScaleNodeManager](SampleApplications/Workshop/Boiler/WeightScaleServer/WeightScaleNodeManager.cs):
```
 public WeightScaleNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
**    base(server, configuration, Opc.Ua.Di.Namespaces.OpcUaDi)**
{
    SystemContext.NodeIdFactory = this;

    //// set one namespace for the type model and one names for dynamically created nodes.
    //string[] namespaceUrls = new string[2];
    //namespaceUrls[0] = Namespaces.Boiler;
    //namespaceUrls[1] = Namespaces.Boiler + "/Instance";
    //SetNamespaces(namespaceUrls);

    // get the configuration for the node manager.
    m_configuration = configuration.ParseExtension<WeightScaleServerConfiguration>();

    // use suitable defaults if no configuration exists.
    if (m_configuration == null)
    {
        m_configuration = new WeightScaleServerConfiguration();
    }
}
```