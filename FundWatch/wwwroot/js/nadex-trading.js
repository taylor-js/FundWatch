// Nadex Trading Signal System
(function () {
    'use strict';

    // Global variables
    let autoRefreshInterval = null;
    let signalHistory = [];
    let currentSymbol = 'SPY';
    let currentTimeframe = '5min';

    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function () {
        initializeEventListeners();
        updateMarketStatus();
        updateCurrentTime();
        setInterval(updateCurrentTime, 1000);
    });

    function initializeEventListeners() {
        // Symbol input and analyze button
        document.getElementById('analyzeBtn').addEventListener('click', analyzeTradingSignals);
        document.getElementById('symbolInput').addEventListener('keypress', function (e) {
            if (e.key === 'Enter') analyzeTradingSignals();
        });

        // Popular symbol buttons
        document.querySelectorAll('.popular-symbol').forEach(btn => {
            btn.addEventListener('click', function () {
                document.getElementById('symbolInput').value = this.dataset.symbol;
                analyzeTradingSignals();
            });
        });

        // Timeframe change
        document.getElementById('timeframeSelect').addEventListener('change', function () {
            currentTimeframe = this.value;
            if (currentSymbol) analyzeTradingSignals();
        });

        // Auto-refresh toggle
        document.getElementById('autoRefresh').addEventListener('change', function () {
            document.getElementById('refreshInterval').disabled = !this.checked;
            if (this.checked) {
                startAutoRefresh();
            } else {
                stopAutoRefresh();
            }
        });

        // Refresh interval change
        document.getElementById('refreshInterval').addEventListener('change', function () {
            if (document.getElementById('autoRefresh').checked) {
                stopAutoRefresh();
                startAutoRefresh();
            }
        });
    }

    function updateMarketStatus() {
        const now = new Date();
        const easternTime = new Date(now.toLocaleString("en-US", { timeZone: "America/New_York" }));
        const day = easternTime.getDay();
        const hour = easternTime.getHours();
        const minute = easternTime.getMinutes();

        let marketStatus = 'CLOSED';
        let nextOpen = '';

        // Nadex trading hours: Sunday 6 PM ET to Friday 4:15 PM ET
        if (day === 0 && hour >= 18) { // Sunday after 6 PM
            marketStatus = 'OPEN';
        } else if (day >= 1 && day <= 4) { // Monday through Thursday
            marketStatus = 'OPEN';
        } else if (day === 5 && (hour < 16 || (hour === 16 && minute <= 15))) { // Friday before 4:15 PM
            marketStatus = 'OPEN';
        }

        // Calculate next open
        if (marketStatus === 'CLOSED') {
            if (day === 5 && hour >= 16) { // Friday after close
                nextOpen = 'Sunday 6:00 PM ET';
            } else if (day === 6) { // Saturday
                nextOpen = 'Sunday 6:00 PM ET';
            } else if (day === 0 && hour < 18) { // Sunday before open
                nextOpen = 'Today 6:00 PM ET';
            }
        }

        document.getElementById('marketStatusText').textContent = marketStatus;
        document.getElementById('marketStatusText').className = marketStatus === 'OPEN' ? 'text-success' : 'text-danger';
        document.getElementById('nextOpen').textContent = nextOpen || 'N/A';
    }

    function updateCurrentTime() {
        const now = new Date();
        const easternTime = new Date(now.toLocaleString("en-US", { timeZone: "America/New_York" }));
        const timeString = easternTime.toLocaleTimeString('en-US', { 
            hour: '2-digit', 
            minute: '2-digit', 
            second: '2-digit' 
        });
        document.getElementById('currentTime').textContent = timeString;
    }

    async function analyzeTradingSignals() {
        const symbol = document.getElementById('symbolInput').value.trim().toUpperCase();
        if (!symbol) {
            showAlert('Please enter a symbol', 'warning');
            return;
        }

        currentSymbol = symbol;
        showLoading(true);

        try {
            const response = await fetch('/NadexTrading/GetTradingSignals', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    symbol: symbol,
                    timeFrame: currentTimeframe
                })
            });

            const data = await response.json();

            if (data.success) {
                displayTradingSignals(data.signals);
                addToHistory(symbol, data.signals);
                const signalContainer = document.getElementById('signalContainer');
                if (signalContainer) {
                    signalContainer.classList.remove('d-none');
                } else {
                    console.error('Signal container not found');
                }
                updateLastUpdateTime();
            } else {
                showAlert(data.message || 'Failed to get trading signals', 'danger');
            }
        } catch (error) {
            console.error('Error:', error);
            showAlert('Error analyzing trading signals', 'danger');
        } finally {
            showLoading(false);
        }
    }

    function displayTradingSignals(signals) {
        // Validate signals object
        if (!signals || typeof signals !== 'object') {
            showAlert('Invalid trading signals received', 'danger');
            return;
        }

        // Update price and basic info
        document.getElementById('currentPrice').textContent = `$${(signals.currentPrice || 0).toFixed(2)}`;
        
        // Update signal and confidence
        const signalElement = document.getElementById('signalText');
        signalElement.textContent = signals.signal || 'NEUTRAL';
        signalElement.className = `mb-0 ${getSignalClass(signals.signal || 'NEUTRAL')}`;
        
        const confidence = signals.confidence || 0;
        document.getElementById('confidenceBar').style.width = `${confidence}%`;
        document.getElementById('confidenceBar').className = `progress-bar ${getConfidenceBarClass(confidence)}`;
        document.getElementById('confidenceText').textContent = `${confidence.toFixed(1)}%`;

        // Update technical indicators with fallbacks
        updateRSI(signals.rsi || signals.RSI || 50);
        document.getElementById('sma20').textContent = (signals.sma20 || signals.SMA20 || 0).toFixed(2);
        document.getElementById('sma50').textContent = (signals.sma50 || signals.SMA50 || 0).toFixed(2);
        document.getElementById('macdValue').textContent = (signals.macd || signals.MACD || 0).toFixed(4);
        document.getElementById('macdSignal').textContent = (signals.macdSignal || signals.MACDSignal || 0).toFixed(4);

        // Update support and resistance
        document.getElementById('supportLevel').textContent = `$${(signals.support || 0).toFixed(2)}`;
        document.getElementById('resistanceLevel').textContent = `$${(signals.resistance || 0).toFixed(2)}`;
        updatePricePosition(signals.currentPrice || 0, signals.support || 0, signals.resistance || 0);

        // Update Nadex strategy
        updateNadexStrategy(signals);

        // Check for alerts
        checkAlerts(signals);
    }

    function updateRSI(rsi) {
        const rsiElement = document.getElementById('rsiValue');
        const statusElement = document.getElementById('rsiStatus');
        
        rsiElement.textContent = rsi.toFixed(2);
        
        if (rsi < 30) {
            statusElement.textContent = 'Oversold';
            statusElement.className = 'badge bg-success';
        } else if (rsi > 70) {
            statusElement.textContent = 'Overbought';
            statusElement.className = 'badge bg-danger';
        } else {
            statusElement.textContent = 'Neutral';
            statusElement.className = 'badge bg-secondary';
        }
    }

    function updatePricePosition(price, support, resistance) {
        const range = resistance - support;
        const position = ((price - support) / range) * 100;
        const clampedPosition = Math.max(0, Math.min(100, position));
        
        document.getElementById('pricePosition').style.left = `${clampedPosition}%`;
    }

    function updateNadexStrategy(signals) {
        const actionElement = document.getElementById('strategyAction');
        const callStrike = document.getElementById('callStrike');
        const putStrike = document.getElementById('putStrike');
        const expectedPayout = document.getElementById('expectedPayout');
        const winProbability = document.getElementById('winProbability');

        // Update strategy action
        let action = '';
        let actionClass = 'alert-secondary';
        
        if (signals.signal.includes('STRONG BUY')) {
            action = 'BUY CALL Option';
            actionClass = 'alert-success signal-strong-buy';
        } else if (signals.signal.includes('BUY')) {
            action = 'Consider CALL Option';
            actionClass = 'alert-success signal-buy';
        } else if (signals.signal.includes('STRONG SELL')) {
            action = 'BUY PUT Option';
            actionClass = 'alert-danger signal-strong-sell';
        } else if (signals.signal.includes('SELL')) {
            action = 'Consider PUT Option';
            actionClass = 'alert-danger signal-sell';
        } else {
            action = 'WAIT - No Clear Signal';
            actionClass = 'alert-secondary signal-neutral';
        }

        actionElement.textContent = action;
        actionElement.className = `alert text-center ${actionClass}`;

        // Update strikes
        callStrike.textContent = `$${signals.callStrike.toFixed(2)}`;
        putStrike.textContent = `$${signals.putStrike.toFixed(2)}`;

        // Update risk analysis
        const payoutPercent = (signals.expectedPayout * 100).toFixed(1);
        expectedPayout.textContent = signals.expectedPayout > 0 ? `+${payoutPercent}%` : `${payoutPercent}%`;
        expectedPayout.className = signals.expectedPayout > 0 ? 'text-success' : 'text-danger';
        winProbability.textContent = `${signals.confidence.toFixed(1)}%`;
    }

    function addToHistory(symbol, signals) {
        const historyItem = {
            time: new Date().toLocaleTimeString(),
            symbol: symbol,
            price: signals.currentPrice,
            signal: signals.signal,
            confidence: signals.confidence,
            result: 'Pending'
        };

        signalHistory.unshift(historyItem);
        if (signalHistory.length > 10) signalHistory.pop();

        updateHistoryTable();
        updateActiveSignalsCount();
    }

    function updateHistoryTable() {
        const tbody = document.querySelector('#signalHistory tbody');
        
        if (signalHistory.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No signals yet. Start by analyzing a symbol.</td></tr>';
            return;
        }

        tbody.innerHTML = signalHistory.map(item => `
            <tr>
                <td>${item.time}</td>
                <td><strong>${item.symbol}</strong></td>
                <td>$${item.price.toFixed(2)}</td>
                <td><span class="badge ${getSignalBadgeClass(item.signal)}">${item.signal}</span></td>
                <td>${item.confidence.toFixed(1)}%</td>
                <td>${item.result}</td>
            </tr>
        `).join('');
    }

    function updateActiveSignalsCount() {
        const activeCount = signalHistory.filter(s => s.result === 'Pending').length;
        document.getElementById('activeSignals').textContent = activeCount;
    }

    function checkAlerts(signals) {
        if (!document.getElementById('strongSignalAlert').checked) return;

        if (signals.signal.includes('STRONG')) {
            if (document.getElementById('soundAlert').checked) {
                playAlertSound();
            }
            showNotification(`Strong ${signals.signal} signal for ${currentSymbol}!`);
        }
    }

    function startAutoRefresh() {
        const interval = parseInt(document.getElementById('refreshInterval').value) * 1000;
        autoRefreshInterval = setInterval(() => {
            if (currentSymbol) analyzeTradingSignals();
        }, interval);
    }

    function stopAutoRefresh() {
        if (autoRefreshInterval) {
            clearInterval(autoRefreshInterval);
            autoRefreshInterval = null;
        }
    }

    function updateLastUpdateTime() {
        document.getElementById('lastUpdate').textContent = new Date().toLocaleTimeString();
    }

    function showLoading(show) {
        const analyzeBtn = document.getElementById('analyzeBtn');
        if (show) {
            analyzeBtn.disabled = true;
            analyzeBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span> Analyzing...';
        } else {
            analyzeBtn.disabled = false;
            analyzeBtn.innerHTML = '<i class="fas fa-chart-line"></i> Analyze';
        }
    }

    function showAlert(message, type) {
        // Create a toast notification
        const toastContainer = document.getElementById('toast-container') || createToastContainer();
        
        const toast = document.createElement('div');
        toast.className = `alert alert-${type} alert-dismissible fade show`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        `;
        
        toastContainer.appendChild(toast);
        
        // Auto-dismiss after 5 seconds
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 150);
        }, 5000);
    }
    
    function createToastContainer() {
        const container = document.createElement('div');
        container.id = 'toast-container';
        container.style.cssText = 'position: fixed; top: 100px; right: 20px; z-index: 9999;';
        document.body.appendChild(container);
        return container;
    }

    function showNotification(message) {
        if ('Notification' in window && Notification.permission === 'granted') {
            new Notification('FundWatch Trading Alert', {
                body: message,
                icon: '/img/fundwatch-logo.svg'
            });
        }
    }

    function playAlertSound() {
        // Create a simple beep sound
        const audioContext = new (window.AudioContext || window.webkitAudioContext)();
        const oscillator = audioContext.createOscillator();
        oscillator.connect(audioContext.destination);
        oscillator.frequency.value = 800;
        oscillator.start();
        oscillator.stop(audioContext.currentTime + 0.2);
    }

    function getSignalClass(signal) {
        if (signal.includes('STRONG BUY')) return 'text-success fw-bold';
        if (signal.includes('BUY')) return 'text-success';
        if (signal.includes('STRONG SELL')) return 'text-danger fw-bold';
        if (signal.includes('SELL')) return 'text-danger';
        return 'text-secondary';
    }

    function getSignalBadgeClass(signal) {
        if (signal.includes('STRONG BUY')) return 'bg-success';
        if (signal.includes('BUY')) return 'bg-success';
        if (signal.includes('STRONG SELL')) return 'bg-danger';
        if (signal.includes('SELL')) return 'bg-danger';
        return 'bg-secondary';
    }

    function getConfidenceBarClass(confidence) {
        if (confidence >= 80) return 'bg-success';
        if (confidence >= 60) return 'bg-info';
        if (confidence >= 40) return 'bg-warning';
        return 'bg-danger';
    }

    // Request notification permissions on load
    if ('Notification' in window && Notification.permission === 'default') {
        Notification.requestPermission();
    }
})();