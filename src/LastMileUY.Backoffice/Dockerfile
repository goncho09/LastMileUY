# Imagen base para ejecutar .NET
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Generar Tailwind CSS con Node.js
FROM node:24-alpine AS tailwind
WORKDIR /src/LastMileUY.Backoffice

COPY src/LastMileUY.Backoffice/package*.json ./
RUN npm ci

COPY src/LastMileUY.Backoffice/Styles ./Styles
COPY src/LastMileUY.Backoffice/Pages ./Pages

RUN npx @tailwindcss/cli \
    -i ./Styles/input.css \
    -o ./wwwroot/css/tailwind.css \
    --minify

# Compilar el Backoffice
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/LastMileUY.Backoffice/LastMileUY.Backoffice.csproj", "src/LastMileUY.Backoffice/"]
COPY ["src/LastMileUY.Contracts/LastMileUY.Contracts.csproj", "src/LastMileUY.Contracts/"]

RUN dotnet restore "src/LastMileUY.Backoffice/LastMileUY.Backoffice.csproj"

COPY . .

# Incorporar el CSS generado
COPY --from=tailwind /src/LastMileUY.Backoffice/wwwroot/css/tailwind.css \
    /src/src/LastMileUY.Backoffice/wwwroot/css/tailwind.css

WORKDIR "/src/src/LastMileUY.Backoffice"
RUN dotnet build "./LastMileUY.Backoffice.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LastMileUY.Backoffice.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Carpeta para las claves de las cookies
USER root
RUN mkdir -p /claves && chown $APP_UID /claves
USER $APP_UID

ENTRYPOINT ["dotnet", "LastMileUY.Backoffice.dll"]