# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && \
    apt-get install -y --no-install-recommends python3 && \
    ln -s /usr/bin/python3 /usr/bin/python

RUN dotnet workload install wasm-tools

COPY ["frontend/Directory.Build.props", "frontend/"]
COPY ["frontend/Directory.Packages.props", "frontend/"]
COPY ["backend/Directory.Build.props", "backend/"]
COPY ["backend/Directory.Packages.props", "backend/"]
COPY ["backend/global.json", "backend/"]
COPY ["frontend/global.json", "frontend/"]

COPY ["frontend/Money.Web/Money.Web.csproj", "frontend/Money.Web/"]
COPY ["backend/Money.ApiClient/Money.ApiClient.csproj", "backend/Money.ApiClient/"]

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "frontend/Money.Web/Money.Web.csproj"

COPY ["frontend/", "frontend/"]
COPY ["backend/Money.ApiClient/", "backend/Money.ApiClient/"]

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "frontend/Money.Web/Money.Web.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM nginx:alpine
WORKDIR /usr/share/nginx/html

RUN apk add --no-cache nginx-mod-http-brotli && \
    sed -i '1i load_module modules/ngx_http_brotli_filter_module.so;' /etc/nginx/nginx.conf && \
    sed -i '1i load_module modules/ngx_http_brotli_static_module.so;' /etc/nginx/nginx.conf

RUN rm -rf ./*

COPY --from=build /app/publish/wwwroot .
COPY ["frontend/Money.Web/nginx.conf", "/etc/nginx/conf.d/default.conf"]

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
