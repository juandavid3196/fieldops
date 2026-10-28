# FieldOps API image for Railway. Build context: backend/ (Root Directory = backend).
# Targets linux/amd64. See docs/deployment/demo-deployment.md.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY FieldOps.slnx dotnet-tools.json ./
COPY src/FieldOps.Domain/FieldOps.Domain.csproj src/FieldOps.Domain/
COPY src/FieldOps.Application/FieldOps.Application.csproj src/FieldOps.Application/
COPY src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj src/FieldOps.Infrastructure/
COPY src/FieldOps.Api/FieldOps.Api.csproj src/FieldOps.Api/
RUN dotnet tool restore \
    && dotnet restore src/FieldOps.Api/FieldOps.Api.csproj

COPY src/ src/
RUN dotnet publish src/FieldOps.Api/FieldOps.Api.csproj \
    --configuration Release --no-restore --output /out/api -p:UseAppHost=false

# Migration bundle: a self-contained executable that applies the EF Core
# migrations. It is built here but never run during the build. The connection
# string below is a placeholder needed only so the design-time host can be
# constructed; the real one comes from the environment when the bundle runs.
ARG BUNDLE_RID=linux-x64
RUN ConnectionStrings__FieldOpsDatabase="Host=build-placeholder;Database=none" \
    dotnet ef migrations bundle \
      --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj \
      --startup-project src/FieldOps.Api/FieldOps.Api.csproj \
      --configuration Release \
      --target-runtime ${BUNDLE_RID} --self-contained \
      --output /out/efbundle --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# setpriv drops root after the entrypoint fixes the volume ownership.
RUN setpriv --version > /dev/null

COPY --from=build /out/api ./
COPY --from=build /out/efbundle ./efbundle
COPY docker-entrypoint.sh ./docker-entrypoint.sh
RUN chmod +x ./efbundle ./docker-entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

# No USER and no ENTRYPOINT on purpose: Railway mounts volumes as root, so the
# entrypoint starts as root, prepares the key directory and drops to "app".
# Pre-deploy commands (./efbundle) replace CMD and must not go through an entrypoint.
CMD ["/app/docker-entrypoint.sh"]
