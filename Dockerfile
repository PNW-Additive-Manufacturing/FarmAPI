# Adapted from https://github.com/bambulab/BambuStudio/blob/master/Dockerfile
FROM docker.io/ubuntu:22.04 AS bambu-setup

#ARG DEBIAN_FRONTEND="noninteractive"
RUN apt-get update && \
    echo 'debconf debconf/frontend select Noninteractive' | debconf-set-selections

RUN echo deb-src http://archive.ubuntu.com/ubuntu \
    $(cat /etc/*release | grep VERSION_CODENAME | cut -d= -f2) main universe>> /etc/apt/sources.list 

RUN apt-get update && apt-get install  -y \
    autoconf \
    build-essential \
    cmake \
    curl \
    eglexternalplatform-dev \
    extra-cmake-modules \
    file \
    git \
    gstreamer1.0-plugins-bad \
    gstreamer1.0-libav \
    libcairo2-dev \
    libcurl4-openssl-dev \
    libdbus-1-dev \
    libglew-dev \ 
    libglu1-mesa-dev \
    libglu1-mesa-dev \
    libgstreamer1.0-dev \
    libgstreamerd-3-dev \ 
    libgstreamer-plugins-base1.0-dev \
    libgstreamer-plugins-good1.0-dev \
    libgtk-3-dev \
    libgtk-3-dev \
    libosmesa6-dev \
    libsecret-1-dev \
    libsoup2.4-dev \
    libssl3 \
    libssl-dev \
    libudev-dev \
    libwayland-dev \
    libwebkit2gtk-4.0-dev \
    libxkbcommon-dev \
    locales \
    locales-all \
    m4 \
    pkgconf \
    sudo \
    wayland-protocols \
    wget \
    bc

RUN git clone https://github.com/bambulab/BambuStudio.git

WORKDIR /BambuStudio

RUN git fetch --all --tags --prune
RUN git checkout v01.09.05.51

ENV LC_ALL=en_US.utf8
RUN locale-gen $LC_ALL
ENV SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt

RUN ./BuildLinux.sh -u
RUN ./BuildLinux.sh -d
RUN ./BuildLinux.sh -s
RUN ./BuildLinux.sh -i

RUN mkdir -p /final \
    && cd /final \
    && cp -r /BambuStudio/build/package/bin . \
    && cp -r /BambuStudio/build/package/resources .

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY /FarmAPI/FarmAPI.csproj FarmAPI/
RUN dotnet restore "./FarmAPI/FarmAPI.csproj"
COPY . .
WORKDIR "/src/FarmAPI"
RUN dotnet build "./FarmAPI.csproj" -c "$BUILD_CONFIGURATION" -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish

ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FarmAPI.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

ARG DEBIAN_FRONTEND="noninteractive"

ENV BAMBU_STUDIO_PATH=/opt/bambu-studio/
ENV BAMBU_STUDIO_EXECUTABLE_PATH=/opt/bambu-studio/bin/bambu-studio

RUN \
    apt-get -y update && \
    apt-get -y install \
    libgtk-3-0 \
    libgl1 \
    libegl1 \
    libgstreamer1.0-0 \
    libgstreamer-plugins-base1.0-0 \
    libwebkit2gtk-4.0-37

COPY --from=bambu-setup /final /opt/bambu-studio/
COPY --from=publish /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "FarmAPI.dll"]