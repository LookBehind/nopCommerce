# Running the nopCommerce Project

This document provides instructions for building and running the nopCommerce web application.

## Prerequisites

- .NET SDK installed
- Database configured (if required)

## Quick Start

### 1. Navigate to Project Directory

```bash
cd ~/workspace/personal/MySnacks/nopCommerce
```

### 2. Build and Run the Application

Build the solution and run the web project:

```bash
dotnet build src/NopCommerce.sln && dotnet run --no-build --project src/Presentation/Nop.Web/Nop.Web.csproj
```

This command will:
- Build the entire solution (`NopCommerce.sln`)
- Run the web application without rebuilding (`--no-build` flag)

### 3. Access the Web Application

Once the application is running, open your browser and navigate to:

**http://localhost:4000/**

The application should now be accessible in your browser.

## Notes

- The application runs on port `4000` by default
- Ensure your database is properly configured before running
- Check the console output for any errors or warnings during startup
- After ANY change re-run the step 2 command. There's no hot-reload in this project.
