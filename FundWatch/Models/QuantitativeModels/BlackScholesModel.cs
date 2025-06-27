using System;

namespace FundWatch.Models.QuantitativeModels
{
    public class BlackScholesModel
    {
        public class OptionPriceResult
        {
            public double CallPrice { get; set; }
            public double PutPrice { get; set; }
            public OptionGreeks Greeks { get; set; }
            public double ImpliedVolatility { get; set; }
            public double TimeToExpiry { get; set; }
            public double IntrinsicValue { get; set; }
            public double TimeValue { get; set; }
            public string ProfitabilityStatus { get; set; } // In-the-money, At-the-money, Out-of-the-money
        }

        public class OptionGreeks
        {
            public double Delta { get; set; }  // Rate of change of option price with respect to stock price
            public double Gamma { get; set; }  // Rate of change of delta
            public double Theta { get; set; }  // Time decay
            public double Vega { get; set; }   // Sensitivity to volatility
            public double Rho { get; set; }    // Sensitivity to interest rate
        }

        public class OptionParameters
        {
            public double StockPrice { get; set; }      // S - Current stock price
            public double StrikePrice { get; set; }     // K - Strike price
            public double TimeToExpiry { get; set; }    // T - Time to expiry in years
            public double RiskFreeRate { get; set; }    // r - Risk-free rate
            public double Volatility { get; set; }      // σ - Volatility (standard deviation of returns)
            public double DividendYield { get; set; }   // q - Dividend yield
        }

        // Calculate option price using Black-Scholes formula
        public static OptionPriceResult CalculateOptionPrice(OptionParameters parameters)
        {
            var S = parameters.StockPrice;
            var K = parameters.StrikePrice;
            var T = parameters.TimeToExpiry;
            var r = parameters.RiskFreeRate;
            var sigma = parameters.Volatility;
            var q = parameters.DividendYield;

            // Handle edge cases
            if (T <= 0)
            {
                var intrinsicCall = Math.Max(S - K, 0);
                var intrinsicPut = Math.Max(K - S, 0);
                return new OptionPriceResult
                {
                    CallPrice = intrinsicCall,
                    PutPrice = intrinsicPut,
                    IntrinsicValue = Math.Max(intrinsicCall, intrinsicPut),
                    TimeValue = 0,
                    TimeToExpiry = 0,
                    ProfitabilityStatus = GetMoneyStatus(S, K),
                    Greeks = new OptionGreeks { Delta = intrinsicCall > 0 ? 1 : 0 }
                };
            }

            // Calculate d1 and d2
            var d1 = (Math.Log(S / K) + (r - q + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
            var d2 = d1 - sigma * Math.Sqrt(T);

            // Calculate option prices
            var callPrice = S * Math.Exp(-q * T) * NormalCDF(d1) - K * Math.Exp(-r * T) * NormalCDF(d2);
            var putPrice = K * Math.Exp(-r * T) * NormalCDF(-d2) - S * Math.Exp(-q * T) * NormalCDF(-d1);

            // Calculate Greeks
            var greeks = CalculateGreeks(S, K, T, r, sigma, q, d1, d2);

            // Calculate intrinsic and time values
            var callIntrinsic = Math.Max(S - K, 0);
            var putIntrinsic = Math.Max(K - S, 0);
            var callTimeValue = callPrice - callIntrinsic;
            var putTimeValue = putPrice - putIntrinsic;

            return new OptionPriceResult
            {
                CallPrice = callPrice,
                PutPrice = putPrice,
                Greeks = greeks,
                ImpliedVolatility = sigma,
                TimeToExpiry = T,
                IntrinsicValue = Math.Max(callIntrinsic, putIntrinsic),
                TimeValue = Math.Max(callTimeValue, putTimeValue),
                ProfitabilityStatus = GetMoneyStatus(S, K)
            };
        }

        private static OptionGreeks CalculateGreeks(double S, double K, double T, double r, double sigma, double q, double d1, double d2)
        {
            var sqrtT = Math.Sqrt(T);
            var pdf_d1 = NormalPDF(d1);
            var cdf_d1 = NormalCDF(d1);

            return new OptionGreeks
            {
                Delta = Math.Exp(-q * T) * cdf_d1, // Call delta
                Gamma = Math.Exp(-q * T) * pdf_d1 / (S * sigma * sqrtT),
                Theta = -(S * pdf_d1 * sigma * Math.Exp(-q * T)) / (2 * sqrtT) 
                        - r * K * Math.Exp(-r * T) * NormalCDF(d2) 
                        + q * S * Math.Exp(-q * T) * cdf_d1,
                Vega = S * Math.Exp(-q * T) * pdf_d1 * sqrtT / 100, // Divided by 100 for 1% change
                Rho = K * T * Math.Exp(-r * T) * NormalCDF(d2) / 100 // Divided by 100 for 1% change
            };
        }

        private static string GetMoneyStatus(double stockPrice, double strikePrice)
        {
            var moneyness = stockPrice / strikePrice;
            if (moneyness > 1.02) return "In-the-Money";
            if (moneyness < 0.98) return "Out-of-the-Money";
            return "At-the-Money";
        }

        // Standard normal cumulative distribution function
        private static double NormalCDF(double x)
        {
            return 0.5 * (1 + Erf(x / Math.Sqrt(2)));
        }

        // Standard normal probability density function
        private static double NormalPDF(double x)
        {
            return Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
        }

        // Error function approximation
        private static double Erf(double x)
        {
            // Abramowitz and Stegun approximation
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            var sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            var t = 1.0 / (1.0 + p * x);
            var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign * y;
        }

        // Calculate implied volatility using Newton-Raphson method
        public static double CalculateImpliedVolatility(double marketPrice, OptionParameters parameters, bool isCall, double tolerance = 0.0001, int maxIterations = 100)
        {
            var sigma = 0.3; // Initial guess
            var S = parameters.StockPrice;
            var K = parameters.StrikePrice;
            var T = parameters.TimeToExpiry;
            var r = parameters.RiskFreeRate;
            var q = parameters.DividendYield;

            for (int i = 0; i < maxIterations; i++)
            {
                parameters.Volatility = sigma;
                var result = CalculateOptionPrice(parameters);
                var theoreticalPrice = isCall ? result.CallPrice : result.PutPrice;
                var vega = result.Greeks.Vega * 100; // Adjust for percentage

                var diff = theoreticalPrice - marketPrice;
                if (Math.Abs(diff) < tolerance)
                    return sigma;

                if (Math.Abs(vega) < 1e-10)
                    break; // Avoid division by zero

                sigma -= diff / vega;
                sigma = Math.Max(0.001, Math.Min(5.0, sigma)); // Keep sigma in reasonable bounds
            }

            return sigma;
        }

        // Calculate volatility smile - returns strike prices and their implied volatilities
        public static List<(double Strike, double ImpliedVol)> CalculateVolatilitySmile(
            double stockPrice, 
            double timeToExpiry, 
            double riskFreeRate, 
            double baseVolatility,
            int numStrikes = 21)
        {
            var results = new List<(double, double)>();
            var strikeRange = 0.4; // 40% above and below current price

            for (int i = 0; i < numStrikes; i++)
            {
                var moneyness = 1 - strikeRange + (2 * strikeRange * i / (numStrikes - 1));
                var strike = stockPrice * moneyness;
                
                // Simulate volatility smile effect
                var skew = Math.Pow(moneyness - 1, 2) * 0.5; // Quadratic smile
                var impliedVol = baseVolatility * (1 + skew);
                
                results.Add((strike, impliedVol));
            }

            return results;
        }
    }
}