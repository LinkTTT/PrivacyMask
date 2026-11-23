using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

internal sealed class MaskPassword
{
    private const int Iterations = 600000;
    internal const int LengthLimit = 128;
    private static readonly string PathName = Path.Combine(MaskFiles.DirectoryPath, "password.dat");
    private readonly byte[] salt;
    private readonly byte[] hash;
    internal readonly bool DefaultContinuous;

    private MaskPassword(byte[] salt, byte[] hash, bool continuous)
    {
        this.salt = salt;
        this.hash = hash;
        DefaultContinuous = continuous;
    }

    internal static MaskPassword Load()
    {
        try
        {
            using (var file = MaskFiles.Open(PathName))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length != 50 || reader.ReadByte() != 1)
                    throw new InvalidDataException("password-settings-invalid");
                bool continuous = reader.ReadBoolean();
                return new MaskPassword(reader.ReadBytes(16), reader.ReadBytes(32), continuous);
            }
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal bool Verify(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > LengthLimit)
            return false;
        byte[] attempt = Derive(password, salt);
        int unequal = 0;
        for (int i = 0; i < hash.Length; i++)
            unequal |= attempt[i] ^ hash[i];
        Array.Clear(attempt, 0, attempt.Length);
        return unequal == 0;
    }

    internal static bool Change(string current, string replacement, bool continuous, bool disable)
    {
        using (var gate = new Mutex(false, @"Local\PrivacyMask-PasswordSettings"))
        {
            try { gate.WaitOne(); }
            catch (AbandonedMutexException) { }
            try
            {
                var existing = Load();
                if (existing != null && !existing.Verify(current))
                    return false;
                if (disable)
                {
                    File.Delete(PathName);
                    return true;
                }

                if (replacement.Length > 0 || existing == null)
                {
                    if (replacement.Length < 4 || replacement.Length > LengthLimit || string.IsNullOrWhiteSpace(replacement))
                        throw new ArgumentException("The password must contain between 4 and 128 characters and cannot be blank.");
                    var freshSalt = new byte[16];
                    using (var random = RandomNumberGenerator.Create())
                        random.GetBytes(freshSalt);
                    existing = new MaskPassword(freshSalt, Derive(replacement, freshSalt), continuous);
                }

                MaskFiles.Save(PathName, writer =>
                {
                    writer.Write((byte)1);
                    writer.Write(continuous);
                    writer.Write(existing.salt);
                    writer.Write(existing.hash);
                });
                return true;
            }
            finally { gate.ReleaseMutex(); }
        }
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        using (var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            return key.GetBytes(32);
    }
}
