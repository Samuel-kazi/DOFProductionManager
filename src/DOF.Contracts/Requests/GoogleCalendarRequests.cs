namespace DOF.Contracts.Requests;

/// <summary>Starts connecting a Google account. <see cref="RedirectUri"/> is the desktop's loopback address (http://127.0.0.1:port/).</summary>
public sealed record StartGoogleAuthorizationRequest(string RedirectUri);

/// <summary>Finishes connecting: the code and state Google sent back to the redirect address.</summary>
public sealed record CompleteGoogleAuthorizationRequest(string Code, string State);

/// <summary>
/// Sends (or updates) Google Calendar invites for an event: team members by id plus outside guests by
/// e-mail address. The list replaces the previous one; people left out are taken off the event.
/// </summary>
public sealed record SendGoogleInvitesRequest(IReadOnlyList<Guid>? UserIds, IReadOnlyList<string>? Emails);
