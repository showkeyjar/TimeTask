using System.Windows;

namespace TimeTask
{
    public partial class TestApp : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Show the test window
            var window = new TestWindow();
            window.Show();
        }
    }
}
