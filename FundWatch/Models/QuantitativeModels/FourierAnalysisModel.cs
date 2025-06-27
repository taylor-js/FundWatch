using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FundWatch.Models.QuantitativeModels
{
    public class FourierAnalysisModel
    {
        public class FourierAnalysisResult
        {
            public List<FrequencyComponent> DominantFrequencies { get; set; }
            public List<CycleAnalysis> MarketCycles { get; set; }
            public SpectralAnalysis PowerSpectrum { get; set; }
            public PriceDecomposition Decomposition { get; set; }
            public List<PredictionPoint> FourierPrediction { get; set; }
            public CorrelationMatrix CrossCorrelations { get; set; }
            public WaveletAnalysis WaveletTransform { get; set; }
        }

        public class FrequencyComponent
        {
            public double Frequency { get; set; }
            public double Period { get; set; } // In days
            public double Amplitude { get; set; }
            public double Phase { get; set; }
            public double Power { get; set; }
            public string CycleType { get; set; } // Daily, Weekly, Monthly, Quarterly, Annual
            public double SignificanceLevel { get; set; }
        }

        public class CycleAnalysis
        {
            public string CycleName { get; set; }
            public double PeriodDays { get; set; }
            public double Strength { get; set; }
            public DateTime NextPeak { get; set; }
            public DateTime NextTrough { get; set; }
            public double CurrentPhase { get; set; }
            public string PhaseDescription { get; set; }
        }

        public class SpectralAnalysis
        {
            public List<double> Frequencies { get; set; }
            public List<double> PowerSpectralDensity { get; set; }
            public double TotalPower { get; set; }
            public double NoiseFloor { get; set; }
            public List<int> SignificantPeaks { get; set; }
        }

        public class PriceDecomposition
        {
            public List<double> Trend { get; set; }
            public List<double> Seasonal { get; set; }
            public List<double> Cyclical { get; set; }
            public List<double> Residual { get; set; }
            public List<DateTime> Dates { get; set; }
        }

        public class PredictionPoint
        {
            public DateTime Date { get; set; }
            public double PredictedPrice { get; set; }
            public double UpperBound { get; set; }
            public double LowerBound { get; set; }
            public double Confidence { get; set; }
        }

        public class CorrelationMatrix
        {
            public List<string> Symbols { get; set; }
            public double[,] Matrix { get; set; }
            public List<SymbolPair> StrongCorrelations { get; set; }
            public List<SymbolPair> LeadLagRelationships { get; set; }
        }

        public class SymbolPair
        {
            public string Symbol1 { get; set; }
            public string Symbol2 { get; set; }
            public double Correlation { get; set; }
            public int LagDays { get; set; }
            public string Relationship { get; set; }
        }

        public class WaveletAnalysis
        {
            public List<WaveletLevel> Levels { get; set; }
            public List<double> Scalogram { get; set; }
            public List<TurningPoint> DetectedTurningPoints { get; set; }
        }

        public class WaveletLevel
        {
            public int Level { get; set; }
            public string TimeScale { get; set; }
            public List<double> Coefficients { get; set; }
            public double Energy { get; set; }
        }

        public class TurningPoint
        {
            public DateTime Date { get; set; }
            public string Type { get; set; } // Peak or Trough
            public double Confidence { get; set; }
            public string TimeScale { get; set; }
        }

        // Perform Fast Fourier Transform on price data
        public static FourierAnalysisResult AnalyzePriceCycles(List<double> prices, List<DateTime> dates)
        {
            var result = new FourierAnalysisResult
            {
                DominantFrequencies = new List<FrequencyComponent>(),
                MarketCycles = new List<CycleAnalysis>(),
                FourierPrediction = new List<PredictionPoint>()
            };

            // Detrend the data
            var detrendedPrices = DetrendData(prices);
            
            // Perform FFT
            var fftResult = PerformFFT(detrendedPrices);
            
            // Extract dominant frequencies
            result.DominantFrequencies = ExtractDominantFrequencies(fftResult, dates.Count);
            
            // Identify market cycles
            result.MarketCycles = IdentifyMarketCycles(result.DominantFrequencies, dates.Last());
            
            // Calculate power spectrum
            result.PowerSpectrum = CalculatePowerSpectrum(fftResult);
            
            // Decompose price series
            result.Decomposition = DecomposeTimeSeries(prices, dates, result.DominantFrequencies);
            
            // Generate predictions
            result.FourierPrediction = GenerateFourierPrediction(
                result.DominantFrequencies, 
                prices.Last(), 
                dates.Last(), 
                30 // 30 days forecast
            );
            
            // Perform wavelet analysis
            result.WaveletTransform = PerformWaveletAnalysis(prices, dates);

            return result;
        }

        private static List<double> DetrendData(List<double> data)
        {
            // Simple linear detrending
            int n = data.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            
            for (int i = 0; i < n; i++)
            {
                sumX += i;
                sumY += data[i];
                sumXY += i * data[i];
                sumX2 += i * i;
            }
            
            double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double intercept = (sumY - slope * sumX) / n;
            
            var detrended = new List<double>();
            for (int i = 0; i < n; i++)
            {
                detrended.Add(data[i] - (slope * i + intercept));
            }
            
            return detrended;
        }

        private static Complex[] PerformFFT(List<double> data)
        {
            // Pad to next power of 2
            int n = 1;
            while (n < data.Count) n *= 2;
            
            var complexData = new Complex[n];
            for (int i = 0; i < data.Count; i++)
            {
                complexData[i] = new Complex(data[i], 0);
            }
            for (int i = data.Count; i < n; i++)
            {
                complexData[i] = new Complex(0, 0);
            }
            
            // Cooley-Tukey FFT algorithm
            FFTRecursive(complexData);
            
            return complexData;
        }

        private static void FFTRecursive(Complex[] data)
        {
            int n = data.Length;
            if (n <= 1) return;
            
            // Divide
            var even = new Complex[n / 2];
            var odd = new Complex[n / 2];
            
            for (int i = 0; i < n / 2; i++)
            {
                even[i] = data[2 * i];
                odd[i] = data[2 * i + 1];
            }
            
            // Conquer
            FFTRecursive(even);
            FFTRecursive(odd);
            
            // Combine
            for (int k = 0; k < n / 2; k++)
            {
                Complex t = Complex.Exp(-2 * Math.PI * Complex.ImaginaryOne * k / n) * odd[k];
                data[k] = even[k] + t;
                data[k + n / 2] = even[k] - t;
            }
        }

        private static List<FrequencyComponent> ExtractDominantFrequencies(Complex[] fftResult, int originalLength)
        {
            var frequencies = new List<FrequencyComponent>();
            int n = fftResult.Length;
            
            // Calculate magnitude spectrum
            var magnitudes = new double[n / 2];
            var avgMagnitude = 0.0;
            
            for (int i = 1; i < n / 2; i++)
            {
                magnitudes[i] = fftResult[i].Magnitude / (n / 2);
                avgMagnitude += magnitudes[i];
            }
            avgMagnitude /= (n / 2 - 1);
            
            // Find peaks (frequencies with magnitude > 2 * average)
            double threshold = avgMagnitude * 2;
            
            for (int i = 1; i < n / 2; i++)
            {
                if (magnitudes[i] > threshold && 
                    (i == 1 || magnitudes[i] > magnitudes[i - 1]) &&
                    (i == n / 2 - 1 || magnitudes[i] > magnitudes[i + 1]))
                {
                    double frequency = (double)i / n;
                    double period = originalLength / (double)i;
                    
                    frequencies.Add(new FrequencyComponent
                    {
                        Frequency = frequency,
                        Period = period,
                        Amplitude = magnitudes[i],
                        Phase = fftResult[i].Phase,
                        Power = magnitudes[i] * magnitudes[i],
                        CycleType = ClassifyCycle(period),
                        SignificanceLevel = magnitudes[i] / avgMagnitude
                    });
                }
            }
            
            return frequencies.OrderByDescending(f => f.Amplitude).Take(10).ToList();
        }

        private static string ClassifyCycle(double periodDays)
        {
            if (periodDays <= 1.5) return "Intraday";
            if (periodDays <= 5.5) return "Weekly";
            if (periodDays <= 25) return "Monthly";
            if (periodDays <= 70) return "Quarterly";
            if (periodDays <= 200) return "Semi-Annual";
            return "Annual";
        }

        private static List<CycleAnalysis> IdentifyMarketCycles(List<FrequencyComponent> frequencies, DateTime currentDate)
        {
            var cycles = new List<CycleAnalysis>();
            
            foreach (var freq in frequencies.Take(5)) // Top 5 frequencies
            {
                var phase = freq.Phase;
                var periodDays = freq.Period;
                
                // Calculate next peak and trough
                var daysToNextPeak = ((Math.PI / 2 - phase) / (2 * Math.PI)) * periodDays;
                if (daysToNextPeak < 0) daysToNextPeak += periodDays;
                
                var daysToNextTrough = ((3 * Math.PI / 2 - phase) / (2 * Math.PI)) * periodDays;
                if (daysToNextTrough < 0) daysToNextTrough += periodDays;
                
                // Current phase description
                var phaseNormalized = phase % (2 * Math.PI);
                if (phaseNormalized < 0) phaseNormalized += 2 * Math.PI;
                
                string phaseDesc;
                if (phaseNormalized < Math.PI / 2) phaseDesc = "Rising - Early Stage";
                else if (phaseNormalized < Math.PI) phaseDesc = "Rising - Late Stage";
                else if (phaseNormalized < 3 * Math.PI / 2) phaseDesc = "Falling - Early Stage";
                else phaseDesc = "Falling - Late Stage";
                
                cycles.Add(new CycleAnalysis
                {
                    CycleName = $"{freq.CycleType} Cycle ({periodDays:F1} days)",
                    PeriodDays = periodDays,
                    Strength = freq.SignificanceLevel,
                    NextPeak = currentDate.AddDays(daysToNextPeak),
                    NextTrough = currentDate.AddDays(daysToNextTrough),
                    CurrentPhase = phaseNormalized * 180 / Math.PI, // Convert to degrees
                    PhaseDescription = phaseDesc
                });
            }
            
            return cycles;
        }

        private static SpectralAnalysis CalculatePowerSpectrum(Complex[] fftResult)
        {
            var analysis = new SpectralAnalysis
            {
                Frequencies = new List<double>(),
                PowerSpectralDensity = new List<double>(),
                SignificantPeaks = new List<int>()
            };
            
            int n = fftResult.Length;
            double totalPower = 0;
            
            for (int i = 0; i < n / 2; i++)
            {
                double freq = (double)i / n;
                double psd = fftResult[i].Magnitude * fftResult[i].Magnitude / n;
                
                analysis.Frequencies.Add(freq);
                analysis.PowerSpectralDensity.Add(psd);
                totalPower += psd;
            }
            
            analysis.TotalPower = totalPower;
            analysis.NoiseFloor = analysis.PowerSpectralDensity.Average();
            
            // Find significant peaks
            double threshold = analysis.NoiseFloor * 3;
            for (int i = 1; i < analysis.PowerSpectralDensity.Count - 1; i++)
            {
                if (analysis.PowerSpectralDensity[i] > threshold &&
                    analysis.PowerSpectralDensity[i] > analysis.PowerSpectralDensity[i - 1] &&
                    analysis.PowerSpectralDensity[i] > analysis.PowerSpectralDensity[i + 1])
                {
                    analysis.SignificantPeaks.Add(i);
                }
            }
            
            return analysis;
        }

        private static PriceDecomposition DecomposeTimeSeries(List<double> prices, List<DateTime> dates, List<FrequencyComponent> frequencies)
        {
            var decomposition = new PriceDecomposition
            {
                Trend = new List<double>(),
                Seasonal = new List<double>(),
                Cyclical = new List<double>(),
                Residual = new List<double>(),
                Dates = dates
            };
            
            // Simple moving average for trend (long-term)
            int trendWindow = Math.Min(200, prices.Count / 4);
            for (int i = 0; i < prices.Count; i++)
            {
                int start = Math.Max(0, i - trendWindow / 2);
                int end = Math.Min(prices.Count, i + trendWindow / 2);
                decomposition.Trend.Add(prices.Skip(start).Take(end - start).Average());
            }
            
            // Reconstruct cyclical components
            for (int i = 0; i < prices.Count; i++)
            {
                double seasonal = 0;
                double cyclical = 0;
                
                foreach (var freq in frequencies)
                {
                    double t = i;
                    double value = freq.Amplitude * Math.Cos(2 * Math.PI * freq.Frequency * t + freq.Phase);
                    
                    if (freq.Period < 30) // Short-term seasonal
                        seasonal += value;
                    else // Longer-term cyclical
                        cyclical += value;
                }
                
                decomposition.Seasonal.Add(seasonal);
                decomposition.Cyclical.Add(cyclical);
                decomposition.Residual.Add(prices[i] - decomposition.Trend[i] - seasonal - cyclical);
            }
            
            return decomposition;
        }

        private static List<PredictionPoint> GenerateFourierPrediction(
            List<FrequencyComponent> frequencies, 
            double lastPrice, 
            DateTime lastDate, 
            int daysAhead)
        {
            var predictions = new List<PredictionPoint>();
            
            for (int day = 1; day <= daysAhead; day++)
            {
                double prediction = lastPrice;
                double variance = 0;
                
                // Sum contributions from each frequency component
                foreach (var freq in frequencies)
                {
                    double t = day;
                    double contribution = freq.Amplitude * Math.Cos(2 * Math.PI * freq.Frequency * t + freq.Phase);
                    prediction += contribution;
                    variance += freq.Amplitude * freq.Amplitude * 0.1; // Simplified variance
                }
                
                double stdDev = Math.Sqrt(variance);
                double confidence = Math.Max(0, 1 - (day / 30.0) * 0.3); // Confidence decreases with time
                
                predictions.Add(new PredictionPoint
                {
                    Date = lastDate.AddDays(day),
                    PredictedPrice = prediction,
                    UpperBound = prediction + 2 * stdDev,
                    LowerBound = prediction - 2 * stdDev,
                    Confidence = confidence
                });
            }
            
            return predictions;
        }

        private static WaveletAnalysis PerformWaveletAnalysis(List<double> prices, List<DateTime> dates)
        {
            var analysis = new WaveletAnalysis
            {
                Levels = new List<WaveletLevel>(),
                DetectedTurningPoints = new List<TurningPoint>()
            };
            
            // Simplified discrete wavelet transform using Haar wavelets
            var currentLevel = prices.ToList();
            int maxLevels = (int)Math.Log2(prices.Count);
            
            for (int level = 1; level <= Math.Min(5, maxLevels); level++)
            {
                var coefficients = new List<double>();
                var approximation = new List<double>();
                
                for (int i = 0; i < currentLevel.Count - 1; i += 2)
                {
                    double avg = (currentLevel[i] + currentLevel[i + 1]) / 2;
                    double diff = (currentLevel[i] - currentLevel[i + 1]) / 2;
                    
                    approximation.Add(avg);
                    coefficients.Add(diff);
                }
                
                double energy = coefficients.Sum(c => c * c);
                
                analysis.Levels.Add(new WaveletLevel
                {
                    Level = level,
                    TimeScale = GetTimeScale(level),
                    Coefficients = coefficients,
                    Energy = energy
                });
                
                // Detect turning points from wavelet coefficients
                DetectTurningPoints(coefficients, level, dates, analysis.DetectedTurningPoints);
                
                currentLevel = approximation;
            }
            
            return analysis;
        }

        private static string GetTimeScale(int level)
        {
            int days = (int)Math.Pow(2, level);
            if (days <= 2) return "Short-term (1-2 days)";
            if (days <= 8) return "Weekly";
            if (days <= 32) return "Monthly";
            return "Long-term";
        }

        private static void DetectTurningPoints(List<double> coefficients, int level, List<DateTime> dates, List<TurningPoint> turningPoints)
        {
            double threshold = coefficients.Select(Math.Abs).Average() * 2;
            int scaleFactor = (int)Math.Pow(2, level);
            
            for (int i = 1; i < coefficients.Count - 1; i++)
            {
                if (Math.Abs(coefficients[i]) > threshold)
                {
                    bool isPeak = coefficients[i] > coefficients[i - 1] && coefficients[i] > coefficients[i + 1];
                    bool isTrough = coefficients[i] < coefficients[i - 1] && coefficients[i] < coefficients[i + 1];
                    
                    if (isPeak || isTrough)
                    {
                        int dateIndex = i * scaleFactor;
                        if (dateIndex < dates.Count)
                        {
                            turningPoints.Add(new TurningPoint
                            {
                                Date = dates[dateIndex],
                                Type = isPeak ? "Peak" : "Trough",
                                Confidence = Math.Abs(coefficients[i]) / threshold,
                                TimeScale = GetTimeScale(level)
                            });
                        }
                    }
                }
            }
        }

        // Analyze correlations between multiple stocks
        public static CorrelationMatrix AnalyzePortfolioCorrelations(Dictionary<string, List<double>> stockReturns)
        {
            var symbols = stockReturns.Keys.ToList();
            int n = symbols.Count;
            var matrix = new double[n, n];
            var strongCorrelations = new List<SymbolPair>();
            var leadLagRelations = new List<SymbolPair>();
            
            // Calculate correlation matrix
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        matrix[i, j] = 1.0;
                    }
                    else
                    {
                        matrix[i, j] = CalculateCorrelation(stockReturns[symbols[i]], stockReturns[symbols[j]]);
                        
                        // Check for strong correlations
                        if (Math.Abs(matrix[i, j]) > 0.7 && i < j)
                        {
                            strongCorrelations.Add(new SymbolPair
                            {
                                Symbol1 = symbols[i],
                                Symbol2 = symbols[j],
                                Correlation = matrix[i, j],
                                Relationship = matrix[i, j] > 0 ? "Positive" : "Negative"
                            });
                        }
                    }
                }
            }
            
            // Check for lead-lag relationships
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var leadLag = FindLeadLagRelationship(stockReturns[symbols[i]], stockReturns[symbols[j]]);
                    if (leadLag != null && Math.Abs(leadLag.Correlation) > 0.6)
                    {
                        leadLag.Symbol1 = symbols[i];
                        leadLag.Symbol2 = symbols[j];
                        leadLagRelations.Add(leadLag);
                    }
                }
            }
            
            return new CorrelationMatrix
            {
                Symbols = symbols,
                Matrix = matrix,
                StrongCorrelations = strongCorrelations,
                LeadLagRelationships = leadLagRelations
            };
        }

        private static double CalculateCorrelation(List<double> x, List<double> y)
        {
            if (x.Count != y.Count || x.Count == 0) return 0;
            
            double meanX = x.Average();
            double meanY = y.Average();
            
            double covariance = 0;
            double varX = 0;
            double varY = 0;
            
            for (int i = 0; i < x.Count; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                
                covariance += dx * dy;
                varX += dx * dx;
                varY += dy * dy;
            }
            
            if (varX == 0 || varY == 0) return 0;
            
            return covariance / Math.Sqrt(varX * varY);
        }

        private static SymbolPair FindLeadLagRelationship(List<double> x, List<double> y)
        {
            double maxCorr = 0;
            int bestLag = 0;
            
            // Check lags from -10 to +10 days
            for (int lag = -10; lag <= 10; lag++)
            {
                if (lag == 0) continue;
                
                var xLagged = lag > 0 ? x.Skip(lag).ToList() : x.Take(x.Count + lag).ToList();
                var yLagged = lag > 0 ? y.Take(y.Count - lag).ToList() : y.Skip(-lag).ToList();
                
                if (xLagged.Count > 10)
                {
                    double corr = CalculateCorrelation(xLagged, yLagged);
                    if (Math.Abs(corr) > Math.Abs(maxCorr))
                    {
                        maxCorr = corr;
                        bestLag = lag;
                    }
                }
            }
            
            if (Math.Abs(maxCorr) > 0.6)
            {
                return new SymbolPair
                {
                    Correlation = maxCorr,
                    LagDays = bestLag,
                    Relationship = bestLag > 0 ? "Leads by" : "Lags by"
                };
            }
            
            return null;
        }
    }
}