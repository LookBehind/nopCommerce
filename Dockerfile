# create the build instance 
FROM microsoft/dotnet:2.2-sdk AS build

WORKDIR /src                                                                    
COPY ./src ./

# restore solution
RUN dotnet restore NopCommerce.sln

# build and publish solution 
RUN dotnet build NopCommerce.sln -c Release -o /app
RUN dotnet publish NopCommerce.sln -c Release -o /app/published

# create the runtime instance 
FROM microsoft/dotnet:2.2-aspnetcore-runtime-alpine AS runtime 

# add globalization support
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app        
RUN mkdir bin
RUN mkdir logs  
                                                            
COPY --from=build /app/published .
                            
ENTRYPOINT ["dotnet", "Nop.Web.dll"]
