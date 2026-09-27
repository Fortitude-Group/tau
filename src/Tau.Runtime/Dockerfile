# Tau Runtime container. Built locally only; never pushed without Rob's go (constitution Principle X).
#
#   docker build -f src/Tau.Runtime/Dockerfile -t tau-runtime:local .                         # CPU
#   docker build -f src/Tau.Runtime/Dockerfile --build-arg FLAVOUR=cuda -t tau-runtime:cuda .    # CUDA
#
# Models aren't baked in (they're multi-GB and pinned by hash). Mount them read-only, and name the
# models to serve (otherwise every package under /models is loaded):
#   docker run --rm -p 8088:8080 -v ${PWD}/models:/models:ro -e Tau__Models__0=laya-en tau-runtime:local
# For CUDA add `--gpus all` and build with FLAVOUR=cuda (the base image then carries CUDA 12 + cuDNN 9).

ARG FLAVOUR=cpu

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Tau.slnx ./
COPY contracts/ contracts/
COPY src/ src/
RUN dotnet publish src/Tau.Runtime/Tau.Runtime.csproj -c Release -r linux-x64 --self-contained false \
      -p:PublishSingleFile=false -o /out

# ONNX Runtime native flavour for linux-x64, fetched from nuget.org by the same script the host uses.
FROM mcr.microsoft.com/powershell:latest AS natives
ARG FLAVOUR
WORKDIR /src
COPY scripts/fetch-natives.ps1 scripts/
RUN pwsh -NoProfile -File scripts/fetch-natives.ps1 -Rid linux-x64 -Flavour ${FLAVOUR} -Root /src

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-cpu
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
FROM nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04 AS runtime-cuda
# The ASP.NET Core runtime comes from Microsoft's own image rather than an apt feed.
COPY --from=mcr.microsoft.com/dotnet/aspnet:10.0 /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && apt-get update && apt-get install -y --no-install-recommends curl libicu74 && rm -rf /var/lib/apt/lists/* \
    && (id app >/dev/null 2>&1 || useradd --uid 1654 --create-home app)

FROM runtime-${FLAVOUR} AS final
ARG FLAVOUR
WORKDIR /app
COPY --from=build /out ./
COPY --from=natives /src/native/ ./native/
# Non-prefixed Urls: it outranks appsettings.json's local-development Urls (ASPNETCORE_URLS would not).
ENV Urls=http://+:8080 \
    Tau__Provider=${FLAVOUR} \
    Tau__ModelsDirectory=/models \
    Tau__NativeDirectory=/app/native
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=120s \
  CMD curl -fsS http://localhost:8080/healthz || exit 1
USER app
ENTRYPOINT ["dotnet", "Tau.Runtime.dll"]
