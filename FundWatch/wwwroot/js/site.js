// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Check if Highcharts is loaded and configure it
function configureHighcharts() {
    // Make sure Highcharts is defined
    if (typeof Highcharts === 'undefined') {
        console.error("Highcharts is not loaded!");
        // Try again after a delay
        setTimeout(configureHighcharts, 500);
        return;
    }
    
    console.log("Configuring Highcharts:", Highcharts.version);
    
    // Set up dark theme for all Highcharts
    Highcharts.setOptions({
        chart: {
            backgroundColor: '#212529',
            style: {
                fontFamily: '"Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
            },
            height: null,
            spacingBottom: 15,
            spacingTop: 15,
            spacingLeft: 10,
            spacingRight: 10
        },
        title: {
            style: {
                color: '#E0E0E3',
                fontSize: '16px',
                fontWeight: 'bold'
            }
        },
        subtitle: {
            style: {
                color: '#E0E0E3',
                fontSize: '12px'
            }
        },
        credits: {
            enabled: false
        },
        tooltip: {
            backgroundColor: 'rgba(0, 0, 0, 0.85)',
            style: {
                color: '#F0F0F0'
            }
        },
        xAxis: {
            gridLineColor: '#505053',
            labels: {
                style: {
                    color: '#E0E0E3'
                }
            },
            lineColor: '#707073',
            minorGridLineColor: '#505053',
            tickColor: '#707073',
            title: {
                style: {
                    color: '#A0A0A3'
                }
            }
        },
        yAxis: {
            gridLineColor: '#505053',
            labels: {
                style: {
                    color: '#E0E0E3'
                }
            },
            lineColor: '#707073',
            minorGridLineColor: '#505053',
            tickColor: '#707073',
            title: {
                style: {
                    color: '#A0A0A3'
                }
            }
        },
        colors: ['#2b908f', '#90ee7e', '#f45b5b', '#7798BF', '#aaeeee', '#ff0066', '#eeaaee',
            '#55BF3B', '#DF5353', '#7798BF', '#aaeeee'],
        legend: {
            itemStyle: {
                color: '#E0E0E3'
            },
            itemHoverStyle: {
                color: '#FFF'
            },
            itemHiddenStyle: {
                color: '#606063'
            },
            backgroundColor: 'rgba(0, 0, 0, 0.5)',
            borderColor: '#606063',
            borderWidth: 1
        },
        plotOptions: {
            series: {
                dataLabels: {
                    color: '#F0F0F3'
                },
                marker: {
                    lineColor: '#333'
                }
            },
            boxplot: {
                fillColor: '#505053'
            },
            candlestick: {
                lineColor: 'white'
            },
            errorbar: {
                color: 'white'
            }
        }
    });
}

// Call the configuration on load
document.addEventListener('DOMContentLoaded', configureHighcharts);

// Chart rendering functions
const ChartManager = {
    // Utility function to handle window resize for all charts
    setupChartResponsiveness: function() {
        if (typeof Highcharts === 'undefined') {
            console.error("Cannot setup chart responsiveness - Highcharts not loaded");
            return;
        }
        
        console.log("Setting up chart responsiveness");
        let resizeTimeout;
        window.addEventListener('resize', function() {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(function() {
                const charts = Highcharts.charts;
                if (charts) {
                    charts.forEach(function(chart) {
                        if (chart) {
                            chart.reflow();
                        }
                    });
                    console.log("Reflowed charts on resize");
                }
            }, 300);
        });
    },

    // Render Monthly Performance Chart
    renderMonthlyPerformanceChart: function(chartData) {
        if (!chartData || chartData.length === 0) {
            console.warn('No data available for Monthly Performance Chart');
            return;
        }

        console.log("Rendering Monthly Performance chart with data:", chartData.length);
        
        const months = chartData.map(item => item.month);
        const portfolioData = chartData.map(item => item.portfolioPerformance);
        const benchmarkData = chartData.map(item => item.benchmarkPerformance);

        // Create portfolio series colors array
        const portfolioColors = portfolioData.map(value => 
            value >= 0 ? '#90ee7e' : '#f45b5b'
        );

        // Create benchmark series colors array
        const benchmarkColors = benchmarkData.map(value => 
            value >= 0 ? '#2b908f' : '#DF5353'
        );

        Highcharts.chart('monthlyPerformanceChart', {
            chart: {
                type: 'column'
            },
            title: {
                text: null
            },
            xAxis: {
                categories: months,
                crosshair: true
            },
            yAxis: {
                title: {
                    text: 'Performance (%)'
                },
                labels: {
                    format: '{value}%'
                }
            },
            tooltip: {
                headerFormat: '<span style="font-size:10px">{point.key}</span><table>',
                pointFormat: '<tr><td style="color:{series.color};padding:0">{series.name}: </td>' +
                    '<td style="padding:0"><b>{point.y:.2f}%</b></td></tr>',
                footerFormat: '</table>',
                shared: true,
                useHTML: true
            },
            plotOptions: {
                column: {
                    pointPadding: 0.2,
                    borderWidth: 0,
                    groupPadding: 0.1
                }
            },
            series: [{
                name: 'Portfolio',
                data: portfolioData,
                colorByPoint: true,
                colors: portfolioColors
            }, {
                name: 'S&P 500',
                data: benchmarkData,
                colorByPoint: true,
                colors: benchmarkColors
            }]
        });
    },

    // Render Rolling Returns Chart
    renderRollingReturnsChart: function(chartData) {
        if (!chartData || chartData.length === 0) {
            console.warn('No data available for Rolling Returns Chart');
            return;
        }

        console.log("Rendering Rolling Returns chart with data:", chartData.length);
        
        const periods = chartData.map(item => item.timePeriod);
        const portfolioData = chartData.map(item => item.portfolioReturn);
        const benchmarkData = chartData.map(item => item.benchmarkReturn);

        Highcharts.chart('rollingReturnsChart', {
            chart: {
                type: 'column'
            },
            title: {
                text: null
            },
            xAxis: {
                categories: periods,
                crosshair: true
            },
            yAxis: {
                title: {
                    text: 'Return (%)'
                },
                labels: {
                    format: '{value}%'
                }
            },
            tooltip: {
                headerFormat: '<span style="font-size:10px">{point.key}</span><table>',
                pointFormat: '<tr><td style="color:{series.color};padding:0">{series.name}: </td>' +
                    '<td style="padding:0"><b>{point.y:.2f}%</b></td></tr>',
                footerFormat: '</table>',
                shared: true,
                useHTML: true
            },
            plotOptions: {
                column: {
                    pointPadding: 0.2,
                    borderWidth: 0
                }
            },
            series: [{
                name: 'Portfolio',
                color: '#2b908f',
                data: portfolioData
            }, {
                name: 'S&P 500',
                color: '#7798BF',
                data: benchmarkData
            }]
        });
    },

    // Render Portfolio Growth Chart
    renderPortfolioGrowthChart: function(chartData) {
        if (!chartData || chartData.length === 0) {
            console.warn('No data available for Portfolio Growth Chart');
            return;
        }

        console.log("Rendering Portfolio Growth chart with data:", chartData.length);

        // Ensure dates are properly parsed
        const dates = chartData.map(item => {
            let date;
            if (typeof item.date === 'string') {
                date = new Date(item.date);
            } else if (item.date instanceof Date) {
                date = item.date;
            } else {
                // Fallback
                date = new Date(item.date);
            }
            return date.getTime();
        });

        const portfolioData = chartData.map((item, index) => [dates[index], item.portfolioValue]);
        const benchmarkData = chartData.map((item, index) => [dates[index], item.benchmarkValue]);

        // Calculate min/max dates to ensure we display 5 years
        let minDate = new Date();
        minDate.setFullYear(minDate.getFullYear() - 5); // Go back 5 years

        // Create percentage change column for relative comparison
        const startPortfolioValue = portfolioData[0]?.[1] || 100;
        const startBenchmarkValue = benchmarkData[0]?.[1] || 100;

        const portfolioPercentData = portfolioData.map(point =>
            [point[0], ((point[1] / startPortfolioValue) - 1) * 100]
        );

        const benchmarkPercentData = benchmarkData.map(point =>
            [point[0], ((point[1] / startBenchmarkValue) - 1) * 100]
        );

        Highcharts.stockChart('portfolioGrowthChart', {
            chart: {
                type: 'area',
                zoomType: 'x',
                backgroundColor: '#212529',
                style: {
                    fontFamily: 'Inter, sans-serif'
                },
                height: 500 // Set explicit height to match Stock Performance chart
            },
            title: {
                text: 'Portfolio Growth',
                style: {
                    color: '#CCCCCC',
                    fontSize: '14px'
                }
            },
            subtitle: {
                text: 'Data shows absolute performance starting at base 100',
                style: {
                    color: '#AAAAAA',
                    fontSize: '12px'
                }
            },
            // Add range selector buttons
            rangeSelector: {
                selected: 5, // Default to 5Y view
                buttons: [
                    { type: 'month', count: 1, text: '1M' },
                    { type: 'month', count: 3, text: '3M' },
                    { type: 'month', count: 6, text: '6M' },
                    { type: 'ytd', text: 'YTD' },
                    { type: 'year', count: 1, text: '1Y' },
                    { type: 'year', count: 5, text: '5Y' },
                    { type: 'all', text: 'ALL' }
                ],
                buttonTheme: {
                    fill: 'none',
                    stroke: 'none',
                    'stroke-width': 0,
                    r: 8,
                    style: {
                        color: '#CCCCCC',
                        fontWeight: 'normal'
                    },
                    states: {
                        hover: {
                            fill: '#4361EE',
                            style: { color: 'white' }
                        },
                        select: {
                            fill: '#4361EE',
                            style: { color: 'white' }
                        }
                    }
                },
                inputEnabled: false
            },
            navigator: {
                enabled: true,
                outlineColor: '#444',
                outlineWidth: 1,
                handles: {
                    backgroundColor: '#666',
                    borderColor: '#AAA'
                },
                series: {
                    color: '#2b908f',
                    fillOpacity: 0.05
                }
            },
            scrollbar: {
                enabled: true,
                barBackgroundColor: '#555',
                barBorderColor: '#999',
                barBorderWidth: 1,
                buttonBackgroundColor: '#555',
                buttonBorderColor: '#999',
                buttonBorderWidth: 1,
                trackBackgroundColor: '#212529',
                trackBorderColor: '#444',
                trackBorderWidth: 1
            },
            xAxis: {
                type: 'datetime',
                dateTimeLabelFormats: {
                    millisecond: '%H:%M:%S.%L',
                    second: '%H:%M:%S',
                    minute: '%H:%M',
                    hour: '%H:%M',
                    day: '%e. %b',
                    week: '%e. %b',
                    month: '%b %Y',
                    year: '%Y'
                },
                title: {
                    text: 'Date',
                    style: { color: '#CCCCCC' }
                },
                labels: {
                    style: { color: '#CCCCCC' }
                },
                ordinal: false, // This ensures equal spacing between data points
                lineColor: '#444',
                tickColor: '#444',
                tickWidth: 1,
            },
            yAxis: {
                title: {
                    text: 'Growth (Base 100)',
                    style: { color: '#CCCCCC' }
                },
                opposite: false,
                min: 80, // Allow some downside
                startOnTick: false,
                endOnTick: false,
                labels: {
                    format: '{value}',
                    style: { color: '#CCCCCC' }
                },
                gridLineColor: 'rgba(255, 255, 255, 0.1)'
            },
            tooltip: {
                pointFormat: '<span style="color:{series.color}">{series.name}</span>: <b>{point.y:.2f}</b><br/>',
                valueDecimals: 2,
                shared: true,
                split: false,
                backgroundColor: 'rgba(0, 0, 0, 0.8)',
                style: { color: '#FFFFFF' }
            },
            plotOptions: {
                series: {
                    marker: {
                        enabled: false,
                        symbol: 'circle',
                        radius: 2,
                        states: {
                            hover: {
                                enabled: true,
                                radiusPlus: 1
                            }
                        }
                    },
                    lineWidth: 2,
                    states: {
                        hover: {
                            lineWidthPlus: 1
                        }
                    }
                },
                area: {
                    fillOpacity: 0.2,
                    trackByArea: false,
                    threshold: null
                }
            },
            legend: {
                enabled: true,
                itemStyle: { color: '#CCCCCC' },
                itemHoverStyle: { color: '#FFFFFF' }
            },
            series: [{
                name: 'Portfolio',
                type: 'area',
                color: '#2b908f',
                data: portfolioData,
                fillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(43, 144, 143, 0.5)'],
                        [1, 'rgba(43, 144, 143, 0.05)']
                    ]
                }
            }, {
                name: 'S&P 500',
                color: '#7798BF',
                data: benchmarkData,
                fillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(119, 152, 191, 0.5)'],
                        [1, 'rgba(119, 152, 191, 0.05)']
                    ]
                }
            }],
            credits: { enabled: false }
        });
    },

    // Render Risk Analysis Chart
    renderRiskAnalysisChart: function(chartData) {
        if (!chartData || chartData.length === 0) {
            console.warn('No data available for Risk Analysis Chart');
            return;
        }

        console.log("Rendering Risk Analysis chart with data:", chartData.length);
        
        // Prepare data for the charts
        const symbols = chartData.map(item => item.symbol);
        const volatility = chartData.map(item => item.volatility);
        const beta = chartData.map(item => item.beta);
        const sharpe = chartData.map(item => item.sharpeRatio);
        const maxDrawdown = chartData.map(item => Math.abs(item.maxDrawdown)); // Absolute value for visualization

        Highcharts.chart('riskAnalysisChart', {
            chart: {
                type: 'column'
            },
            title: {
                text: null
            },
            xAxis: {
                categories: symbols,
                crosshair: true
            },
            yAxis: [{ // Primary yAxis
                title: {
                    text: 'Volatility (%)'
                },
                labels: {
                    format: '{value}%'
                }
            }, { // Secondary yAxis
                title: {
                    text: 'Beta'
                },
                opposite: true
            }],
            tooltip: {
                shared: true
            },
            legend: {
                align: 'center',
                verticalAlign: 'bottom',
                backgroundColor: 'rgba(0,0,0,0.5)',
                shadow: false
            },
            plotOptions: {
                column: {
                    grouping: false,
                    shadow: false,
                    borderWidth: 0
                }
            },
            series: [{
                name: 'Volatility (%)',
                color: '#f45b5b',
                data: volatility,
                pointPadding: 0.3,
                pointPlacement: -0.2
            }, {
                name: 'Max Drawdown (%)',
                color: '#DF5353',
                data: maxDrawdown,
                pointPadding: 0.4,
                pointPlacement: -0.2
            }, {
                name: 'Beta',
                color: '#7798BF',
                data: beta,
                pointPadding: 0.3,
                pointPlacement: 0.2,
                yAxis: 1
            }, {
                name: 'Sharpe Ratio',
                color: '#90ee7e',
                data: sharpe,
                pointPadding: 0.4,
                pointPlacement: 0.2,
                yAxis: 1
            }]
        });
    },

    // Render Drawdown Chart
    renderDrawdownChart: function(chartData) {
        if (!chartData || chartData.length === 0) {
            console.warn('No data available for Drawdown Chart');
            return;
        }

        console.log("Rendering Drawdown chart with data:", chartData.length);
        
        const dates = chartData.map(item => {
            const date = new Date(item.date);
            return date.getTime();
        });
        
        const portfolioData = chartData.map((item, index) => [dates[index], item.portfolioDrawdown]);
        const benchmarkData = chartData.map((item, index) => [dates[index], item.benchmarkDrawdown]);

        Highcharts.chart('drawdownChart', {
            chart: {
                type: 'area',
                zoomType: 'x'
            },
            title: {
                text: null
            },
            xAxis: {
                type: 'datetime',
                dateTimeLabelFormats: {
                    month: '%b %Y',
                    year: '%Y'
                },
                title: {
                    text: 'Date'
                }
            },
            yAxis: {
                title: {
                    text: 'Drawdown (%)'
                },
                labels: {
                    format: '{value}%'
                },
                // Invert the axis to show drawdowns as negative values going down
                reversed: false
            },
            tooltip: {
                headerFormat: '<span style="font-size:10px">{point.x:%B %e, %Y}</span><table>',
                pointFormat: '<tr><td style="color:{series.color};padding:0">{series.name}: </td>' +
                    '<td style="padding:0"><b>{point.y:.2f}%</b></td></tr>',
                footerFormat: '</table>',
                shared: true,
                useHTML: true
            },
            plotOptions: {
                area: {
                    fillOpacity: 0.2,
                    // Gradient and colors will be set per series
                    marker: {
                        radius: 2
                    },
                    lineWidth: 1,
                    states: {
                        hover: {
                            lineWidth: 1
                        }
                    },
                    threshold: 0
                }
            },
            series: [{
                name: 'Portfolio',
                color: '#f45b5b',
                fillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(244, 91, 91, 0.5)'],
                        [1, 'rgba(244, 91, 91, 0.05)']
                    ]
                },
                negativeFillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(244, 91, 91, 0.3)'],
                        [1, 'rgba(244, 91, 91, 0.05)']
                    ]
                },
                data: portfolioData
            }, {
                name: 'S&P 500',
                color: '#2b908f',
                fillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(43, 144, 143, 0.5)'],
                        [1, 'rgba(43, 144, 143, 0.05)']
                    ]
                },
                negativeFillColor: {
                    linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
                    stops: [
                        [0, 'rgba(43, 144, 143, 0.3)'],
                        [1, 'rgba(43, 144, 143, 0.05)']
                    ]
                },
                data: benchmarkData
            }]
        });
    }
};

// Dashboard tab functionality
document.addEventListener('DOMContentLoaded', function() {
    // Initialize chart responsiveness
    ChartManager.setupChartResponsiveness();
    
    // Handle dashboard tab switching
    const dashboardTabs = document.querySelectorAll('.nav-link[data-bs-toggle="tab"]');
    if (dashboardTabs.length > 0) {
        dashboardTabs.forEach(tab => {
            tab.addEventListener('shown.bs.tab', function(event) {
                const targetId = event.target.getAttribute('data-bs-target');
                const targetTab = document.querySelector(targetId);
                
                // Remove the active class from all tab content containers
                document.querySelectorAll('.tab-pane').forEach(pane => {
                    pane.classList.remove('show', 'active');
                });
                
                // Add active class to the selected tab content
                if (targetTab) {
                    targetTab.classList.add('show', 'active');
                    
                    // Force redraw charts in the active tab
                    setTimeout(() => {
                        if (Highcharts && Highcharts.charts) {
                            Highcharts.charts.forEach(chart => {
                                if (chart && chart.renderTo && targetTab.contains(chart.renderTo)) {
                                    chart.reflow();
                                    console.log(`Reflowed chart: ${chart.renderTo.id}`);
                                }
                            });
                        }
                        
                        console.log(`Refreshed charts in ${targetId}`);
                    }, 200);
                }

                // Store the active tab in localStorage for persistence
                localStorage.setItem('activeTab', targetId);
                
                // Update body class for tab-specific styling
                document.body.className = document.body.className.replace(/tab-active-\w+/g, '');
                document.body.classList.add(`tab-active-${targetId.replace('#', '').replace('Tab', '')}`);
            });
        });
        
        // Restore the active tab on page load
        const activeTab = localStorage.getItem('activeTab');
        if (activeTab) {
            const tabToActivate = document.querySelector(`[data-bs-target="${activeTab}"]`);
            if (tabToActivate) {
                const bsTab = new bootstrap.Tab(tabToActivate);
                bsTab.show();
            }
        }
    }

    // Navbar active state
    const currentPath = window.location.pathname;
    document.querySelectorAll('.navbar-nav .nav-link').forEach(link => {
        const href = link.getAttribute('href');
        if (href && currentPath.includes(href) && href !== '/') {
            link.classList.add('active');
        } else if (href === '/' && currentPath === '/') {
            link.classList.add('active');
        }
    });

    // Performance coloring
    document.querySelectorAll('.performance-cell').forEach(cell => {
        const value = parseFloat(cell.textContent);
        if (!isNaN(value)) {
            if (value > 0) {
                cell.classList.add('positive');
                cell.innerHTML = `<i class="fas fa-caret-up me-1"></i>${cell.textContent}`;
            } else if (value < 0) {
                cell.classList.add('negative');
                cell.innerHTML = `<i class="fas fa-caret-down me-1"></i>${cell.textContent}`;
            }
        }
    });
});