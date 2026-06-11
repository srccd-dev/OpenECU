namespace OpenEcu.Core.Obd;

/// <summary>Parses an ELM327 ASCII reply into raw bytes, rejecting error/no-data replies.</summary>
public static class Elm327Response
{
    private static readonly string[] Errors =
        { "NO DATA", "UNABLE", "ERROR", "STOPPED", "?", "BUFFER FULL", "CAN ERROR" };

    public static bool TryParse(string raw, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        string upper = raw.ToUpperInvariant();
        foreach (string e in Errors)
            if (upper.Contains(e))
                return false;

        var hex = new System.Text.StringBuilder();
        foreach (string line in raw.Split('\r', '\n'))
        {
            string s = line.Replace(" ", "").Trim();
            if (s.Length > 0 && IsHex(s))
                hex.Append(s);
        }

        if (hex.Length < 2 || hex.Length % 2 != 0)
            return false;

        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.ToString(i * 2, 2), 16);
        bytes = result;
        return true;
    }

    private static bool IsHex(string s)
    {
        foreach (char ch in s)
            if (!Uri.IsHexDigit(ch))
                return false;
        return true;
    }
}
