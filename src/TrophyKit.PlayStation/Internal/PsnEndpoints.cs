namespace TrophyKit.PlayStation.Internal;

internal static class PsnEndpoints
{
    public const string Platform = "PlayStation";

    public const string AuthBase = "https://ca.account.sony.com/api/authz/v3/oauth/";
    public const string ApiBase = "https://m.np.playstation.com/api/";
    public const string LegacyProfileBase = "https://us-prof.np.community.playstation.net/userProfile/v1/users/";

    // Public client of the PlayStation App, used by every community library.
    public const string ClientId = "09515159-7237-4370-9b40-3806e67c0891";
    public const string ClientBasicAuth = "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";
    public const string RedirectUri = "com.scee.psxandroid.scecompcall://redirect";
    public const string Scope = "psn:mobile.v2.core psn:clientapp";

    public const string LegacyProfileFields =
        "npId,onlineId,accountId,avatarUrls,plus,aboutMe,languagesUsed," +
        "trophySummary(@default,level,progress,earnedTrophies),isOfficiallyVerified";
}
