FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Services/Marketplace.API/Marketplace.API.csproj src/Services/Marketplace.API/
RUN dotnet restore src/Services/Marketplace.API/Marketplace.API.csproj

COPY src/Services/Marketplace.API/ src/Services/Marketplace.API/
RUN dotnet publish src/Services/Marketplace.API/Marketplace.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "Marketplace.API.dll"]
