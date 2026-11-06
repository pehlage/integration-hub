FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/IntegrationHub.Api/IntegrationHub.Api.csproj src/IntegrationHub.Api/
RUN dotnet restore src/IntegrationHub.Api/IntegrationHub.Api.csproj
COPY src/IntegrationHub.Api/ src/IntegrationHub.Api/
RUN dotnet publish src/IntegrationHub.Api/IntegrationHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegrationHub.Api.dll"]
