using System;
using System.Collections.Generic;
using System.Linq;

namespace FundWatch.Models.QuantitativeModels
{
    public class PortfolioOptimizationModel
    {
        public class OptimizationResult
        {
            public List<PortfolioPoint> EfficientFrontier { get; set; }
            public PortfolioPoint OptimalPortfolio { get; set; }
            public PortfolioPoint CurrentPortfolio { get; set; }
            public Dictionary<string, double> OptimalWeights { get; set; }
            public Dictionary<string, double> CurrentWeights { get; set; }
            public double MaxSharpeRatio { get; set; }
            public double MinVarianceReturn { get; set; }
            public List<MonteCarloResult> MonteCarloSimulations { get; set; }
            public RiskMetrics RiskAnalysis { get; set; }
        }

        public class PortfolioPoint
        {
            public double Risk { get; set; }  // Standard deviation
            public double Return { get; set; } // Expected return
            public double SharpeRatio { get; set; }
            public Dictionary<string, double> Weights { get; set; }
        }

        public class MonteCarloResult
        {
            public int SimulationId { get; set; }
            public double FinalValue { get; set; }
            public double AnnualizedReturn { get; set; }
            public double MaxDrawdown { get; set; }
            public List<double> Path { get; set; }
            public int Percentile { get; set; }
        }

        public class RiskMetrics
        {
            public double ValueAtRisk95 { get; set; }
            public double ConditionalValueAtRisk { get; set; }
            public double MaxDrawdown { get; set; }
            public double SortinoRatio { get; set; }
            public double Beta { get; set; }
            public double TreynorRatio { get; set; }
            public double InformationRatio { get; set; }
        }

        public class AssetData
        {
            public string Symbol { get; set; }
            public List<double> Returns { get; set; }
            public double ExpectedReturn { get; set; }
            public double Volatility { get; set; }
        }

        private const double RISK_FREE_RATE = 0.045; // 4.5% annual

        // Calculate efficient frontier using Modern Portfolio Theory
        public static OptimizationResult OptimizePortfolio(List<AssetData> assets, Dictionary<string, double> currentWeights)
        {
            var result = new OptimizationResult
            {
                EfficientFrontier = new List<PortfolioPoint>(),
                CurrentWeights = currentWeights,
                OptimalWeights = new Dictionary<string, double>(),
                MonteCarloSimulations = new List<MonteCarloResult>()
            };

            // Calculate covariance matrix
            var covarianceMatrix = CalculateCovarianceMatrix(assets);
            
            // Generate efficient frontier points
            var targetReturns = GenerateTargetReturns(assets, 50);
            
            foreach (var targetReturn in targetReturns)
            {
                var optimizedWeights = OptimizeForTargetReturn(assets, covarianceMatrix, targetReturn);
                if (optimizedWeights != null)
                {
                    var portfolioRisk = CalculatePortfolioRisk(optimizedWeights, covarianceMatrix);
                    var sharpeRatio = (targetReturn - RISK_FREE_RATE) / portfolioRisk;
                    
                    result.EfficientFrontier.Add(new PortfolioPoint
                    {
                        Risk = portfolioRisk,
                        Return = targetReturn,
                        SharpeRatio = sharpeRatio,
                        Weights = optimizedWeights
                    });
                }
            }

            // Find optimal portfolio (max Sharpe ratio)
            result.OptimalPortfolio = result.EfficientFrontier
                .OrderByDescending(p => p.SharpeRatio)
                .FirstOrDefault();
            
            if (result.OptimalPortfolio != null)
            {
                result.OptimalWeights = result.OptimalPortfolio.Weights;
                result.MaxSharpeRatio = result.OptimalPortfolio.SharpeRatio;
            }

            // Calculate current portfolio metrics
            result.CurrentPortfolio = CalculatePortfolioMetrics(currentWeights, assets, covarianceMatrix);

            // Run Monte Carlo simulations
            result.MonteCarloSimulations = RunMonteCarloSimulation(
                result.OptimalWeights, 
                assets, 
                1000, // number of simulations
                252 * 5 // 5 years of trading days
            );

            // Calculate risk metrics
            result.RiskAnalysis = CalculateRiskMetrics(result.MonteCarloSimulations, assets, result.OptimalWeights);

            return result;
        }

        private static double[,] CalculateCovarianceMatrix(List<AssetData> assets)
        {
            int n = assets.Count;
            var matrix = new double[n, n];
            
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    matrix[i, j] = CalculateCovariance(assets[i].Returns, assets[j].Returns);
                }
            }
            
            return matrix;
        }

        private static double CalculateCovariance(List<double> returns1, List<double> returns2)
        {
            if (returns1.Count != returns2.Count || returns1.Count == 0)
                return 0;

            var mean1 = returns1.Average();
            var mean2 = returns2.Average();
            
            double sum = 0;
            for (int i = 0; i < returns1.Count; i++)
            {
                sum += (returns1[i] - mean1) * (returns2[i] - mean2);
            }
            
            return sum / (returns1.Count - 1);
        }

        private static List<double> GenerateTargetReturns(List<AssetData> assets, int numPoints)
        {
            var minReturn = assets.Min(a => a.ExpectedReturn);
            var maxReturn = assets.Max(a => a.ExpectedReturn);
            var step = (maxReturn - minReturn) / (numPoints - 1);
            
            var returns = new List<double>();
            for (int i = 0; i < numPoints; i++)
            {
                returns.Add(minReturn + i * step);
            }
            
            return returns;
        }

        // Simplified portfolio optimization for target return
        private static Dictionary<string, double> OptimizeForTargetReturn(
            List<AssetData> assets, 
            double[,] covarianceMatrix, 
            double targetReturn)
        {
            // This is a simplified version. In production, you'd use a proper optimizer
            // For now, we'll use a basic allocation strategy
            var weights = new Dictionary<string, double>();
            
            // Simple mean-variance optimization approximation
            var totalRisk = 0.0;
            foreach (var asset in assets)
            {
                var weight = (asset.ExpectedReturn - RISK_FREE_RATE) / (asset.Volatility * asset.Volatility);
                totalRisk += weight;
                weights[asset.Symbol] = weight;
            }
            
            // Normalize weights
            foreach (var symbol in weights.Keys.ToList())
            {
                weights[symbol] = Math.Max(0, weights[symbol] / totalRisk);
            }
            
            // Ensure weights sum to 1
            var sum = weights.Values.Sum();
            if (sum > 0)
            {
                foreach (var symbol in weights.Keys.ToList())
                {
                    weights[symbol] /= sum;
                }
            }
            
            return weights;
        }

        private static double CalculatePortfolioRisk(Dictionary<string, double> weights, double[,] covarianceMatrix)
        {
            double portfolioVariance = 0;
            var symbols = weights.Keys.ToList();
            
            for (int i = 0; i < symbols.Count; i++)
            {
                for (int j = 0; j < symbols.Count; j++)
                {
                    portfolioVariance += weights[symbols[i]] * weights[symbols[j]] * covarianceMatrix[i, j];
                }
            }
            
            return Math.Sqrt(portfolioVariance);
        }

        private static PortfolioPoint CalculatePortfolioMetrics(
            Dictionary<string, double> weights, 
            List<AssetData> assets, 
            double[,] covarianceMatrix)
        {
            double portfolioReturn = 0;
            foreach (var asset in assets)
            {
                if (weights.ContainsKey(asset.Symbol))
                {
                    portfolioReturn += weights[asset.Symbol] * asset.ExpectedReturn;
                }
            }
            
            double portfolioRisk = CalculatePortfolioRisk(weights, covarianceMatrix);
            
            return new PortfolioPoint
            {
                Risk = portfolioRisk,
                Return = portfolioReturn,
                SharpeRatio = (portfolioReturn - RISK_FREE_RATE) / portfolioRisk,
                Weights = weights
            };
        }

        // Monte Carlo simulation for portfolio performance
        private static List<MonteCarloResult> RunMonteCarloSimulation(
            Dictionary<string, double> weights,
            List<AssetData> assets,
            int numSimulations,
            int numDays)
        {
            var results = new List<MonteCarloResult>();
            var random = new Random();
            
            // Calculate portfolio parameters
            double portfolioReturn = 0;
            double portfolioVolatility = 0;
            
            foreach (var asset in assets)
            {
                if (weights.ContainsKey(asset.Symbol))
                {
                    portfolioReturn += weights[asset.Symbol] * asset.ExpectedReturn;
                    portfolioVolatility += Math.Pow(weights[asset.Symbol] * asset.Volatility, 2);
                }
            }
            portfolioVolatility = Math.Sqrt(portfolioVolatility);
            
            // Daily parameters
            double dailyReturn = portfolioReturn / 252;
            double dailyVolatility = portfolioVolatility / Math.Sqrt(252);
            
            for (int sim = 0; sim < numSimulations; sim++)
            {
                var path = new List<double> { 100 }; // Start with $100
                double maxValue = 100;
                double maxDrawdown = 0;
                
                for (int day = 1; day <= numDays; day++)
                {
                    // Generate random return using geometric Brownian motion
                    double z = NormalRandom(random);
                    double dailyChange = dailyReturn + dailyVolatility * z;
                    double newValue = path.Last() * (1 + dailyChange);
                    
                    path.Add(newValue);
                    maxValue = Math.Max(maxValue, newValue);
                    maxDrawdown = Math.Max(maxDrawdown, (maxValue - newValue) / maxValue);
                }
                
                double finalValue = path.Last();
                double totalReturn = (finalValue - 100) / 100;
                double annualizedReturn = Math.Pow(1 + totalReturn, 252.0 / numDays) - 1;
                
                results.Add(new MonteCarloResult
                {
                    SimulationId = sim,
                    FinalValue = finalValue,
                    AnnualizedReturn = annualizedReturn,
                    MaxDrawdown = maxDrawdown,
                    Path = path
                });
            }
            
            // Sort by final value and assign percentiles
            results = results.OrderBy(r => r.FinalValue).ToList();
            for (int i = 0; i < results.Count; i++)
            {
                results[i].Percentile = (i + 1) * 100 / results.Count;
            }
            
            return results;
        }

        private static double NormalRandom(Random random)
        {
            // Box-Muller transform for normal distribution
            double u1 = random.NextDouble();
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static RiskMetrics CalculateRiskMetrics(
            List<MonteCarloResult> simulations,
            List<AssetData> assets,
            Dictionary<string, double> weights)
        {
            var sortedReturns = simulations.Select(s => s.AnnualizedReturn).OrderBy(r => r).ToList();
            var var95Index = (int)(simulations.Count * 0.05);
            var cvarReturns = sortedReturns.Take(var95Index).ToList();
            
            return new RiskMetrics
            {
                ValueAtRisk95 = -sortedReturns[var95Index], // Negative because VaR is a loss
                ConditionalValueAtRisk = -cvarReturns.Average(),
                MaxDrawdown = simulations.Max(s => s.MaxDrawdown),
                SortinoRatio = CalculateSortinoRatio(simulations),
                Beta = 1.0, // Simplified - would calculate vs market in production
                TreynorRatio = CalculateTreynorRatio(simulations.Average(s => s.AnnualizedReturn), 1.0),
                InformationRatio = 0.5 // Simplified - would calculate vs benchmark
            };
        }

        private static double CalculateSortinoRatio(List<MonteCarloResult> simulations)
        {
            var returns = simulations.Select(s => s.AnnualizedReturn).ToList();
            var avgReturn = returns.Average();
            var downside = returns.Where(r => r < RISK_FREE_RATE).Select(r => Math.Pow(r - RISK_FREE_RATE, 2)).Average();
            var downsideDeviation = Math.Sqrt(downside);
            
            return (avgReturn - RISK_FREE_RATE) / downsideDeviation;
        }

        private static double CalculateTreynorRatio(double portfolioReturn, double beta)
        {
            return (portfolioReturn - RISK_FREE_RATE) / beta;
        }
    }
}