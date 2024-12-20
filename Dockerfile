FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["/frontend/Money.Web/Money.Web.csproj", "Money.Web/"]
COPY ["/frontend/Money.Web/nginx.conf", "/etc/nginx/nginx.conf"]
COPY ["/backend/Money.ApiClient/Money.ApiClient.csproj", "Money.ApiClient/"]
RUN apt-get update
RUN apt-get install -y python3
RUN dotnet workload install wasm-tools
RUN dotnet workload restore "Money.Web/Money.Web.csproj"
RUN dotnet restore "Money.Web/Money.Web.csproj"
COPY . .
WORKDIR "/src/frontend/Money.Web"
RUN dotnet build "Money.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Money.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Money.Web.dll"]

FROM nginx:alpine
WORKDIR /var/www/web
COPY --from=final /app/output/wwwroot .
EXPOSE 80
EXPOSE 443
