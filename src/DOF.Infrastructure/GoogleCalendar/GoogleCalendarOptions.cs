namespace DOF.Infrastructure.GoogleCalendar;

/// <summary>
/// Google OAuth client ("Desktop app" type) and the key that encrypts stored refresh tokens. All three
/// come from the environment or a secret store, never from committed files.
/// </summary>
public sealed class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Base64 of 32 random bytes (<c>openssl rand -base64 32</c>).</summary>
    public string TokenEncryptionKey { get; set; } = string.Empty;

    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
    public string RevocationEndpoint { get; set; } = "https://oauth2.googleapis.com/revoke";
    public string CalendarApiBase { get; set; } = "https://www.googleapis.com/calendar/v3/";
}
