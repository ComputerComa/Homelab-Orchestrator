using System.Text;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>A parsed OpenSSH public-key record. Identity for deduplication is (Type, EncodedData); the comment is not significant.</summary>
public readonly record struct SshPublicKeyRecord(string Type, string EncodedData);

/// <summary>
/// Parses and validates a single line of a normal OpenSSH public-key file: "&lt;type&gt; &lt;base64-data&gt; [comment]".
/// Pure and unit-testable.
/// </summary>
public static class OpenSshPublicKey
{
    public static bool TryParse(string line, out SshPublicKeyRecord record)
    {
        record = default;

        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var type = parts[0];
        var encodedData = parts[1];

        if (!MatchesDeclaredType(type, encodedData))
        {
            return false;
        }

        record = new SshPublicKeyRecord(type, encodedData);
        return true;
    }

    /// <summary>
    /// The OpenSSH wire format embeds the key type as a length-prefixed string at the very start
    /// of the base64-decoded blob. Checking that it matches the type declared at the front of the
    /// line is what actually distinguishes a real key record from arbitrary base64 garbage.
    /// </summary>
    private static bool MatchesDeclaredType(string type, string encodedData)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(encodedData);
        }
        catch (FormatException)
        {
            return false;
        }

        if (blob.Length < 4)
        {
            return false;
        }

        var length = (blob[0] << 24) | (blob[1] << 16) | (blob[2] << 8) | blob[3];
        if (length <= 0 || length > blob.Length - 4)
        {
            return false;
        }

        var embeddedType = Encoding.ASCII.GetString(blob, 4, length);
        return embeddedType == type;
    }
}
