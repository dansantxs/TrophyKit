namespace TrophyKit.Xbox.Internal;

internal static class XboxEndpoints
{
    public const string Platform = "Xbox";

    public const string AuthorizeUrl = "https://login.live.com/oauth20_authorize.srf";
    public const string TokenUrl = "https://login.live.com/oauth20_token.srf";
    public const string Scope = "Xboxlive.signin Xboxlive.offline_access";
    public const string UserAuthenticateUrl = "https://user.auth.xboxlive.com/user/authenticate";
    public const string XstsAuthorizeUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";

    public const string ProfileBase = "https://profile.xboxlive.com/users/";
    public const string TitleHubBase = "https://titlehub.xboxlive.com/users/";
    public const string AchievementsBase = "https://achievements.xboxlive.com/users/";

    public const string ProfileSettings = "Gamertag,Gamerscore,GameDisplayPicRaw,AccountTier,XboxOneRep";
}
