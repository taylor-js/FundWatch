// Holdings Tab JavaScript
var HoldingsUtils = (function() {
    'use strict';
    
    function toggleAdvancedColumns() {
        const advancedColumns = document.querySelectorAll('.advanced-column');
        advancedColumns.forEach(function(col) {
            col.classList.toggle('show');
        });
        
        // Save preference
        const isShowing = advancedColumns.length > 0 && advancedColumns[0].classList.contains('show');
        localStorage.setItem('showAdvancedColumns', isShowing);
    }
    
    function initializeHoldings() {
        // Delete confirmation
        document.addEventListener('click', function (e) {
            if (e.target.closest('.delete-btn')) {
                e.preventDefault();
                if (confirm('Are you sure you want to delete this stock from your portfolio?')) {
                    e.target.closest('form').submit();
                }
            }
        });
        
        // Load saved preference
        const showAdvanced = localStorage.getItem('showAdvancedColumns') === 'true';
        if (showAdvanced) {
            toggleAdvancedColumns();
        }
        
        // Enable tooltips
        var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
        var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
            return new bootstrap.Tooltip(tooltipTriggerEl);
        });
    }
    
    // Public API
    return {
        toggleAdvancedColumns: toggleAdvancedColumns,
        initialize: initializeHoldings
    };
})();

// Make toggleAdvancedColumns available globally for onclick attribute
window.toggleAdvancedColumns = HoldingsUtils.toggleAdvancedColumns;

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    HoldingsUtils.initialize();
});