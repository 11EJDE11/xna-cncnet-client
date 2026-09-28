using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace AvClientView.Theme;

/// <summary>App-wide adjustments so the XNA layouts draw as they do in the XNA client.</summary>
public static class ThemedAppStyles
{
    public static void Apply(Application application)
    {
        // The themed screens are hosted in plain ContentControls, which clip by default; XNA draws controls outside
        // their window (the lobbies' rab*/rac* frame at -8), so the hosts don't clip. OfType matches ContentControl
        // itself only, not buttons, list items or other derived controls.
        application.Styles.Add(new Style(selector => selector.OfType<ContentControl>())
        {
            Setters = { new Setter(Visual.ClipToBoundsProperty, false) },
        });
    }
}
