using System.ComponentModel;

namespace com.usagemeter.androidapp
{
    public partial class AppShell : Shell, INotifyPropertyChanged
    {
        public AppShell()
        {
            InitializeComponent();
            BindingContext = this;

            // Register routes for navigation
            RegisterRoutes();
        }

        private void RegisterRoutes()
        {
            // Register routes for pages that need parameters
            Routing.RegisterRoute("rules/edit", typeof(Components.Pages.RuleEdit));
            Routing.RegisterRoute("rules/select-apps", typeof(Components.Pages.AppSelection));
            Routing.RegisterRoute("rules/select-target", typeof(Components.Pages.AppSelection));
            Routing.RegisterRoute("app-launcher-test", typeof(Components.Pages.AppLauncherTest));
        }

        // Property to show/hide Android-specific menu items
        public bool IsAndroid => DeviceInfo.Platform == DevicePlatform.Android;

        // Property to show/hide debug menu items
        public bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}