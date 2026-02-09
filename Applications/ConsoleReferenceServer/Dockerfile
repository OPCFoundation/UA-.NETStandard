FROM mcr.microsoft.com/dotnet/runtime AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY *.* .
COPY ["Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj", "Applications/ConsoleReferenceServer/"]
COPY ["Tools/Opc.Ua.SourceGeneration.Core/Opc.Ua.SourceGeneration.Core.csproj", "Tools/Opc.Ua.SourceGeneration.Core/"]
COPY ["Tools/Opc.Ua.SourceGeneration.Stack/Opc.Ua.SourceGeneration.Stack.csproj", "Tools/Opc.Ua.SourceGeneration.Stack/"]
COPY ["Tools/Opc.Ua.SourceGeneration/Opc.Ua.SourceGeneration.csproj", "Tools/Opc.Ua.SourceGeneration/"]
COPY ["Stack/Opc.Ua/Opc.Ua.csproj", "Stack/Opc.Ua/"]
COPY ["Stack/Opc.Ua.Core/Opc.Ua.Core.csproj", "Stack/Opc.Ua.Core/"]
COPY ["Stack/Opc.Ua.Types/Opc.Ua.Types.csproj", "Stack/Opc.Ua.Types/"]
COPY ["Libraries/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj", "Libraries/Opc.Ua.Security.Certificates/"]
COPY ["Libraries/Opc.Ua.Configuration/Opc.Ua.Configuration.csproj", "Libraries/Opc.Ua.Configuration/"]
COPY ["Libraries/Opc.Ua.Server/Opc.Ua.Server.csproj", "Libraries/Opc.Ua.Server/"]
COPY ["Applications/Quickstarts.Servers/Quickstarts.Servers.csproj", "Applications/Quickstarts.Servers/"]
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet restore "Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj" --force -p:NoHttps=true -p:CustomTestTarget=net10.0
COPY . .

WORKDIR "/src/Applications/ConsoleReferenceServer"
ARG Version
ARG SimpleVersion
ARG InformationalVersion
ENV OPTIONS="-c Release -f net10.0 -p:NoHttps=true -p:Dockerbuild=true -p:Version=${Version} -p:AssemblyVersion=${SimpleVersion} -p:FileVersion=${Version} -p:InformationalVersion=${InformationalVersion} -p:CustomTestTarget=net10.0"

FROM build AS publish
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet publish "ConsoleReferenceServer.csproj" --no-restore ${OPTIONS} -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
