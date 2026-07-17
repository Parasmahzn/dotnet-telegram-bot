FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MeroShareBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish MeroShareBot.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__Default=""
ENTRYPOINT ["dotnet", "MeroShareBot.dll"]
