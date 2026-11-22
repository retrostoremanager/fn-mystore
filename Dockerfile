# Local/Podman-only image for full-stack testing -- NOT used for the real Azure deploy
# (that's GitHub Actions -> az functionapp deployment, untouched by this file).
#
# Runs via Azure Functions Core Tools (func start) rather than the prebuilt
# mcr.microsoft.com/azure-functions/dotnet-isolated runtime image, so this works
# regardless of whether Microsoft has published a matching runtime tag for whatever
# .NET version this repo targets, and so local.settings.json's Host.CORS setting
# (Core Tools-specific, not a normal app setting) actually takes effect.
FROM mcr.microsoft.com/dotnet/sdk:8.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && npm install -g azure-functions-core-tools@4 --unsafe-perm true \
    && apt-get purge -y curl gnupg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .
RUN dotnet publish MyStore.Functions/MyStore.Functions.csproj -c Release -o /app

WORKDIR /app
# local.settings.docker.json carries only local/dev placeholder values (committed) --
# real local.settings.json (gitignored, real secrets) is irrelevant here since this
# never talks to anything but the other containers on the podman network.
COPY local.settings.docker.json local.settings.json

EXPOSE 80
ENTRYPOINT ["func", "start", "--port", "80", "--no-build"]
