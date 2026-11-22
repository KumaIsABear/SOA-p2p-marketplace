FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Shared/Marketplace.Contracts/Marketplace.Contracts.csproj src/Shared/Marketplace.Contracts/
COPY src/Services/Wallet.API/Wallet.API.csproj src/Services/Wallet.API/
RUN dotnet restore src/Services/Wallet.API/Wallet.API.csproj

COPY src/Shared/Marketplace.Contracts/ src/Shared/Marketplace.Contracts/
COPY src/Services/Wallet.API/ src/Services/Wallet.API/
RUN dotnet publish src/Services/Wallet.API/Wallet.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "Wallet.API.dll"]