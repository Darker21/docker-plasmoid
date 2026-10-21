FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS base
USER $APP_UID

WORKDIR /app/Logs
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=
WORKDIR /src
COPY ["src/PlasmaDockerMonitor.Api/PlasmaDockerMonitor.Api.csproj", "PlasmaDockerMonitor.Api/"]
RUN dotnet restore "PlasmaDockerMonitor.Api/PlasmaDockerMonitor.Api.csproj"
COPY ./src .
WORKDIR "/src/PlasmaDockerMonitor.Api"
RUN dotnet build "./PlasmaDockerMonitor.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:ReleaseVersion=$VERSION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=
RUN dotnet publish "./PlasmaDockerMonitor.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:ReleaseVersion=$VERSION

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PlasmaDockerMonitor.Api.dll"]
