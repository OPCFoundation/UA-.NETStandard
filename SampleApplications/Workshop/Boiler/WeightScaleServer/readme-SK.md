# WeightScaleServer
[Anglická verzia je dostupná tu...](readme.md)

Projekt je odvodený od **Boiler Server**. Jeho cieľom je vytvorenie OPC UA serveru s _informačným modelom (IM)_ založeným na _Device Information Model (DI)_.
DI IM je z git [OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset) vetva v1.04, pretože aktuálny _NETStandard stack_ používa túto verziu.
## Nahratie DI a WS name space (menného priestoru)
Menný priestor váhy _Weight Scale (WS)_ používa objekty definované v mennom priestore DI, preto je potrebné nahrať do adresného priestoru oba menné priestory.
Súbory popisujúce menné priestory sú tieto:

![namespacesfiles.PNG](namespacesfiles.PNG)

Vo vlastnostiach súborov s príponou _.uanodes_ treba nastaviť **Build Action = [Embedded Resource](https://docs.microsoft.com/en-us/visualstudio/ide/build-actions?view=vs-2019)**.

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
