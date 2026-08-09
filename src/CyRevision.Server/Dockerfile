FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates git git-lfs syncthing \
    && git lfs install --system \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --create-home --home-dir /home/cyrevision cyrevision \
    && mkdir -p /var/lib/cyrevision \
    && chown -R cyrevision:cyrevision /var/lib/cyrevision

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/CyRevision.Server/CyRevision.Server.csproj -c Release -o /out --no-self-contained

FROM runtime AS final
COPY --from=build /out .
VOLUME ["/var/lib/cyrevision"]
USER cyrevision
ENTRYPOINT ["dotnet", "CyRevision.Server.dll"]
