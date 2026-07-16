using System.Security.Cryptography;
using System.Text;

namespace MeroShareBot.Shared.Security;

// AES-256-GCM. Key derived from Security:DataEncryptionKey; ciphertext stored as "iv:tag:cipher" hex.
public sealed class CryptoService
{
    private readonly byte[] _key;

    public CryptoService(IOptions<SecurityOptions> opts) =>
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(opts.Value.DataEncryptionKey));

    public string Encrypt(string plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(_key, tag.Length);
        aesGcm.Encrypt(iv, plainBytes, cipherBytes, tag);

        return $"{Convert.ToHexString(iv)}:{Convert.ToHexString(tag)}:{Convert.ToHexString(cipherBytes)}";
    }

    public string Decrypt(string payload)
    {
        var parts = payload.Split(':');
        var iv = Convert.FromHexString(parts[0]);
        var tag = Convert.FromHexString(parts[1]);
        var cipherBytes = Convert.FromHexString(parts[2]);
        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, tag.Length);
        aesGcm.Decrypt(iv, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
