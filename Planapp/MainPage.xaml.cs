namespace com.usagemeter.androidapp
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== MAINPAGE CONSTRUCTOR START ===");

                InitializeComponent();

                System.Diagnostics.Debug.WriteLine("=== MAINPAGE CONSTRUCTOR COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR IN MAINPAGE CONSTRUCTOR: {ex} ===");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to write crash log
                try
                {
                    var crashLog = $"MAINPAGE CONSTRUCTOR CRASH at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n\n";
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mainpage_crash.log"),
                        crashLog
                    );
                }
                catch { }

                throw; // Re-throw to trigger system error
            }
        }
    }
}