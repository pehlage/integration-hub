FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/IntegrationHub.MockErp/IntegrationHub.MockErp.csproj src/IntegrationHub.MockErp/
RUN dotnet restore src/IntegrationHub.MockErp/IntegrationHub.MockErp.csproj
COPY src/IntegrationHub.MockErp/ src/IntegrationHub.MockErp/
RUN dotnet publish src/IntegrationHub.MockErp/IntegrationHub.MockErp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegrationHub.MockErp.dll"]
