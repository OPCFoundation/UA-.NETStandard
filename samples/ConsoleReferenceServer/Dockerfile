FROM mcr.microsoft.com/dotnet/runtime AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY *.* .
COPY ["samples/ConsoleReferenceServer/ConsoleReferenceServer.csproj", "samples/ConsoleReferenceServer/"]
COPY ["tools/Opc.Ua.SourceGeneration.Core/Opc.Ua.SourceGeneration.Core.csproj", "tools/Opc.Ua.SourceGeneration.Core/"]
COPY ["tools/Opc.Ua.SourceGeneration.Stack/Opc.Ua.SourceGeneration.Stack.csproj", "tools/Opc.Ua.SourceGeneration.Stack/"]
COPY ["tools/Opc.Ua.SourceGeneration/Opc.Ua.SourceGeneration.csproj", "tools/Opc.Ua.SourceGeneration/"]
COPY ["src/Opc.Ua.Core.Types/Opc.Ua.Core.Types.csproj", "src/Opc.Ua.Core.Types/"]
COPY ["src/Opc.Ua.Core/Opc.Ua.Core.csproj", "src/Opc.Ua.Core/"]
COPY ["src/Opc.Ua.Core.Schema/Opc.Ua.Core.Schema.csproj", "src/Opc.Ua.Core.Schema/"]
COPY ["src/Opc.Ua.Core.Diagnostics/Opc.Ua.Core.Diagnostics.csproj", "src/Opc.Ua.Core.Diagnostics/"]
COPY ["src/Opc.Ua.Types/Opc.Ua.Types.csproj", "src/Opc.Ua.Types/"]
COPY ["src/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj", "src/Opc.Ua.Security.Certificates/"]
COPY ["src/Opc.Ua.Client/Opc.Ua.Client.csproj", "src/Opc.Ua.Client/"]
COPY ["src/Opc.Ua.Configuration/Opc.Ua.Configuration.csproj", "src/Opc.Ua.Configuration/"]
COPY ["src/Opc.Ua.Server/Opc.Ua.Server.csproj", "src/Opc.Ua.Server/"]
COPY ["samples/Quickstarts.Servers/Quickstarts.Servers.csproj", "samples/Quickstarts.Servers/"]
COPY ["src/Opc.Ua.Gds.Common/Opc.Ua.Gds.Common.csproj", "src/Opc.Ua.Gds.Common/"]
COPY ["src/Opc.Ua.Gds.Server.Common/Opc.Ua.Gds.Server.Common.csproj", "src/Opc.Ua.Gds.Server.Common/"]
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet restore "samples/ConsoleReferenceServer/ConsoleReferenceServer.csproj" --force -p:NoHttps=true -p:CustomTestTarget=net10.0
COPY . .

WORKDIR "/src/samples/ConsoleReferenceServer"
ARG Version
ARG SimpleVersion
ARG InformationalVersion
ENV OPTIONS="-c Release -f net10.0 -p:PublishAot=false -p:NoHttps=true -p:Dockerbuild=true -p:Version=${Version} -p:AssemblyVersion=${SimpleVersion} -p:FileVersion=${Version} -p:InformationalVersion=${InformationalVersion} -p:CustomTestTarget=net10.0"

FROM build AS publish
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet publish "ConsoleReferenceServer.csproj" --no-restore ${OPTIONS} -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
