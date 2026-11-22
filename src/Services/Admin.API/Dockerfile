FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Shared/Marketplace.Contracts/Marketplace.Contracts.csproj src/Shared/Marketplace.Contracts/
COPY src/Services/Admin.API/Admin.API.csproj src/Services/Admin.API/
RUN dotnet restore src/Services/Admin.API/Admin.API.csproj

COPY src/Shared/Marketplace.Contracts/ src/Shared/Marketplace.Contracts/
COPY src/Services/Admin.API/ src/Services/Admin.API/
RUN dotnet publish src/Services/Admin.API/Admin.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "Admin.API.dll"]
