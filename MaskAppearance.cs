using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class MaskFiles
{
    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivacyMaskPoC");

    internal static FileStream Open(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    }

    internal static void Save(string path, Action<BinaryWriter> write)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
                write(writer);
            try { File.Replace(temp, path, null); }
            catch (FileNotFoundException) { File.Move(temp, path); }
        }
        finally { File.Delete(temp); }
    }
}

internal enum MaskAppearanceMode { Black, Picture, Text }

internal sealed class MaskAppearance : IDisposable
{
    private const int MaxBytes = 72 * 1024 * 1024;
    internal const int TextLimit = 160;
    private static readonly string PathName = Path.Combine(MaskFiles.DirectoryPath, "appearance.dat");
    internal MaskAppearanceMode Mode;
    internal string Text = "";
    internal Bitmap Picture;

    internal static MaskAppearance Load(out bool recovered)
    {
        recovered = false;
        try
        {
            using (var file = MaskFiles.Open(PathName))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length > MaxBytes || reader.ReadByte() != 1)
                    throw new InvalidDataException();
                var mode = (MaskAppearanceMode)reader.ReadByte();
                string text = reader.ReadString();
                if (!Enum.IsDefined(typeof(MaskAppearanceMode), mode) || text.Length > TextLimit ||
                    (mode == MaskAppearanceMode.Text && string.IsNullOrWhiteSpace(text)) ||
                    (mode == MaskAppearanceMode.Picture && file.Position == file.Length))
                    throw new InvalidDataException();
                var appearance = new MaskAppearance { Mode = mode, Text = text };
                if (file.Position < file.Length)
                {
                    using (var image = new MemoryStream(reader.ReadBytes((int)(file.Length - file.Position))))
                        appearance.Picture = Decode(image);
                }
                return appearance;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { recovered = true; }
        catch (InvalidDataException) { recovered = true; }
        catch (UnauthorizedAccessException) { recovered = true; }
        catch (ArgumentException) { recovered = true; }
        catch (ExternalException) { recovered = true; }
        catch (OutOfMemoryException) { recovered = true; }
        return new MaskAppearance();
    }

    internal void Save()
    {
        MaskFiles.Save(PathName, writer =>
        {
            writer.Write((byte)1);
            writer.Write((byte)Mode);
            writer.Write(Text);
            if (Picture != null)
            {
                using (var png = new MemoryStream())
                {
                    Picture.Save(png, ImageFormat.Png);
                    writer.Write(png.GetBuffer(), 0, (int)png.Length);
                }
            }
        });
    }

    internal void Import(string path)
    {
        using (var file = File.OpenRead(path))
        {
            if (file.Length > MaxBytes)
                throw new InvalidDataException("The image file is too large.");
            Bitmap decoded = Decode(file);
            if (Picture != null)
                Picture.Dispose();
            Picture = decoded;
        }
    }

    private static Bitmap Decode(Stream stream)
    {
        using (var source = Image.FromStream(stream, true, true))
        {
            if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > 65000000)
                throw new InvalidDataException("The pixel dimensions are too large.");
            double scale = Math.Min(1, 4096.0 / Math.Max(source.Width, source.Height));
            var bitmap = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)),
                PixelFormat.Format32bppPArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }

    internal void Draw(Graphics graphics, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;
        graphics.FillRectangle(Brushes.Black, area);
        if (Mode == MaskAppearanceMode.Picture && Picture != null)
        {
            double scale = Math.Min((double)area.Width / Picture.Width, (double)area.Height / Picture.Height);
            int width = Math.Max(1, (int)(scale * Picture.Width));
            int height = Math.Max(1, (int)(scale * Picture.Height));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(Picture, new Rectangle(area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2, width, height));
        }
        if (Mode == MaskAppearanceMode.Text && !string.IsNullOrWhiteSpace(Text))
            DrawText(graphics, area);
    }

    private void DrawText(Graphics graphics, Rectangle area)
    {
        Rectangle box = Rectangle.Inflate(area, -area.Width / 12, -area.Height / 10);
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter })
        {
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int low = 1, high = Math.Max(1, Math.Min(box.Width / 3, box.Height / 4));
            while (low < high)
            {
                int size = (low + high + 1) / 2;
                using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, size, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    int fitted, lines;
                    graphics.MeasureString(Text, font, box.Size, format, out fitted, out lines);
                    if (fitted == Text.Length)
                        low = size;
                    else
                        high = size - 1;
                }
            }
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, low, FontStyle.Bold, GraphicsUnit.Pixel))
                graphics.DrawString(Text, font, Brushes.White, box, format);
        }
    }

    public void Dispose()
    {
        if (Picture != null)
            Picture.Dispose();
    }
}
