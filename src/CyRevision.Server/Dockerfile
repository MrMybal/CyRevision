FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/CyRevision.Server/CyRevision.Server.csproj -c Release -o /out --no-self-contained

FROM runtime AS final
COPY --from=build /out .
ENTRYPOINT ["dotnet", "CyRevision.Server.dll"]

