FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["backend/Directory.Build.props", "backend/"]
COPY ["backend/Directory.Packages.props", "backend/"]
COPY ["backend/global.json", "backend/"]

COPY ["backend/Money.Api/Money.Api.csproj", "backend/Money.Api/"]
COPY ["backend/Money.Business/Money.Business.csproj", "backend/Money.Business/"]
COPY ["backend/Money.Common/Money.Common.csproj", "backend/Money.Common/"]
COPY ["backend/Money.Data/Money.Data.csproj", "backend/Money.Data/"]
COPY ["backend/Money.ApiClient/Money.ApiClient.csproj", "backend/Money.ApiClient/"]

RUN dotnet restore "backend/Money.Api/Money.Api.csproj"

COPY . .

RUN dotnet publish "backend/Money.Api/Money.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Money.Api.dll"]
EXPOSE 8080
