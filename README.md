# FundWatch

[FundWatch](https://fundwatch-app-east-crh6cmajgxb2a5ct.canadaeast-01.azurewebsites.net/) is a comprehensive stock tracking and portfolio management application that helps users monitor investments and simulate trading strategies.

## Features

- **Stock Portfolio Tracking**: Monitor your investments with detailed metrics including purchase price, current value, and performance statistics
- **Stock Data Integration**: Leverages Polygon.io API to fetch real-time and historical stock data
- **Performance Analytics**: Visual charts and metrics showing portfolio performance over time
- **Transaction History**: Track purchases and sales with comprehensive history views
- **Watchlist Management**: Keep track of potential investment opportunities
- **Simulation Tools**: Test different investment strategies with historical data

## Technology Stack

- **Backend**: ASP.NET Core MVC (.NET 8)
- **Database**: Microsoft SQL Server (migrated from PostgreSQL)
- **Frontend**:
  - HTML5, CSS3, JavaScript
  - Syncfusion UI components
  - Bootstrap for responsive design
- **Authentication**: ASP.NET Core Identity
- **External APIs**: Polygon.io Stock API
- **Cloud Infrastructure**:
  - Microsoft Azure App Service
  - Azure SQL Database
  - Cloudflare for DNS, SSL, and security

## Getting Started

### Prerequisites

- .NET SDK 8.0 or later
- SQL Server (local or cloud instance)
- Visual Studio 2022 or Visual Studio Code
- Polygon.io API key

### Local Development Setup

1. Clone the repository:
   ```sh
   git clone https://github.com/taylor-js/FundWatch.git
   cd FundWatch
   ```

2. Set up user secrets for connection strings and API keys:
   ```sh
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=FundWatch;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"
   dotnet user-secrets set "PolygonApi:ApiKey" "YOUR_POLYGON_API_KEY"
   dotnet user-secrets set "Syncfusion:LicenseKey" "YOUR_SYNCFUSION_LICENSE_KEY"
   ```

3. Apply database migrations:
   ```sh
   dotnet ef database update --context ApplicationDbContext
   dotnet ef database update --context AuthDbContext
   ```

4. Run the application:
   ```sh
   dotnet run
   ```

5. Open your browser and navigate to [http://localhost:5147](http://localhost:5147)

### Polygon.io API Integration

FundWatch utilizes several Polygon.io API endpoints:

- `/v2/aggs/ticker/{symbol}/range/{multiplier}/{timespan}/{from}/{to}` - For historical price data
- `/v3/reference/tickers/{symbol}` - For company details
- `/v2/snapshot/locale/us/markets/stocks/tickers/{symbol}` - For real-time quotes

### Deployment

The application is deployed on Microsoft Azure using GitHub Actions for CI/CD:

- Every commit to the main branch triggers an automated build and deployment
- Azure SQL Database is used for data storage
- Cloudflare provides additional security and performance optimizations
