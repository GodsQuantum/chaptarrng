FROM mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net11.0-cross-x86@sha256:efd21070248f3e6cd728b069e6708de2a5188162bd3a7e2fe357732f4f78b848
RUN tdnf --refresh update -y && tdnf clean all
# Runtime pack compilation uses the cross sysroot and has no Azure CLI dependency.
RUN rpm -e azure-cli && clang --version && test -d /crossrootfs/x86
ARG BUILD_UID=1000
RUN test "$BUILD_UID" -gt 0 && install -d -m 0700 -o "$BUILD_UID" /home/runtime-builder
ENV HOME=/home/runtime-builder
USER ${BUILD_UID}
HEALTHCHECK --interval=5m CMD clang --version >/dev/null && test -r /crossrootfs/x86/usr/include/stdlib.h
