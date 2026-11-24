using System.Drawing;
using System.Windows.Forms;

internal static class MaskUi
{
    private static readonly bool HighContrast = SystemInformation.HighContrast;
    internal static readonly Color Canvas = HighContrast ? SystemColors.Window : Color.FromArgb(238, 243, 249);
    internal static readonly Color Surface = HighContrast ? SystemColors.Window : Color.White;
    internal static readonly Color Ink = HighContrast ? SystemColors.WindowText : Color.FromArgb(20, 37, 60);
    internal static readonly Color Muted = HighContrast ? SystemColors.WindowText : Color.FromArgb(67, 88, 113);
    internal static readonly Color Line = HighContrast ? SystemColors.WindowFrame : Color.FromArgb(193, 209, 226);
    internal static readonly Color Accent = HighContrast ? SystemColors.Highlight : Color.FromArgb(27, 89, 175);
    internal static readonly Color ActiveFill = HighContrast ? SystemColors.Window : Color.FromArgb(226, 246, 236);
    internal static readonly Color ActiveInk = HighContrast ? SystemColors.WindowText : Color.FromArgb(17, 99, 63);
    internal static readonly Color WarningInk = HighContrast ? SystemColors.WindowText : Color.FromArgb(147, 65, 19);
    internal static readonly Font Heading = new Font(SystemFonts.MessageBoxFont.FontFamily, 17, FontStyle.Bold);
    internal static readonly Font Subheading = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold);
    internal static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
    internal static readonly Bitmap BrandMark = AppIcon.ToBitmap();

    internal static void StyleWindow(Form form)
    {
        form.Font = SystemFonts.MessageBoxFont;
        form.AutoScaleMode = AutoScaleMode.Font;
        form.AutoScaleDimensions = new SizeF(7, 15);
        form.BackColor = Canvas;
        form.ForeColor = Ink;
        form.Icon = AppIcon;
    }

    internal static Label Title()
    {
        return new Label { Font = Heading, Dock = DockStyle.Fill, Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft };
    }

    internal static Label Caption()
    {
        return new Label { ForeColor = Muted, Dock = DockStyle.Fill, Margin = Padding.Empty };
    }

    internal static void StyleButton(Button button, bool primary = false)
    {
        button.MinimumSize = new Size(82, 32);
        button.Padding = new Padding(8, 0, 8, 0);
        if (HighContrast)
            return;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? Color.White : Ink;
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(21, 73, 147) : Canvas;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(17, 60, 126) : Line;
        if (primary)
            button.EnabledChanged += (sender, args) =>
            {
                button.BackColor = button.Enabled ? Accent : Surface;
                button.FlatAppearance.BorderColor = button.Enabled ? Accent : Line;
            };
    }

    internal static void StyleSelector(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        if (!HighContrast)
            combo.FlatStyle = FlatStyle.Flat;
    }
}
