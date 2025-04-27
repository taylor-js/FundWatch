// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Dashboard tab functionality
document.addEventListener('DOMContentLoaded', function() {
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
                    
                    // Force redraw SVG elements to fix dimension issues
                    setTimeout(() => {
                        const svgElements = targetTab.querySelectorAll('svg');
                        svgElements.forEach(svg => {
                            // Force SVG visibility
                            svg.style.visibility = 'visible';
                            svg.style.display = 'block';
                            
                            // Force SVG dimensions refresh
                            const parent = svg.parentElement;
                            if (parent) {
                                const width = parent.clientWidth;
                                const height = parent.clientHeight || 400;
                                svg.setAttribute('width', width);
                                svg.setAttribute('height', height);
                                
                                // Fix SVG viewBox if it exists
                                if (svg.hasAttribute('viewBox')) {
                                    const viewBox = svg.getAttribute('viewBox').split(' ');
                                    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
                                }
                            }
                            
                            // Ensure all children are visible
                            const children = svg.querySelectorAll('*');
                            children.forEach(child => {
                                child.style.visibility = 'visible';
                            });
                        });
                        
                        // Refresh Syncfusion charts if they exist
                        targetTab.querySelectorAll('[id$="Chart"]').forEach(chart => {
                            if (chart.ej2_instances && chart.ej2_instances[0]) {
                                chart.ej2_instances[0].refresh();
                                console.log(`Refreshed chart: ${chart.id}`);
                            }
                        });
                        
                        console.log(`Fixed SVG elements in ${targetId}`);
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