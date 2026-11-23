using System.IO;

internal sealed class MaskLanguage
{
    internal static readonly MaskLanguage English = new MaskLanguage(false);
    internal static readonly MaskLanguage Chinese = new MaskLanguage(true);
    private static readonly string PathName = Path.Combine(MaskFiles.DirectoryPath, "language.dat");

    internal readonly bool IsChinese;

    private MaskLanguage(bool chinese)
    {
        IsChinese = chinese;
    }

    internal static MaskLanguage Load()
    {
        try
        {
            using (var file = MaskFiles.Open(PathName))
                return file.Length == 1 && file.ReadByte() == 1 ? Chinese : English;
        }
        catch (IOException) { return English; }
        catch (System.UnauthorizedAccessException) { return English; }
    }

    internal void Save()
    {
        MaskFiles.Save(PathName, writer => writer.Write(IsChinese));
    }

    internal string UnlockTitle
    {
        get { return Text("Unlock Mask", "解除遮罩"); }
    }

    internal string Text(string english, string chinese)
    {
        return IsChinese ? chinese : english;
    }
}
