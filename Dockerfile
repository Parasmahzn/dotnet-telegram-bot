FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MeroShareBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish MeroShareBot.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Install Chromium + OS dependencies via the Playwright CLI bundled in the publish output.
# (The playwright.ps1 wrapper just invokes this DLL; no PowerShell needed in the image.)
RUN dotnet Microsoft.Playwright.dll install --with-deps chromium

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MeroShareBot.dll"]
