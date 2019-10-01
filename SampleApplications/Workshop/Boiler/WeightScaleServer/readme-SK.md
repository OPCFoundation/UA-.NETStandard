# WeightScaleServer
[Anglická verzia je dostupná tu...](readme.md)

Projekt je odvodený od **Boiler Server**. Jeho cieľom je vytvorenie OPC UA serveru s _informačným modelom (IM)_ založeným na _Device Information Model (DI)_.
DI IM je z git [OPCFoundation/UA-Nodeset](https://github.com/OPCFoundation/UA-Nodeset) vetva v1.04, pretože aktuálny _NETStandard stack_ používa túto verziu.
## Pridanie DI a WS name space (menného priestoru) do address space (adresného priestoru)
Menný priestor váhy _Weight Scale (WS)_ používa objekty definované v mennom priestore DI, preto je potrebné nahrať do adresného priestoru oba menné priestory.
Súbory popisujúce menné priestory sú tieto:

![namespacesfiles.PNG](namespacesfiles.PNG)

Vo vlastnostiach súborov s príponou _.uanodes_ treba nastaviť **Build Action = [Embedded Resource](https://docs.microsoft.com/en-us/visualstudio/ide/build-actions?view=vs-2019)**.

Tieto súbory obsahujú predefinované uzly, ktoré je treba pridať do adresného priestoru servera pomocou metódy **LoadPredefinedNodes** v [WeightScaleNodeManager.cs](SampleApplications/Workshop/Boiler/WeightScaleServer/WeightScaleNodeManager.cs):

```
protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
{
    NodeStateCollection predefinedNodes = new NodeStateCollection();

    NodeStateCollection tmp = new NodeStateCollection();

    tmp.LoadFromBinaryResource(context,
    "Quickstarts.WeightScale.Server.Opc.Ua.Di.PredefinedNodes.uanodes",
    typeof(WeightScaleNodeManager).GetTypeInfo().Assembly,
    true);

    tmp.ForEach((ns) => predefinedNodes.Add(ns));
            
    tmp.LoadFromBinaryResource(context,
        "Quickstarts.WeightScale.Server.Opc.Ua.Ws.PredefinedNodes.uanodes",
        typeof(WeightScaleNodeManager).GetTypeInfo().Assembly, 
        true);

    tmp.ForEach((ns) => predefinedNodes.Add(ns));
            

    return predefinedNodes;
}
```
Súbory _.uanodes_ taktiež obsahujú informáciu o tom, do ktoréhe menného priestoru patria predefinované uzly. Na to aby predefinované uzly bolo zobrazené v adresnom priestore je treba do adresného priestoru pridať menné priestory DI a WS. 

Názovy menných priestorov sú zadefinované v súboroch [Opc.Ua.Di.Constants.cs](SampleApplications/Workshop/Boiler/WeightScaleServer/Opc.Ua.Di.Constants.cs) a [Opc.Ua.Ws.Constants.cs](SampleApplications/Workshop/Boiler/WeightScaleServer/Opc.Ua.Ws.Constants.cs) ako:

```
/// </summary>
public const string OpcUaDi = "http://opcfoundation.org/UA/DI/";

/// </summary>
public const string OpcUaWs = "http://phi-ware.com/FEISTU/WS/";
```

Pridanie menných prestorov do adresného priestoru sa deje v konštruktory triedy [**WeightScaleNodeManager**](SampleApplications/Workshop/Boiler/WeightScaleServer/WeightScaleNodeManager.cs):

```
public WeightScaleNodeManager(IServerInternal server, ApplicationConfiguration configuration)
:
    base(server, configuration, Opc.Ua.Ws.Namespaces.OpcUaDi, Opc.Ua.Ws.Namespaces.OpcUaWs)
{
...
}
```

Takto upravný program je potrebné skompilovať a spustiť.
![OPCUAServer.png](OPCUAServer.PNG)

Na overenie obsahu adresného priestoru je možné použit program [UaExpert](https://www.unified-automation.com/products/development-tools/uaexpert.html) a pridať server podľa návodu.
![UAExpert.PNG](UAExpert.PNG)



Check [Is there any solution to import nodeset xml file to opc ua server in C#? #546](https://github.com/OPCFoundation/UA-.NETStandard/issues/546)
