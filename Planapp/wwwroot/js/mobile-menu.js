// Mobile menu helper functions
window.mobileMenu = {
    // Initialize mobile menu functionality
    init: function () {
        // Handle clicks outside the mobile menu to close it
        document.addEventListener('click', function (event) {
            const navMenu = document.querySelector('.nav-scrollable');
            const toggleButton = document.querySelector('.navbar-toggler');

            if (navMenu && toggleButton) {
                const isMenuOpen = navMenu.classList.contains('show');
                const clickedInsideMenu = navMenu.contains(event.target);
                const clickedToggleButton = toggleButton.contains(event.target);

                if (isMenuOpen && !clickedInsideMenu && !clickedToggleButton) {
                    // Close the menu
                    window.mobileMenu.closeMenu();
                }
            }
        });

        // Handle escape key to close menu
        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                window.mobileMenu.closeMenu();
            }
        });

        // Prevent body scroll when menu is open on mobile
        const observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                if (mutation.type === 'attributes' && mutation.attributeName === 'class') {
                    const navMenu = document.querySelector('.nav-scrollable');
                    if (navMenu && navMenu.classList.contains('show')) {
                        document.body.style.overflow = 'hidden';
                    } else {
                        document.body.style.overflow = '';
                    }
                }
            });
        });

        const navMenu = document.querySelector('.nav-scrollable');
        if (navMenu) {
            observer.observe(navMenu, { attributes: true });
        }
    },

    // Close the mobile menu
    closeMenu: function () {
        const navMenu = document.querySelector('.nav-scrollable');
        if (navMenu && navMenu.classList.contains('show')) {
            navMenu.classList.remove('show');
            document.body.style.overflow = '';

            // Trigger Blazor component update if available
            if (window.blazorMobileMenu && window.blazorMobileMenu.closeMenu) {
                window.blazorMobileMenu.closeMenu();
            }
        }
    },

    // Open the mobile menu
    openMenu: function () {
        const navMenu = document.querySelector('.nav-scrollable');
        if (navMenu && !navMenu.classList.contains('show')) {
            navMenu.classList.add('show');
            document.body.style.overflow = 'hidden';
        }
    },

    // Toggle the mobile menu
    toggleMenu: function () {
        const navMenu = document.querySelector('.nav-scrollable');
        if (navMenu) {
            if (navMenu.classList.contains('show')) {
                window.mobileMenu.closeMenu();
            } else {
                window.mobileMenu.openMenu();
            }
        }
    }
};

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', window.mobileMenu.init);
} else {
    window.mobileMenu.init();
}