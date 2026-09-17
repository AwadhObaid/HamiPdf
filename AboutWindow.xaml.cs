using System.Reflection;
using System.Windows;
namespace HamiPdf;
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "الإصدار " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—");
    }
}
