# FundWatch

FundWatch is a comprehensive stock tracking and simulation app designed to help investors and traders make informed decisions. Built using .NET Core, this application provides powerful tools to track your investments, analyze potential returns, and simulate various stock purchase scenarios.

## Features

- **Stock Tracking**: Monitor key details like stock symbols, purchase price, date purchased, number of shares, current price, date sold, and value changes.
- **Portfolio Simulation**: Run simulations to see how different purchase dates and strategies could impact your portfolio's performance.
- **Paper Portfolio**: Create and manage a paper portfolio to test strategies without risking real money.
- **Historical Analysis**: Analyze how much you would have made if you bought a stock on a specific date and sold it on another.
- **User-Friendly Interface**: Intuitive design for easy navigation and detailed insights into your investments.

## Getting Started

### Prerequisites

- .NET Core SDK 3.1 or higher
- SQL Server or PostgreSQL (for data storage)
- Git (for version control)

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/your-username/FundWatch.git
   cd FundWatch
   ```
2. Set up the database:
   - Update the connection string in appsettings.json to point to your SQL Server or PostgreSQL instance.
   - Run the initial migrations to set up the database schema:
   ```bash
   dotnet ef database update
   ```
3. Run the application:
   ```bash
   dotnet run
   ```
4. Open your browser and navigate to http://localhost:5000 to start using FundWatch.

# Contributing

Contributions are welcome! If you'd like to contribute, please fork the repository, create a new branch, and submit a pull request. Make sure to follow the coding standards and include detailed documentation for any new features.
