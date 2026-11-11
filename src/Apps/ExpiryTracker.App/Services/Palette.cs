namespace ExpiryTracker.App.Services;

/// <summary>
/// Status colours. Same values as Resources/Styles/Colors.xaml. Chosen for contrast with white text
/// and always shown together with words, never colour alone.
/// </summary>
public static class Palette
{
    public static readonly Color Expired = Color.FromArgb("#B3261E");
    public static readonly Color UseSoon = Color.FromArgb("#9A4D00");
    public static readonly Color Fresh = Color.FromArgb("#1E7B34");
    public static readonly Color NoDate = Color.FromArgb("#5F6368");
}
