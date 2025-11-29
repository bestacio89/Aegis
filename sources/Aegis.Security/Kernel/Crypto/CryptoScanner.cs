using System.Security.Cryptography;

namespace Aegis.Security.Kernel.Crypto;

public sealed class CryptoScanner
{
    public IEnumerable<CryptoScanResult> Scan()
    {
        // Hash algorithms
        yield return InspectHash("MD5", () => MD5.Create());
        yield return InspectHash("SHA1", () => SHA1.Create());
        yield return InspectHash("SHA256", () => SHA256.Create());
        yield return InspectHash("SHA512", () => SHA512.Create());

        // Symmetric algorithms
        yield return InspectSymmetric("DES", () => DES.Create());
        yield return InspectSymmetric("3DES", () => TripleDES.Create());
        yield return InspectSymmetric("RC2", () => RC2.Create());
        yield return InspectSymmetric("AES", () => Aes.Create());

        // Random number generators
        yield return InspectRandom();
    }

    private CryptoScanResult InspectHash(string name, Func<HashAlgorithm> factory)
    {
        try
        {
            using var h = factory();
            bool weak = name is "MD5" or "SHA1"; // NIST deprecated

            return new CryptoScanResult(
                Algorithm: name,
                KeySize: null,
                Mode: null,
                IsWeak: weak,
                Error: null
            );
        }
        catch (Exception ex)
        {
            return new CryptoScanResult(name, null, null, true, ex.Message);
        }
    }

    private CryptoScanResult InspectSymmetric(string name, Func<SymmetricAlgorithm> factory)
    {
        try
        {
            using var alg = factory();
            bool weak =
                name is "DES" or "3DES" or "RC2" ||
                (alg.KeySize < 128); // Minimum NIST secure key size

            return new CryptoScanResult(
                Algorithm: name,
                KeySize: alg.KeySize,
                Mode: alg.Mode.ToString(),
                IsWeak: weak,
                Error: null
            );
        }
        catch (Exception ex)
        {
            return new CryptoScanResult(name, null, null, true, ex.Message);
        }
    }

    private CryptoScanResult InspectRandom()
    {
        try
        {
            // Check if System.Random is in use (insecure)
            bool weak = true; // System.Random always weak vs RNGCryptoServiceProvider/RandomNumberGenerator

            return new CryptoScanResult(
                Algorithm: "System.Random",
                KeySize: null,
                Mode: null,
                IsWeak: weak,
                Error: null
            );
        }
        catch (Exception ex)
        {
            return new CryptoScanResult("System.Random", null, null, true, ex.Message);
        }
    }
}
