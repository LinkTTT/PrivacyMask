using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class MaskHotkeys
{
    internal const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;
    internal const int ScopeCount = 3;
    private static readonly string PathName = Path.Combine(MaskFiles.DirectoryPath, "shortcuts.dat");

    internal static int[] Defaults()
    {
        uint modifiers = ModControl | ModAlt | ModShift;
        return new[] { Encode(modifiers, Keys.D0), Encode(modifiers, Keys.D1), Encode(modifiers, Keys.D2) };
    }

    internal static int Encode(uint modifiers, Keys key)
    {
        return (int)(modifiers << 8) | (int)key;
    }

    internal static IEnumerable<Keys> AvailableKeys()
    {
        for (Keys key = Keys.A; key <= Keys.Z; key++)
            yield return key;
        for (Keys key = Keys.D0; key <= Keys.D9; key++)
            yield return key;
        for (Keys key = Keys.F1; key <= Keys.F11; key++)
            yield return key;
    }

    internal static bool IsValid(int binding)
    {
        if (binding == 0)
            return true;
        uint modifiers = (uint)binding >> 8;
        if (modifiers > 15 || (modifiers & (ModControl | ModAlt | ModWin)) == 0)
            return false;
        Keys key = (Keys)(binding & 0xff);
        return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) ||
            (key >= Keys.F1 && key <= Keys.F11);
    }

    internal static string KeyName(Keys key)
    {
        return key >= Keys.D0 && key <= Keys.D9 ? ((char)key).ToString() : key.ToString();
    }

    internal static string Describe(int binding)
    {
        uint modifiers = (uint)binding >> 8;
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName((Keys)(binding & 0xff)));
        return string.Join("+", parts);
    }

    internal static int[] Load()
    {
        try
        {
            using (var file = MaskFiles.Open(PathName))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length != 1 + ScopeCount * sizeof(int) || reader.ReadByte() != 1)
                    return Defaults();
                var bindings = new int[ScopeCount];
                var used = new HashSet<int>();
                for (int i = 0; i < bindings.Length; i++)
                {
                    int binding = reader.ReadInt32();
                    if (!IsValid(binding) || (binding != 0 && !used.Add(binding)))
                        return Defaults();
                    bindings[i] = binding;
                }
                return bindings;
            }
        }
        catch (IOException) { return Defaults(); }
        catch (UnauthorizedAccessException) { return Defaults(); }
    }

    internal static void Save(int[] bindings)
    {
        MaskFiles.Save(PathName, writer =>
        {
            writer.Write((byte)1);
            foreach (int binding in bindings)
                writer.Write(binding);
        });
    }

    internal static bool Register(IntPtr hwnd, int binding)
    {
        return RegisterHotKey(hwnd, binding, ((uint)binding >> 8) | ModNoRepeat, (uint)(binding & 0xff));
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
