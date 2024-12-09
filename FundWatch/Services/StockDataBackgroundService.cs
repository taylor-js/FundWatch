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
                    await Task.Delay(TimeSpan.FromHours(4), stoppingToken);
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

            // Get all unique stock symbols from active positions with a single query
            var symbols = await context.UserStocks
                .Where(s => s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0) > 0)
                .Select(s => s.StockSymbol)
                .Distinct()
                .ToListAsync(stoppingToken);

            if (!symbols.Any()) return;

            // Process all data types in parallel
            var tasks = new[]
            {
                UpdatePriceCache(stockService, symbols, stoppingToken),
                UpdateCompanyDetailsCache(stockService, symbols, stoppingToken),
                UpdateHistoryCache(stockService, symbols, stoppingToken)
            };

            await Task.WhenAll(tasks);
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
                var history = await stockService.GetRealTimeDataAsync(symbols, 90);
                foreach (var (symbol, data) in history)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(4))
                        .SetSlidingExpiration(TimeSpan.FromHours(1));
                    _cache.Set($"History_{symbol}", data, cacheOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating history cache");
            }
        }
    }
}