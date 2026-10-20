namespace EasyClothes.Core.Rage;

public static class Jenkins
{
    // One-at-a-time hash ("joaat") the game uses for names; lowercases like the game does for asset names.
    public static uint Hash(string text, bool lowercase = true)
    {
        uint h = 0;
        foreach (var c in lowercase ? text.ToLowerInvariant() : text)
        {
            h += (byte)c;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }
}
