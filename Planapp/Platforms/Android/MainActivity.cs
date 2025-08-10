using Android.App;
using Android.Content.PM;

namespace com.usagemeter.androidapp
{
    [Activity(
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        Exported = true)]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}