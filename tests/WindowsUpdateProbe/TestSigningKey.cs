using Chaos.NaCl;
using System.Security.Cryptography;

namespace WindowsUpdateProbe;

// Somente fixtures. Usa a mesma dependencia criptografica do NetSparkle.
// Nenhuma chave privada e serializada, enviada por HTTP ou escrita em logs.
internal sealed class TestSigningKey : IDisposable
{
    private readonly byte[] expandedPrivateKey;
    public string PublicKey { get; }

    public TestSigningKey()
    {
        var seed = RandomNumberGenerator.GetBytes(Ed25519.PrivateKeySeedSizeInBytes);
        try
        {
            Ed25519.KeyPairFromSeed(out var publicKey, out expandedPrivateKey, seed);
            PublicKey = Convert.ToBase64String(publicKey);
        }
        finally { CryptographicOperations.ZeroMemory(seed); }
    }

    public string Sign(byte[] data) => Convert.ToBase64String(Ed25519.Sign(data, expandedPrivateKey));
    public void Dispose() => CryptographicOperations.ZeroMemory(expandedPrivateKey);
}
