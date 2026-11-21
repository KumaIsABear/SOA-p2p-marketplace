FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Services/Inventory.API/Inventory.API.csproj src/Services/Inventory.API/
RUN dotnet restore src/Services/Inventory.API/Inventory.API.csproj

COPY src/Services/Inventory.API/ src/Services/Inventory.API/
RUN dotnet publish src/Services/Inventory.API/Inventory.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "Inventory.API.dll"]
