# create the build instance 
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# restore solution
COPY ./src/NopCommerce.sln .
COPY **/*.csproj **/**/*.csproj **/**/**/*.csproj ./

RUN dotnet sln NopCommerce.sln list | while read line ; do mkdir -pv $(dirname $line) && mv $(basename $line) $line; done
RUN dotnet restore NopCommerce.sln

COPY ./src ./

WORKDIR /src/Presentation/Nop.Web

# build project
RUN dotnet build Nop.Web.csproj -c Release --no-restore

# build plugins
WORKDIR /src/Plugins/Nop.Plugin.DiscountRules.CustomerRoles
RUN dotnet build Nop.Plugin.DiscountRules.CustomerRoles.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.ExchangeRate.EcbExchange
RUN dotnet build Nop.Plugin.ExchangeRate.EcbExchange.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.ExternalAuth.ExtendedAuth
RUN dotnet build Nop.Plugin.ExternalAuth.ExtendedAuth.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Payments.CheckMoneyOrder
RUN dotnet build Nop.Plugin.Payments.CheckMoneyOrder.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Shipping.FixedByWeightByTotal
RUN dotnet build Nop.Plugin.Shipping.FixedByWeightByTotal.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Tax.FixedOrByCountryStateZip
RUN dotnet build Nop.Plugin.Tax.FixedOrByCountryStateZip.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Widgets.GoogleAnalytics
RUN dotnet build Nop.Plugin.Widgets.GoogleAnalytics.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Widgets.NivoSlider
RUN dotnet build Nop.Plugin.Widgets.NivoSlider.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.BuyAmScraper
RUN dotnet build Nop.Plugin.BuyAmScraper.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Notifications.Manager
RUN dotnet build Nop.Plugin.Notifications.Manager.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Payments.Idram
RUN dotnet build Nop.Plugin.Payments.Idram.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Payments.AmeriaVPos
RUN dotnet build Nop.Plugin.Payments.AmeriaVPos.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Company.Company
RUN dotnet build Nop.Plugin.Company.Company.csproj -c Release
WORKDIR /src/Plugins/Nop.Plugin.Company.Insights
RUN dotnet build Nop.Plugin.Company.Insights.csproj -c Release

# publish project
WORKDIR /src/Presentation/Nop.Web
RUN dotnet publish Nop.Web.csproj --no-restore -c Release -o /app/published

# create the runtime instance 
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime 

# add globalization support
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# installs required packages
RUN apk add tiff --no-cache --repository http://dl-3.alpinelinux.org/alpine/edge/main/ --allow-untrusted
RUN apk add libgdiplus --no-cache --repository http://dl-3.alpinelinux.org/alpine/edge/community/ --allow-untrusted
RUN apk add libc-dev --no-cache
RUN apk add tzdata --no-cache

# copy entrypoint script
COPY ./entrypoint.sh /entrypoint.sh
RUN chmod 755 /entrypoint.sh

WORKDIR /app
RUN mkdir bin
RUN mkdir logs

COPY --from=build /app/published .

# Build metadata - the source commit baked into the image itself, not just the
# tag. Kept at the very end so it never busts the (slow) build/publish cache.
# CI passes these (see .github/workflows/docker-image.yml); a manual build should
# too: docker build --build-arg GIT_COMMIT=$(git rev-parse HEAD) ...
# Traceable three ways: image label (docker/crane inspect), env var
# (kubectl exec <pod> -- printenv GIT_COMMIT), and /app/COMMIT (cat in the pod).
ARG GIT_COMMIT=unknown
ARG GIT_BRANCH=unknown
ARG BUILD_DATE=unknown
LABEL org.opencontainers.image.revision=$GIT_COMMIT \
      org.opencontainers.image.version=$GIT_BRANCH \
      org.opencontainers.image.created=$BUILD_DATE \
      org.opencontainers.image.source="https://github.com/lookbehind/nopcommerce"
ENV GIT_COMMIT=$GIT_COMMIT \
    GIT_BRANCH=$GIT_BRANCH
RUN printf '%s\n' "$GIT_COMMIT" > /app/COMMIT

# call entrypoint script instead of dotnet
ENTRYPOINT "/entrypoint.sh"
