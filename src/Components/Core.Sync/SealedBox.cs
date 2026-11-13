using System.Security.Cryptography;
using System.Text;

namespace HomeTracker.Core.Sync;

/// <summary>
/// End-to-end encryption with a shared 32-byte key (AES-256-GCM). The relay only ever sees the sealed text.
/// A label (e.g. the home id) is bound to every message, so a message can't be replayed into another home.
/// </summary>
public static class SealedBox
{
    private const string Version = "1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Seal(byte[] key, byte[] plaintext, string label)
    {
        CheckKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        nonce.CopyTo(output, 0);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            output.AsSpan(NonceSize, plaintext.Length),
            output.AsSpan(NonceSize + plaintext.Length, TagSize),
            Encoding.UTF8.GetBytes(label));

        return Version + Convert.ToBase64String(output);
    }

    /// <returns>The plaintext, or null when the text wasn't sealed with this key and label (or was altered).</returns>
    public static byte[]? Open(byte[] key, string sealedText, string label)
    {
        CheckKey(key);
        if (!sealedText.StartsWith(Version, StringComparison.Ordinal))
        {
            return null;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(sealedText[Version.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (data.Length < NonceSize + TagSize)
        {
            return null;
        }

        var plaintext = new byte[data.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                data.AsSpan(0, NonceSize),
                data.AsSpan(NonceSize, plaintext.Length),
                data.AsSpan(NonceSize + plaintext.Length, TagSize),
                plaintext,
                Encoding.UTF8.GetBytes(label));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private static void CheckKey(byte[] key)
    {
        if (key is not { Length: 32 })
        {
            throw new ArgumentException("The key must be 32 bytes.", nameof(key));
        }
    }
}
