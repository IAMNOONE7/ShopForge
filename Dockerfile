FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ShopForge.slnx ./
COPY src ./src
RUN dotnet publish src/ShopForge.Api/ShopForge.Api.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./

# Container Apps sends traffic to this port and calls the readiness probe before putting a revision in rotation.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "ShopForge.Api.dll"]
