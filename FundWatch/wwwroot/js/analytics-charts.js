// Analytics Tab Charts
var AnalyticsCharts = (function() {
    'use strict';
    
    // Light theme configuration for charts
    var lightTheme = {
        chart: {
            backgroundColor: '#ffffff',
            borderRadius: 8,
            style: {
                fontFamily: 'Roboto, sans-serif'
            }
        },
        title: {
            style: {
                color: '#333333',
                fontSize: '18px',
                fontWeight: '600'
            }
        },
        subtitle: {
            style: {
                color: '#666666'
            }
        },
        xAxis: {
            gridLineColor: '#e0e0e0',
            labels: {
                style: {
                    color: '#333333'
                }
            },
            title: {
                style: {
                    color: '#333333',
                    fontSize: '14px'
                }
            },
            lineColor: '#cccccc'
        },
        yAxis: {
            gridLineColor: '#e0e0e0',
            labels: {
                style: {
                    color: '#333333'
                }
            },
            title: {
                style: {
                    color: '#333333',
                    fontSize: '14px'
                }
            }
        },
        tooltip: {
            backgroundColor: 'rgba(255, 255, 255, 0.95)',
            borderColor: '#cccccc',
            style: {
                color: '#333333'
            }
        },
        legend: {
            backgroundColor: '#ffffff',
            borderWidth: 0,
            itemStyle: {
                color: '#333333'
            },
            itemHoverStyle: {
                color: '#000000'
            }
        }
    };
    
    function initializeCharts(data) {
        console.log('Initializing Analytics tab charts...');
        
        if (!data.hasFourierAnalysis) {
            console.log('Fourier analysis is not available - need more historical data');
            return;
        }
        
        var powerSpectrum = data.powerSpectrum;
        var decomposition = data.decomposition;
        var predictions = data.predictions;
        var marketCycles = data.marketCycles;
        var correlations = data.correlations;
        var waveletLevels = data.waveletLevels;
        var historicalValidation = data.historicalValidation;
        
        console.log('Power Spectrum:', powerSpectrum);
        console.log('Decomposition:', decomposition);
        console.log('Predictions:', predictions);
        console.log('Market Cycles:', marketCycles);
        
        // 1. Power Spectrum Chart
        if (document.getElementById('powerSpectrumChart') && powerSpectrum && powerSpectrum.PowerSpectralDensity && powerSpectrum.PowerSpectralDensity.length > 0) {
            var spectrumData = [];
            for (var i = 0; i < powerSpectrum.PowerSpectralDensity.length; i++) {
                if (powerSpectrum.Frequencies && powerSpectrum.Frequencies[i] && powerSpectrum.Frequencies[i] !== 0) {
                    var dataPoint = {
                        x: 1 / powerSpectrum.Frequencies[i], // Convert to period
                        y: powerSpectrum.PowerSpectralDensity[i]
                    };
                    
                    if (powerSpectrum.SignificantPeaks && powerSpectrum.SignificantPeaks.includes(i)) {
                        dataPoint.marker = {
                            radius: 8,
                            fillColor: '#dc3545'
                        };
                    }
                    
                    spectrumData.push(dataPoint);
                }
            }
            
            Highcharts.chart('powerSpectrumChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'area',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Dominant Market Cycles'
                },
                xAxis: {
                    type: 'logarithmic',
                    title: { text: 'Period (Days)' },
                    min: 1,
                    max: 365
                },
                yAxis: {
                    title: { text: 'Power Spectral Density' }
                },
                tooltip: {
                    formatter: function() {
                        return '<b>Period:</b> ' + this.x.toFixed(1) + ' days<br>' +
                               '<b>Power:</b> ' + this.y.toFixed(4);
                    }
                },
                series: [{
                    name: 'Power Spectrum',
                    data: spectrumData,
                    color: '#007bff',
                    fillOpacity: 0.3
                }]
            }));
        }
        
        // 2. Decomposition Chart
        if (document.getElementById('decompositionChart') && decomposition && decomposition.Dates && decomposition.Dates.length > 0) {
            var dates = decomposition.Dates.map(function(d) { return new Date(d).getTime(); });
            
            Highcharts.chart('decompositionChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'line',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Time Series Components'
                },
                xAxis: {
                    type: 'datetime'
                },
                yAxis: {
                    title: { text: 'Value' }
                },
                tooltip: {
                    shared: true,
                    crosshairs: true
                },
                series: [{
                    name: 'Trend',
                    data: dates.map(function(d, i) { return [d, decomposition.Trend && decomposition.Trend[i] ? decomposition.Trend[i] : 0]; }),
                    color: '#28a745',
                    lineWidth: 3
                }, {
                    name: 'Seasonal',
                    data: dates.map(function(d, i) { return [d, decomposition.Seasonal && decomposition.Seasonal[i] ? decomposition.Seasonal[i] : 0]; }),
                    color: '#17a2b8',
                    lineWidth: 2
                }, {
                    name: 'Cyclical',
                    data: dates.map(function(d, i) { return [d, decomposition.Cyclical && decomposition.Cyclical[i] ? decomposition.Cyclical[i] : 0]; }),
                    color: '#ffc107',
                    lineWidth: 2
                }, {
                    name: 'Residual',
                    data: dates.map(function(d, i) { return [d, decomposition.Residual && decomposition.Residual[i] ? decomposition.Residual[i] : 0]; }),
                    color: '#6c757d',
                    lineWidth: 1,
                    opacity: 0.5
                }]
            }));
        }
        
        // 3. Fourier Prediction Chart
        if (document.getElementById('fourierPredictionChart') && predictions && predictions.length > 0) {
            var predictionData = predictions.map(function(p) {
                return {
                    x: new Date(p.Date).getTime(),
                    y: p.PredictedPrice,
                    high: p.UpperBound,
                    low: p.LowerBound,
                    confidence: p.Confidence
                };
            });
            
            Highcharts.chart('fourierPredictionChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'arearange',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Price Forecast Based on Cycles'
                },
                xAxis: {
                    type: 'datetime'
                },
                yAxis: {
                    title: { text: 'Predicted Price ($)' }
                },
                tooltip: {
                    crosshairs: true,
                    shared: true,
                    valuePrefix: '$'
                },
                series: [{
                    name: 'Confidence Range',
                    data: predictionData.map(function(p) { return [p.x, p.low, p.high]; }),
                    type: 'arearange',
                    lineWidth: 0,
                    color: '#17a2b8',
                    fillOpacity: 0.3
                }, {
                    name: 'Predicted Price',
                    data: predictionData.map(function(p) { return [p.x, p.y]; }),
                    color: '#212529',
                    lineWidth: 3,
                    marker: {
                        enabled: false
                    }
                }]
            }));
        }
        
        // 4. Phase Indicators for Cycles
        if (marketCycles && marketCycles.length > 0) {
            marketCycles.forEach(function(cycle) {
                var canvasId = 'phase-' + cycle.CycleName.replace(/\s/g, '');
                var canvas = document.getElementById(canvasId);
                if (canvas) {
                    var ctx = canvas.getContext('2d');
                    var centerX = canvas.width / 2;
                    var centerY = canvas.height / 2;
                    var radius = Math.min(canvas.height / 2 - 25, 45); // Use height for radius calculation, max 45px
                    
                    // Clear canvas and set white background
                    ctx.fillStyle = '#ffffff';
                    ctx.fillRect(0, 0, canvas.width, canvas.height);
                    
                    // Draw circle
                    ctx.beginPath();
                    ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#dee2e6';
                    ctx.lineWidth = 2;
                    ctx.stroke();
                    
                    // Add phase labels
                    ctx.font = '11px Arial';
                    ctx.fillStyle = '#495057';
                    ctx.textAlign = 'center';
                    ctx.textBaseline = 'middle';
                    
                    // Top - Peak (0°)
                    ctx.fillText('Peak', centerX, centerY - radius - 15);
                    
                    // Right - Declining (90°)
                    ctx.save();
                    ctx.textAlign = 'left';
                    ctx.fillText('Decline', centerX + radius + 12, centerY);
                    ctx.restore();
                    
                    // Bottom - Trough (180°)
                    ctx.fillText('Trough', centerX, centerY + radius + 15);
                    
                    // Left - Rising (270°)
                    ctx.save();
                    ctx.textAlign = 'right';
                    ctx.fillText('Rise', centerX - radius - 12, centerY);
                    ctx.restore();
                    
                    // Add tick marks at quarters
                    ctx.strokeStyle = '#dee2e6';
                    ctx.lineWidth = 1;
                    var tickLength = 5;
                    
                    // Top tick
                    ctx.beginPath();
                    ctx.moveTo(centerX, centerY - radius);
                    ctx.lineTo(centerX, centerY - radius + tickLength);
                    ctx.stroke();
                    
                    // Right tick
                    ctx.beginPath();
                    ctx.moveTo(centerX + radius, centerY);
                    ctx.lineTo(centerX + radius - tickLength, centerY);
                    ctx.stroke();
                    
                    // Bottom tick
                    ctx.beginPath();
                    ctx.moveTo(centerX, centerY + radius);
                    ctx.lineTo(centerX, centerY + radius - tickLength);
                    ctx.stroke();
                    
                    // Left tick
                    ctx.beginPath();
                    ctx.moveTo(centerX - radius, centerY);
                    ctx.lineTo(centerX - radius + tickLength, centerY);
                    ctx.stroke();
                    
                    // Draw phase indicator arrow
                    var phaseRad = cycle.CurrentPhase * Math.PI / 180;
                    ctx.beginPath();
                    ctx.moveTo(centerX, centerY);
                    ctx.lineTo(
                        centerX + radius * Math.cos(phaseRad - Math.PI / 2),
                        centerY + radius * Math.sin(phaseRad - Math.PI / 2)
                    );
                    ctx.strokeStyle = cycle.PhaseDescription.includes('Rising') ? '#28a745' : '#dc3545';
                    ctx.lineWidth = 3;
                    ctx.stroke();
                    
                    // Draw center dot
                    ctx.beginPath();
                    ctx.arc(centerX, centerY, 3, 0, 2 * Math.PI);
                    ctx.fillStyle = '#495057';
                    ctx.fill();
                    
                    // Draw phase dot at end of arrow
                    ctx.beginPath();
                    ctx.arc(
                        centerX + radius * Math.cos(phaseRad - Math.PI / 2),
                        centerY + radius * Math.sin(phaseRad - Math.PI / 2),
                        4, 0, 2 * Math.PI
                    );
                    ctx.fillStyle = cycle.PhaseDescription.includes('Rising') ? '#28a745' : '#dc3545';
                    ctx.fill();
                }
            });
        }
        
        // 5. Correlation Heatmap
        if (document.getElementById('correlationHeatmap') && correlations) {
            var heatmapData = [];
            
            if (correlations.Symbols && correlations.Matrix) {
                correlations.Symbols.forEach(function(sym1, i) {
                    correlations.Symbols.forEach(function(sym2, j) {
                        if (correlations.Matrix[i] && correlations.Matrix[i][j] !== undefined) {
                            heatmapData.push([i, j, correlations.Matrix[i][j]]);
                        }
                    });
                });
            }
            
            Highcharts.chart('correlationHeatmap', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'heatmap',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Stock Correlation Matrix'
                },
                xAxis: {
                    categories: correlations.Symbols,
                    opposite: true
                },
                yAxis: {
                    categories: correlations.Symbols,
                    reversed: true
                },
                colorAxis: {
                    min: -1,
                    max: 1,
                    stops: [
                        [0, '#dc3545'],
                        [0.5, '#ffffff'],
                        [1, '#28a745']
                    ],
                    labels: {
                        style: {
                            color: '#333333'
                        }
                    }
                },
                legend: {
                    align: 'right',
                    layout: 'vertical',
                    margin: 0,
                    verticalAlign: 'top',
                    y: 25,
                    symbolHeight: 280,
                    borderWidth: 0,
                    backgroundColor: 'transparent',
                    itemStyle: {
                        color: '#333333'
                    }
                },
                tooltip: {
                    formatter: function() {
                        return '<b>' + this.series.xAxis.categories[this.point.x] + '</b> - <b>' +
                               this.series.yAxis.categories[this.point.y] + '</b><br>' +
                               'Correlation: <b>' + (this.point.value * 100).toFixed(0) + '%</b>';
                    }
                },
                series: [{
                    name: 'Correlation',
                    data: heatmapData,
                    borderWidth: 1,
                    dataLabels: {
                        enabled: true,
                        color: '#000000',
                        formatter: function() {
                            return (this.point.value * 100).toFixed(0);
                        }
                    }
                }]
            }));
        }
        
        // 6. Wavelet Energy Chart
        if (document.getElementById('waveletEnergyChart') && waveletLevels && waveletLevels.length > 0) {
            Highcharts.chart('waveletEnergyChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'column',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Energy Distribution Across Time Scales'
                },
                xAxis: {
                    categories: waveletLevels.map(function(l) { return l.TimeScale; }),
                    title: { text: 'Time Scale' }
                },
                yAxis: {
                    title: { text: 'Energy' }
                },
                tooltip: {
                    formatter: function() {
                        return '<b>' + this.x + '</b><br>' +
                               'Energy: ' + this.y.toFixed(2) + '<br>' +
                               'Level: ' + (this.point.index + 1);
                    }
                },
                series: [{
                    name: 'Wavelet Energy',
                    data: waveletLevels.map(function(l) { return l.Energy; }),
                    colorByPoint: true,
                    colors: ['#007bff', '#28a745', '#ffc107', '#dc3545', '#6f42c1']
                }]
            }));
        }
        
        // 7. Historical Prediction Accuracy Chart
        if (document.getElementById('predictionAccuracyChart') && historicalValidation && historicalValidation.PastPredictions && historicalValidation.PastPredictions.length > 0) {
            var predictions = historicalValidation.PastPredictions;
            
            // Group predictions by date for visualization
            var accuracyData = predictions.map(function(p) {
                return {
                    x: new Date(p.TargetDate).getTime(),
                    y: Math.abs(p.Error) * 100,
                    color: p.WithinConfidenceInterval ? '#28a745' : '#dc3545',
                    predicted: p.PredictedValue,
                    actual: p.ActualValue
                };
            });
            
            Highcharts.chart('predictionAccuracyChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'scatter',
                    backgroundColor: '#ffffff',
                    zoomType: 'xy'
                },
                title: {
                    text: 'Prediction Error Over Time'
                },
                subtitle: {
                    text: 'Green: Within confidence interval, Red: Outside interval'
                },
                xAxis: {
                    type: 'datetime',
                    title: { text: 'Date' }
                },
                yAxis: {
                    title: { text: 'Prediction Error (%)' },
                    min: 0
                },
                tooltip: {
                    formatter: function() {
                        return '<b>' + new Date(this.x).toLocaleDateString() + '</b><br>' +
                               'Error: ' + this.y.toFixed(2) + '%<br>' +
                               'Predicted: $' + this.point.predicted.toFixed(2) + '<br>' +
                               'Actual: $' + this.point.actual.toFixed(2);
                    }
                },
                plotOptions: {
                    scatter: {
                        marker: {
                            radius: 5,
                            states: {
                                hover: {
                                    enabled: true,
                                    lineColor: 'rgb(100,100,100)'
                                }
                            }
                        }
                    }
                },
                series: [{
                    name: 'Prediction Errors',
                    data: accuracyData,
                    colorByPoint: true
                }]
            }));
        }
    }
    
    // Public API
    return {
        initializeCharts: initializeCharts
    };
})();

// Make it available globally
window.AnalyticsCharts = AnalyticsCharts;

// Also expose the function for backward compatibility
window.initializeAnalyticsCharts = function(data) {
    AnalyticsCharts.initializeCharts(data);
};