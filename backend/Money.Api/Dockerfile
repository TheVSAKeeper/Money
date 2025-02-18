FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Money.Api/Money.Api.csproj", "Money.Api/"]
COPY ["Money.Business/Money.Business.csproj", "Money.Business/"]
COPY ["Money.Common/Money.Common.csproj", "Money.Common/"]
COPY ["Money.Data/Money.Data.csproj", "Money.Data/"]
RUN dotnet restore "./Money.Api/Money.Api.csproj"
COPY . .
WORKDIR "/src/Money.Api"
RUN dotnet build "./Money.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Money.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Money.Api.dll"]
