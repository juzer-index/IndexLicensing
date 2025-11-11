# Use the official .NET Core SDK as a parent image 
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy the project file and restore dependencies
COPY . .
WORKDIR /app
RUN dotnet restore
CMD ['ls']
# Publish the app (we are already in /app/IndexInfo)
RUN dotnet publish IndexLicensing.csproj -c Release -o /app/out

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

# Expose the port
EXPOSE 80

# Run the app
ENTRYPOINT ["dotnet", "IndexLicensing.dll"]
