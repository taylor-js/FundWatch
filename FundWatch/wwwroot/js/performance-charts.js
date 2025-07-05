// Performance Tab Charts
var PerformanceCharts = (function() {
    'use strict';
    
    // Store chart instances for later reflow
    var chartInstances = {};
    
    // Store data for deferred rendering
    var chartData = null;
    var chartsInitialized = false;
    
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
        console.log('Initializing Performance tab charts...');
        
        if (!data || !data.hasOptimization) {
            console.log('Portfolio optimization is not available - need more historical data or stocks');
            return;
        }
        
        // Store data for later use
        chartData = data;
        
        // Check if tab is visible
        var performanceTabPane = document.getElementById('performanceTab');
        if (performanceTabPane && performanceTabPane.classList.contains('active')) {
            // Tab is visible, create charts immediately
            createChartsIfNeeded();
        } else {
            // Tab is not visible, defer chart creation
            console.log('Performance tab is not visible, deferring chart creation');
        }
    }
    
    function createChartsIfNeeded() {
        if (chartsInitialized || !chartData) {
            console.log('Charts already initialized or no data available');
            return;
        }
        
        console.log('Creating performance charts...');
        initializeChartsInternal(chartData);
        chartsInitialized = true;
    }
    
    function initializeChartsInternal(data) {
        
        var efficientFrontier = data.efficientFrontier;
        var currentPortfolio = data.currentPortfolio;
        var optimalPortfolio = data.optimalPortfolio;
        var monteCarloResults = data.monteCarloResults;
        var historicalPerformance = data.historicalPerformance;
        var optimization = data.optimization;
        
        console.log('Efficient Frontier data:', efficientFrontier);
        console.log('Current Portfolio:', currentPortfolio);
        console.log('Optimal Portfolio:', optimalPortfolio);
        console.log('Monte Carlo Results:', monteCarloResults);
        console.log('Historical Performance:', historicalPerformance);
        
        // 1. Efficient Frontier Chart
        if (document.getElementById('efficientFrontierChart') && efficientFrontier && efficientFrontier.length > 0) {
            var frontierData = efficientFrontier.map(function(p) {
                return {
                    x: p.Risk * 100,
                    y: p.Return * 100,
                    sharpe: p.SharpeRatio
                };
            });
            
            try {
                var efficientFrontierChartInstance = Highcharts.chart('efficientFrontierChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'scatter'
                },
                title: {
                    text: 'Risk vs Return Analysis'
                },
                xAxis: {
                    title: { text: 'Risk (Volatility %)' },
                    min: 0
                },
                yAxis: {
                    title: { text: 'Expected Return (%)' }
                },
                tooltip: {
                    formatter: function() {
                        if (this.series.name === 'Efficient Frontier') {
                            return '<b>Efficient Portfolio</b><br>' +
                                   'Risk: ' + this.x.toFixed(2) + '%<br>' +
                                   'Return: ' + this.y.toFixed(2) + '%<br>' +
                                   'Sharpe: ' + this.point.sharpe.toFixed(2);
                        }
                        return '<b>' + this.series.name + '</b><br>' +
                               'Risk: ' + this.x.toFixed(2) + '%<br>' +
                               'Return: ' + this.y.toFixed(2) + '%';
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
                    name: 'Efficient Frontier',
                    data: frontierData,
                    color: '#007bff',
                    lineWidth: 2,
                    marker: { radius: 3 },
                    type: 'line'
                }, {
                    name: 'Current Portfolio',
                    data: currentPortfolio && currentPortfolio.Risk !== undefined && currentPortfolio.Return !== undefined ? [{
                        x: currentPortfolio.Risk * 100,
                        y: currentPortfolio.Return * 100
                    }] : [],
                    color: '#dc3545',
                    marker: {
                        symbol: 'circle',
                        radius: 10
                    }
                }, {
                    name: 'Optimal Portfolio',
                    data: optimalPortfolio && optimalPortfolio.Risk !== undefined && optimalPortfolio.Return !== undefined ? [{
                        x: optimalPortfolio.Risk * 100,
                        y: optimalPortfolio.Return * 100
                    }] : [],
                    color: '#28a745',
                    marker: {
                        symbol: 'diamond',
                        radius: 12
                    }
                }],
                annotations: currentPortfolio && optimalPortfolio ? [{
                    labels: [{
                        point: {
                            x: currentPortfolio.Risk * 100,
                            y: currentPortfolio.Return * 100
                        },
                        text: 'Current'
                    }, {
                        point: {
                            x: optimalPortfolio.Risk * 100,
                            y: optimalPortfolio.Return * 100
                        },
                        text: 'Optimal'
                    }]
                }] : []
            }));
                chartInstances.efficientFrontierChart = efficientFrontierChartInstance;
                console.log('Efficient Frontier chart created successfully');
            } catch (error) {
                console.error('Error creating Efficient Frontier chart:', error);
            }
        }
        
        // 2. Monte Carlo Fan Chart
        if (document.getElementById('monteCarloChart') && monteCarloResults && monteCarloResults.length > 0) {
            // Extract percentile paths
            var p5 = monteCarloResults.find(function(r) { return r.Percentile >= 5; });
            var p25 = monteCarloResults.find(function(r) { return r.Percentile >= 25; });
            var p50 = monteCarloResults.find(function(r) { return r.Percentile >= 50; });
            var p75 = monteCarloResults.find(function(r) { return r.Percentile >= 75; });
            var p95 = monteCarloResults.find(function(r) { return r.Percentile >= 95; });
            
            // Create time series data
            var categories = [];
            if (p50 && p50.Path && p50.Path.length > 0) {
                var quarterCount = Math.ceil(p50.Path.length / 63);
                for (var q = 0; q < quarterCount; q++) {
                    categories.push('Q' + q);
                }
            }
            
            Highcharts.chart('monteCarloChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'arearange'
                },
                title: {
                    text: 'Portfolio Value Projections'
                },
                xAxis: {
                    categories: categories,
                    title: { text: 'Time (Quarters)' }
                },
                yAxis: {
                    title: { text: 'Portfolio Value ($)' }
                },
                tooltip: {
                    crosshairs: true,
                    shared: true,
                    valuePrefix: '$',
                    valueDecimals: 2
                },
                series: [{
                    name: '90% Confidence Interval',
                    data: categories.map(function(_, i) {
                        var idx = i * 63;
                        if (p5 && p5.Path && p95 && p95.Path) {
                            return [p5.Path[idx] || p5.Path[p5.Path.length - 1], 
                                    p95.Path[idx] || p95.Path[p95.Path.length - 1]];
                        }
                        return [0, 0];
                    }),
                    type: 'arearange',
                    lineWidth: 0,
                    color: '#28a745',
                    fillOpacity: 0.2
                }, {
                    name: '50% Confidence Interval',
                    data: categories.map(function(_, i) {
                        var idx = i * 63;
                        if (p25 && p25.Path && p75 && p75.Path) {
                            return [p25.Path[idx] || p25.Path[p25.Path.length - 1], 
                                    p75.Path[idx] || p75.Path[p75.Path.length - 1]];
                        }
                        return [0, 0];
                    }),
                    type: 'arearange',
                    lineWidth: 0,
                    color: '#28a745',
                    fillOpacity: 0.4
                }, {
                    name: 'Median Path',
                    data: categories.map(function(_, i) {
                        var idx = i * 63;
                        if (p50 && p50.Path) {
                            return p50.Path[idx] || p50.Path[p50.Path.length - 1];
                        }
                        return 0;
                    }),
                    type: 'line',
                    lineWidth: 3,
                    color: '#212529',
                    marker: { enabled: false }
                }]
            }));
        }
        
        // 3. Outcome Distribution
        if (document.getElementById('outcomeDistribution') && monteCarloResults && monteCarloResults.length > 0) {
            var finalValues = monteCarloResults.map(function(r) { return r.FinalValue; });
            var histogram = [];
            var bins = 20;
            var min = Math.min.apply(null, finalValues);
            var max = Math.max.apply(null, finalValues);
            var binWidth = (max - min) / bins;
            
            for (var i = 0; i < bins; i++) {
                var binMin = min + i * binWidth;
                var binMax = binMin + binWidth;
                var count = 0;
                finalValues.forEach(function(v) {
                    if (v >= binMin && v < binMax) {
                        count++;
                    }
                });
                histogram.push({
                    x: (binMin + binMax) / 2,
                    y: count
                });
            }
            
            Highcharts.chart('outcomeDistribution', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'column'
                },
                title: {
                    text: 'Final Value Distribution'
                },
                xAxis: {
                    title: { text: 'Portfolio Value ($)' }
                },
                yAxis: {
                    title: { text: 'Frequency' }
                },
                series: [{
                    name: 'Outcomes',
                    data: histogram,
                    color: '#17a2b8'
                }],
                legend: { enabled: false }
            }));
        }
        
        // 4. Risk Gauge Chart
        if (document.getElementById('riskGaugeChart') && optimization && optimization.RiskAnalysis && optimization.RiskAnalysis.ValueAtRisk95 !== undefined) {
            var riskScore = optimization.RiskAnalysis.ValueAtRisk95 * 100;
            
            Highcharts.chart('riskGaugeChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'gauge',
                    backgroundColor: '#ffffff'
                },
                title: {
                    text: 'Portfolio Risk Level'
                },
                pane: {
                    startAngle: -150,
                    endAngle: 150,
                    background: [{
                        backgroundColor: {
                            linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                            stops: [
                                [0, '#FFF'],
                                [1, '#333']
                            ]
                        },
                        borderWidth: 0,
                        outerRadius: '109%'
                    }, {
                        backgroundColor: {
                            linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                            stops: [
                                [0, '#333'],
                                [1, '#FFF']
                            ]
                        },
                        borderWidth: 1,
                        outerRadius: '107%'
                    }, {
                        backgroundColor: '#DDD',
                        borderWidth: 0,
                        outerRadius: '105%',
                        innerRadius: '103%'
                    }]
                },
                yAxis: {
                    min: 0,
                    max: 50,
                    minorTickInterval: 'auto',
                    minorTickWidth: 1,
                    minorTickLength: 10,
                    minorTickPosition: 'inside',
                    minorTickColor: '#666',
                    tickPixelInterval: 30,
                    tickWidth: 2,
                    tickPosition: 'inside',
                    tickLength: 10,
                    tickColor: '#666',
                    labels: {
                        step: 2,
                        rotation: 'auto',
                        style: {
                            color: '#333333'
                        }
                    },
                    title: {
                        text: 'VaR %',
                        style: {
                            color: '#333333'
                        }
                    },
                    plotBands: [{
                        from: 0,
                        to: 10,
                        color: '#55BF3B' // green
                    }, {
                        from: 10,
                        to: 20,
                        color: '#DDDF0D' // yellow
                    }, {
                        from: 20,
                        to: 30,
                        color: '#FFA500' // orange
                    }, {
                        from: 30,
                        to: 50,
                        color: '#DF5353' // red
                    }]
                },
                series: [{
                    name: 'Risk',
                    data: [Math.abs(riskScore)],
                    tooltip: {
                        valueSuffix: '%'
                    }
                }]
            }));
        }
        
        // 5. Historical Performance Chart
        if (document.getElementById('historicalPerformanceChart') && historicalPerformance && historicalPerformance.ActualPerformance && historicalPerformance.ActualPerformance.length > 0) {
            // Prepare data series with proper date parsing and validation
            var actualData = [];
            var optimalData = [];
            
            // Process actual performance data
            historicalPerformance.ActualPerformance.forEach(function(p) {
                var timestamp = new Date(p.Date).getTime();
                if (!isNaN(timestamp) && !isNaN(p.Value)) {
                    actualData.push({
                        x: timestamp,
                        y: p.Value
                    });
                }
            });
            
            // Process optimal performance data
            if (historicalPerformance.OptimalPerformance && historicalPerformance.OptimalPerformance.length > 0) {
                historicalPerformance.OptimalPerformance.forEach(function(p) {
                    var timestamp = new Date(p.Date).getTime();
                    if (!isNaN(timestamp) && !isNaN(p.Value)) {
                        optimalData.push({
                            x: timestamp,
                            y: p.Value
                        });
                    }
                });
            }
            
            // Sort data by date
            actualData.sort(function(a, b) { return a.x - b.x; });
            optimalData.sort(function(a, b) { return a.x - b.x; });
            
            // Only create chart if we have valid data
            if (actualData.length > 0 && optimalData.length > 0) {
                try {
                    var historicalChartInstance = Highcharts.chart('historicalPerformanceChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'line',
                    zoomType: 'x'
                },
                title: {
                    text: 'Portfolio Performance: Actual vs Optimal Strategy'
                },
                subtitle: {
                    text: 'What could have been earned with optimal allocation from ' + 
                          new Date(historicalPerformance.StartDate).toLocaleDateString()
                },
                xAxis: {
                    type: 'datetime',
                    title: { text: 'Date' }
                },
                yAxis: {
                    title: { text: 'Portfolio Value ($)' }
                },
                tooltip: {
                    shared: true,
                    crosshairs: true,
                    formatter: function() {
                        var date = new Date(this.x).toLocaleDateString();
                        var s = '<b>' + date + '</b>';
                        
                        this.points.forEach(function(point) {
                            var value = point.y.toFixed(2);
                            var actualPoint = historicalPerformance.ActualPerformance.find(function(p) {
                                return new Date(p.Date).getTime() === point.x;
                            });
                            var optimalPoint = historicalPerformance.OptimalPerformance.find(function(p) {
                                return new Date(p.Date).getTime() === point.x;
                            });
                            
                            if (point.series.name === 'Your Actual Strategy' && actualPoint) {
                                s += '<br/>' + point.series.name + ': $' + value + 
                                     ' (' + (actualPoint.CumulativeReturn * 100).toFixed(2) + '% return)';
                            } else if (point.series.name === 'Optimal Strategy' && optimalPoint) {
                                s += '<br/>' + point.series.name + ': $' + value + 
                                     ' (' + (optimalPoint.CumulativeReturn * 100).toFixed(2) + '% return)';
                            }
                        });
                        
                        return s;
                    }
                },
                plotOptions: {
                    line: {
                        marker: {
                            enabled: false
                        }
                    }
                },
                series: [{
                    name: 'Your Actual Strategy',
                    data: actualData,
                    color: '#dc3545',
                    lineWidth: 2
                }, {
                    name: 'Optimal Strategy',
                    data: optimalData,
                    color: '#28a745',
                    lineWidth: 2,
                    dashStyle: 'shortdash'
                }],
                annotations: [{
                    labelOptions: {
                        backgroundColor: 'rgba(255,255,255,0.8)',
                        borderColor: '#666',
                        borderRadius: 5
                    },
                    labels: [{
                        point: {
                            xAxis: 0,
                            yAxis: 0,
                            x: actualData[actualData.length - 1].x,
                            y: actualData[actualData.length - 1].y
                        },
                        text: 'Actual: $' + actualData[actualData.length - 1].y.toFixed(2)
                    }, {
                        point: {
                            xAxis: 0,
                            yAxis: 0,
                            x: optimalData[optimalData.length - 1].x,
                            y: optimalData[optimalData.length - 1].y
                        },
                        text: 'Optimal: $' + optimalData[optimalData.length - 1].y.toFixed(2)
                    }]
                }]
            }));
                    chartInstances.historicalPerformanceChart = historicalChartInstance;
                    console.log('Historical Performance chart created successfully');
                } catch (error) {
                    console.error('Error creating Historical Performance chart:', error);
                }
            } else {
                console.warn('Historical Performance chart has no valid data to display');
            }
        }
        
        // Force reflow of all charts after initialization
        setTimeout(function() {
            if (window.Highcharts && window.Highcharts.charts) {
                window.Highcharts.charts.forEach(function(chart) {
                    if (chart && chart.container && 
                        (chart.container.id === 'efficientFrontierChart' || 
                         chart.container.id === 'historicalPerformanceChart')) {
                        chart.reflow();
                        console.log('Reflowed chart: ' + chart.container.id);
                    }
                });
            }
        }, 250);
    }
    
    // Function to reflow all performance charts
    function reflowCharts() {
        console.log('Reflowing performance charts...');
        Object.keys(chartInstances).forEach(function(key) {
            if (chartInstances[key] && chartInstances[key].reflow) {
                chartInstances[key].reflow();
                console.log('Reflowed chart: ' + key);
            }
        });
    }
    
    // Public API
    return {
        initializeCharts: initializeCharts,
        reflowCharts: reflowCharts,
        createChartsIfNeeded: createChartsIfNeeded
    };
})();

// Make it available globally 
window.PerformanceCharts = PerformanceCharts;

// Make it available globally for backward compatibility
window.initializePerformanceCharts = function(data) {
    PerformanceCharts.initializeCharts(data);
};