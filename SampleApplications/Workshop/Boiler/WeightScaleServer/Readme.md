# WeightScaleServer
Is based on Boiler Server. The purpose of this project is to enable custom _information model_ based on DI companion specification.
The DI information model is based on [OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset) v1.04, because the current NETStandard stack is using same version.
## Loading DI namespace
The DI nodeset is loaded in method [WeightScaleNodeManager](SampleApplications/Workshop/Boiler/WeightScaleServer/WeightScaleNodeManager.cs):
in base constructor wiht _Opc.Ua.Di.Namespaces.OpcUaDi_
```
 public WeightScaleNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
    base(server, configuration, Opc.Ua.Di.Namespaces.OpcUaDi)
{
...
}
```

Check [Is there any solution to import nodeset xml file to opc ua server in C#? #546](https://github.com/OPCFoundation/UA-.NETStandard/issues/546)