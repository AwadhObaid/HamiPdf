using System.Reflection;
using System.Windows;
namespace HamiPdf;
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AutomaticUpdates.IsChecked = Updates.UpdateSettings.Load().Automatic;
        VersionText.Text = "الإصدار " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—");
    }
    private void Automatic_Click(object sender, RoutedEventArgs e)
    {
        var settings = Updates.UpdateSettings.Load(); settings.Automatic = AutomaticUpdates.IsChecked == true; settings.Save();
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            var update = await Updates.ReleaseFeed.CheckAsync(Assembly.GetExecutingAssembly().GetName().Version!);
            if (!IsVisible) return;
            if (update == null) MessageBox.Show(this,"أنت تستخدم أحدث إصدار متاح.","التحديثات");
            else new Updates.UpdateWindow(update) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { if (IsVisible) MessageBox.Show(this,"تعذر التحقق من التحديثات.\n" + ex.Message,"التحديثات"); }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }
}
