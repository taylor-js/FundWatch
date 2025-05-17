using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using FundWatch.Models;
using FundWatch.Models.ViewModels; // Add this for PerformancePoint class

namespace FundWatch.Services
{
    public class StockDataBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<StockDataBackgroundService> _logger;
        private readonly IMemoryCache _cache;

        public StockDataBackgroundService(
            IServiceProvider services,
            ILogger<StockDataBackgroundService> logger,
            IMemoryCache cache)
        {
            _services = services;
            _logger = logger;
            _cache = cache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateCacheAsync(stoppingToken);
                    // Increased cache update interval to reduce API calls
                    await Task.Delay(TimeSpan.FromHours(8), stoppingToken); // Increased from 4 hours to reduce API load
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating stock cache");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task UpdateCacheAsync(CancellationToken stoppingToken)
        {
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stockService = scope.ServiceProvider.GetRequiredService<StockService>();

            // Get unique stock symbols from active positions only (not all users)
            // This reduces API calls by only loading data for stocks that are currently held
            var symbols = await context.UserStocks
                .Where(s => s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0) > 0)
                .Select(s => s.StockSymbol)
                .Distinct()
                .ToListAsync(stoppingToken);

            if (!symbols.Any()) return;

            // Process data types sequentially to avoid exceeding API limits
            await UpdatePriceCache(stockService, symbols, stoppingToken);
            
            // Introduce delay between different types of API requests
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            
            await UpdateCompanyDetailsCache(stockService, symbols, stoppingToken);
            
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            
            await UpdateHistoryCache(stockService, symbols, stoppingToken);
        }
        private async Task UpdatePriceCache(StockService stockService, List<string> symbols, CancellationToken stoppingToken)
        {
            try
            {
                var prices = await stockService.GetRealTimePricesAsync(symbols);
                foreach (var (symbol, price) in prices)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
                        .SetSlidingExpiration(TimeSpan.FromMinutes(5));
                    _cache.Set($"Price_{symbol}", price, cacheOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating price cache");
            }
        }

        private async Task UpdateCompanyDetailsCache(StockService stockService, List<string> symbols, CancellationToken stoppingToken)
        {
            try
            {
                var details = await stockService.GetCompanyDetailsAsync(symbols);
                foreach (var (symbol, detail) in details)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(24)) // Company details change less frequently
                        .SetSlidingExpiration(TimeSpan.FromHours(1));
                    _cache.Set($"Details_{symbol}", detail, cacheOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating company details cache");
            }
        }

        private async Task UpdateHistoryCache(StockService stockService, List<string> symbols, CancellationToken stoppingToken)
        {
            try
            {
                // Process in smaller batches to avoid API rate limits
                const int batchSize = 3;
                for (int i = 0; i < symbols.Count; i += batchSize)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;
                        
                    var batch = symbols.Skip(i).Take(batchSize).ToList();
                    _logger.LogInformation($"Updating historical data for batch {i/batchSize + 1} of {(symbols.Count + batchSize - 1)/batchSize} ({batch.Count} symbols)");
                    
                    var history = await stockService.GetRealTimeDataAsync(batch, 1825); // 5 years
                    foreach (var (symbol, data) in history)
                    {
                        if (data != null && data.Count > 0)
                        {
                            _logger.LogInformation("Caching {Count} data points for symbol {Symbol}", data.Count, symbol);
                            var cacheOptions = new MemoryCacheEntryOptions()
                                .SetAbsoluteExpiration(TimeSpan.FromHours(24)) // Increased from 4 hours to 24 hours
                                .SetSlidingExpiration(TimeSpan.FromHours(12)); // Increased from 1 hour to 12 hours
                            _cache.Set($"History_{symbol}", data, cacheOptions);
                            
                            // Pre-calculate performance data for this symbol and cache it too
                            try
                            {
                                // Get all stocks for this symbol from database
                                using var scope = _services.CreateScope();
                                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                var stocks = await context.UserStocks
                                    .Where(s => s.StockSymbol == symbol && 
                                              (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0)
                                    .ToListAsync(stoppingToken);
                                    
                                if (stocks.Any())
                                {
                                    var tempHistoryDict = new Dictionary<string, List<StockDataPoint>>
                                    {
                                        { symbol, data }
                                    };
                                    
                                    // Use the controller method to calculate performance
                                    var controllerType = Type.GetType("FundWatch.Controllers.AppUserStocksController, FundWatch");
                                    if (controllerType != null)
                                    {
                                        var calculateMethod = controllerType.GetMethod("CalculatePerformanceData", 
                                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            
                                        if (calculateMethod != null)
                                        {
                                            var controller = scope.ServiceProvider.GetRequiredService<FundWatch.Controllers.AppUserStocksController>();
                                            var result = calculateMethod.Invoke(controller, new object[] { stocks, tempHistoryDict });
                                            
                                            // Use explicit Dictionary<string, List<PerformancePoint>> type for clarity
                                            if (result is Dictionary<string, List<PerformancePoint>> perfData && 
                                                perfData.ContainsKey(symbol) &&
                                                perfData[symbol] != null &&
                                                perfData[symbol].Count > 0)
                                            {
                                                _logger.LogInformation("Precalculated {Count} performance points for {Symbol}", 
                                                    perfData[symbol].Count, symbol);
                                                    
                                                _cache.Set($"Performance_{symbol}", perfData[symbol], cacheOptions);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception perfEx)
                            {
                                _logger.LogError(perfEx, "Error precalculating performance data for {Symbol}", symbol);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Received empty or null data for symbol {Symbol}", symbol);
                        }
                    }
                    
                    // Add delay between batches to respect API rate limits
                    if (i + batchSize < symbols.Count)
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating history cache");
            }
        }
    }
}