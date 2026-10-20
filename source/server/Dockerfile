# Sumoner World dev server image (TDD §3.1a). Runs the Godot project headless as the dedicated server; dev and test only.
# Build from the source/ folder:  docker compose -f docker-compose.dev.yml up --build
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Pinned to ADR-001 (engine) and global.json (SDK); the checksum guards the download.
ARG GODOT_VERSION=4.7.2
ARG GODOT_SHA256=129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247

RUN apt-get update \
 && apt-get install -y --no-install-recommends unzip iproute2 \
 && rm -rf /var/lib/apt/lists/*

# The Godot project targets net8.0: add the .NET 8 runtime next to the pinned SDK 9.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

RUN curl -sSL -o /tmp/godot.zip "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip" \
 && echo "${GODOT_SHA256}  /tmp/godot.zip" | sha256sum -c - \
 && unzip -q /tmp/godot.zip -d /opt \
 && rm /tmp/godot.zip \
 && ln -s "/opt/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" /usr/local/bin/godot

WORKDIR /src
COPY . .
RUN dotnet build project-sumoner-worl/SumonerWorld.Game.csproj -c Debug -v q \
 && godot --headless --path project-sumoner-worl --import

RUN sed -i 's/\r$//' server/entrypoint.sh && chmod +x server/entrypoint.sh

EXPOSE 7777/udp
ENV SW_SERVER_MODE=dev
# Runs godot --headless with the arguments; applies SW_NETEM (tc netem) first when set (B2).
ENTRYPOINT ["/src/server/entrypoint.sh"]
CMD ["--server"]
