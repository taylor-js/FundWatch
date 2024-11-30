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
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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

            // Get all unique stock symbols from active positions
            var symbols = await context.UserStocks
                .Where(s => (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0)
                .Select(s => s.StockSymbol)
                .Distinct()
                .ToListAsync(stoppingToken);

            if (!symbols.Any()) return;

            // Update cache in batches
            foreach (var batch in symbols.Chunk(5))
            {
                try
                {
                    var prices = await stockService.GetRealTimePricesAsync(batch.ToList());
                    var details = await stockService.GetCompanyDetailsAsync(batch.ToList());
                    // Only fetch 90 days of historical data for the dashboard
                    var history = await stockService.GetRealTimeDataAsync(batch.ToList(), 90);

                    foreach (var symbol in batch)
                    {
                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
                            .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                        if (prices.TryGetValue(symbol, out var price))
                        {
                            _cache.Set($"Price_{symbol}", price, cacheOptions);
                        }

                        if (details.TryGetValue(symbol, out var detail))
                        {
                            _cache.Set($"Details_{symbol}", detail, cacheOptions);
                        }

                        if (history.TryGetValue(symbol, out var data))
                        {
                            _cache.Set($"History_{symbol}", data, cacheOptions);
                        }
                    }

                    _logger.LogInformation("Updated cache for batch of {Count} symbols", batch.Count());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating cache for batch");
                }

                await Task.Delay(200, stoppingToken); // Rate limiting between batches
            }
        }
    }
}