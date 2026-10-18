# The two frontends, built as CI builds them, for the app to serve itself (D-206).
FROM node:24-alpine AS frontends
WORKDIR /src
COPY frontend/storefront/package.json frontend/storefront/package-lock.json frontend/storefront/
COPY frontend/admin/package.json frontend/admin/package-lock.json frontend/admin/
RUN npm ci --prefix frontend/storefront && npm ci --prefix frontend/admin
COPY frontend/storefront frontend/storefront
COPY frontend/admin frontend/admin
RUN npm run build --prefix frontend/storefront && npm run build --prefix frontend/admin

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
# The commit the image is built from. The source arrives without its history, so it is passed in, and the build puts
# it on its version, which is how a release tells its own revision from the one it replaces (D-205).
ARG SOURCE_VERSION=
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ShopForge.slnx ./
COPY src ./src
RUN dotnet publish src/ShopForge.Api/ShopForge.Api.csproj --configuration Release --output /app -p:SourceRevisionId="$SOURCE_VERSION"

# The -extra variant carries ICU and time-zone data. The plain Alpine image has neither and runs invariant, where
# asking for a shop's culture throws, and statement days are Prague days (D-197).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine-extra AS runtime
# Invoices are rendered in the container, and PDFsharp brings no fonts of its own.
RUN apk add --no-cache font-dejavu
WORKDIR /app
COPY --from=build /app ./
COPY --from=frontends /src/frontend/storefront/dist ./frontends/storefront
COPY --from=frontends /src/frontend/admin/dist ./frontends/admin

# Container Apps sends traffic to this port and calls the readiness probe before putting a revision in rotation.
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "ShopForge.Api.dll"]
