# Compilar Tailwind
FROM node:22-alpine AS styles

WORKDIR /src
COPY src/LastMileUY.DriverPWA/package*.json ./
RUN npm ci

COPY src/LastMileUY.DriverPWA/Pages ./Pages
COPY src/LastMileUY.DriverPWA/Layout ./Layout
COPY src/LastMileUY.DriverPWA/Styles ./Styles

RUN npm run build:css

# Compilar Blazor
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .

COPY --from=styles /src/wwwroot/css/tailwind.css \
    src/LastMileUY.DriverPWA/wwwroot/css/tailwind.css

RUN dotnet publish src/LastMileUY.DriverPWA/LastMileUY.DriverPWA.csproj \
    -c Release \
    -o /app/publish

# Servir con nginx
FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/LastMileUY.DriverPWA/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80