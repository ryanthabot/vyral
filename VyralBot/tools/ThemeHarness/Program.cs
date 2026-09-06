using System.Windows;
using System.Windows.Media;

Thread t = new(Run);
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
return;

static void Run()
{
    var app = new Application();
    app.Resources.MergedDictionaries.Add(new ResourceDictionary
    { Source = new Uri("pack://application:,,,/VyralBot.UI;component/Themes/Colors/NativeColors.xaml") });
    app.Resources.MergedDictionaries.Add(new ResourceDictionary
    { Source = new Uri("pack://application:,,,/VyralBot.UI;component/Themes/ThemeBrushes.xaml") });

    var brushesDict = app.Resources.MergedDictionaries[1];
    var before = (app.Resources["BackgroundPrimaryBrush"] as SolidColorBrush)?.Color;
    Console.WriteLine($"Brush before: {before}");

    app.Resources["BackgroundPrimary"] = Color.FromRgb(0xFF, 0x00, 0xFF);
    // FIX: replace the brushes dictionary so every brush is recreated and
    // re-reads the (overridden) colors.
    app.Resources.MergedDictionaries[1] = new ResourceDictionary
    { Source = new Uri("pack://application:,,,/VyralBot.UI;component/Themes/ThemeBrushes.xaml") };

    var after = (app.Resources["BackgroundPrimaryBrush"] as SolidColorBrush)?.Color;
    Console.WriteLine($"After brushes-dict replacement: {after}");
    Console.WriteLine($"FIX WORKS: {after == Color.FromRgb(0xFF, 0x00, 0xFF)}");
}
