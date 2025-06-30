// Overview Tab Charts
var OverviewCharts = (function() {
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
            itemStyle: {
                color: '#333333'
            },
            itemHoverStyle: {
                color: '#000000'
            }
        },
        plotOptions: {
            series: {
                dataLabels: {
                    color: '#333333'
                }
            }
        }
    };
    
    function initializeCharts(data) {
        console.log('Initializing Overview tab charts...');
        console.log('Has options data:', data.hasOptionsData);
        
        if (!data.hasOptionsData) {
            console.log('No options data available');
            return;
        }
        
        var payoffData = data.payoffData;
        var greeksData = data.greeksData;
        var smileData = data.smileData;
        
        console.log('Chart data received:', {
            payoffData: payoffData,
            greeksData: greeksData,
            smileData: smileData
        });
        
        // 1. Options Payoff Diagram
        if (document.getElementById('optionsPayoffChart') && payoffData) {
            Highcharts.chart('optionsPayoffChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'line'
                },
                title: {
                    text: 'Options Payoff at Expiration'
                },
                subtitle: {
                    text: 'Strike: $' + payoffData.strikePrice.toFixed(2) + ' | Current: $' + payoffData.currentPrice.toFixed(2)
                },
                xAxis: {
                    title: { text: 'Stock Price at Expiration' },
                    plotLines: [{
                        color: '#2196F3',
                        width: 2,
                        value: payoffData.currentPrice,
                        label: {
                            text: 'Current Price',
                            align: 'center',
                            style: { color: '#2196F3', fontWeight: 'bold' }
                        }
                    }, {
                        color: '#FF9800',
                        width: 2,
                        value: payoffData.strikePrice,
                        dashStyle: 'dash',
                        label: {
                            text: 'Strike',
                            align: 'center',
                            style: { color: '#FF9800', fontWeight: 'bold' }
                        }
                    }]
                },
                yAxis: {
                    title: { text: 'Profit/Loss ($)' },
                    plotLines: [{
                        color: '#000',
                        width: 1,
                        value: 0
                    }]
                },
                tooltip: {
                    shared: true,
                    valueDecimals: 2,
                    valuePrefix: '$'
                },
                series: [{
                    name: 'Long Call',
                    data: payoffData.callPayoff.map(function(p) { return [p.x, p.y]; }),
                    color: '#4CAF50',
                    lineWidth: 3,
                    marker: {
                        enabled: false
                    }
                }, {
                    name: 'Long Put',
                    data: payoffData.putPayoff.map(function(p) { return [p.x, p.y]; }),
                    color: '#F44336',
                    lineWidth: 3,
                    marker: {
                        enabled: false
                    }
                }, {
                    name: 'Stock Position',
                    data: payoffData.stockReturns.map(function(p) { return [p.x, p.y]; }),
                    color: '#9E9E9E',
                    lineWidth: 2,
                    dashStyle: 'dash',
                    marker: {
                        enabled: false
                    }
                }],
                legend: {
                    layout: 'horizontal',
                    align: 'center',
                    verticalAlign: 'bottom'
                }
            }));
        }
        
        // 2. Greeks Radar Chart
        if (document.getElementById('greeksRadarChart') && greeksData) {
            Highcharts.chart('greeksRadarChart', Highcharts.merge(lightTheme, {
                chart: {
                    polar: true,
                    type: 'line'
                },
                title: {
                    text: 'Options Greeks Profile'
                },
                subtitle: {
                    text: 'Risk sensitivities for ATM options'
                },
                pane: {
                    size: '85%',
                    background: [{
                        backgroundColor: '#f8f8f8',
                        borderWidth: 0
                    }]
                },
                xAxis: {
                    categories: greeksData.categories,
                    tickmarkPlacement: 'on',
                    lineWidth: 0
                },
                yAxis: {
                    gridLineInterpolation: 'polygon',
                    lineWidth: 0,
                    min: -1,
                    max: 1,
                    gridLineColor: '#e0e0e0',
                    alternateGridColor: '#f5f5f5'
                },
                tooltip: {
                    shared: true,
                    pointFormat: '<span style="color:{series.color}">{series.name}: <b>{point.y:.3f}</b><br/>'
                },
                series: [{
                    name: 'Call Greeks',
                    data: greeksData.callGreeks,
                    color: '#4CAF50',
                    lineWidth: 2,
                    pointPlacement: 'on',
                    marker: {
                        symbol: 'circle',
                        radius: 6
                    }
                }, {
                    name: 'Put Greeks',
                    data: greeksData.putGreeks,
                    color: '#F44336',
                    lineWidth: 2,
                    pointPlacement: 'on',
                    marker: {
                        symbol: 'circle',
                        radius: 6
                    }
                }],
                legend: {
                    align: 'right',
                    verticalAlign: 'middle',
                    layout: 'vertical'
                }
            }));
        }
        
        // 3. Volatility Smile Chart
        if (document.getElementById('volatilitySmileChart') && smileData) {
            Highcharts.chart('volatilitySmileChart', Highcharts.merge(lightTheme, {
                chart: {
                    type: 'line'
                },
                title: {
                    text: 'Implied Volatility Smile'
                },
                xAxis: {
                    title: { text: 'Strike Price ($)' },
                    plotLines: [{
                        color: '#2196F3',
                        width: 2,
                        value: smileData.currentPrice,
                        label: {
                            text: 'Current Price',
                            align: 'center',
                            style: { color: '#2196F3', fontWeight: 'bold' }
                        }
                    }]
                },
                yAxis: {
                    title: { text: 'Implied Volatility (%)' },
                    min: 0
                },
                tooltip: {
                    valueSuffix: '%',
                    valueDecimals: 1
                },
                series: [{
                    name: 'Implied Volatility',
                    data: smileData.dataPoints.map(function(p) { return [p.strike, p.impliedVolatility]; }),
                    color: '#2196F3',
                    lineWidth: 3,
                    marker: {
                        enabled: true,
                        radius: 5,
                        fillColor: '#ffffff',
                        lineWidth: 2,
                        lineColor: '#2196F3'
                    }
                }],
                legend: {
                    enabled: false
                }
            }));
        }
        
        // Test Highcharts availability
        setTimeout(function() {
            console.log('Testing Highcharts availability...');
            console.log('Highcharts defined: ' + (typeof Highcharts !== 'undefined'));
            if (typeof Highcharts !== 'undefined') {
                console.log('Highcharts version: ' + Highcharts.version);
            }
        }, 1000);
    }
    
    // Public API
    return {
        initializeCharts: initializeCharts
    };
})();

// Make it available globally for backward compatibility
window.initializeOverviewCharts = function(data) {
    OverviewCharts.initializeCharts(data);
};