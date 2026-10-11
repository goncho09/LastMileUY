FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet publish src/LastMileUY.DriverPWA/LastMileUY.DriverPWA.csproj \
    -c Release \
    -o /app/publish

FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80