using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace DOF.Desktop.Services;

/// <summary>Opens a web address in the user's browser. Tests replace it to play the browser's part.</summary>
public interface IBrowserLauncher
{
    void Open(string url);
}

public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>The result of the browser sign-in: the code and state for the server, or the reason it stopped.</summary>
public sealed record GoogleSignInResult(string? Code, string? State, string? Error)
{
    public bool Succeeded => Code is not null && State is not null;
}

/// <summary>
/// Google sign-in for a desktop app (RFC 8252): listens on a random loopback port, opens Google's page in
/// the browser, and waits for Google to redirect back with the authorization code. The code is handed to
/// the DOF server, which holds the client secret and the PKCE verifier and exchanges it.
/// </summary>
public sealed class GoogleSignIn(IBrowserLauncher browser)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <param name="authorizationUrl">Asks the server for Google's sign-in address for this redirect URI.</param>
    public async Task<GoogleSignInResult> RunAsync(Func<string, Task<string>> authorizationUrl, CancellationToken ct = default)
    {
        var redirectUri = $"http://127.0.0.1:{FreePort()}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        try
        {
            browser.Open(await authorizationUrl(redirectUri));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            while (true)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];
                if (code is null && error is null)
                {
                    // Something else (such as a favicon request) reached the port: ignore it.
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                await RespondAsync(context, error is null);
                return error is null
                    ? new GoogleSignInResult(code, state, null)
                    : new GoogleSignInResult(null, null, error == "access_denied" ? "Google sign-in was cancelled." : $"Google sign-in failed ({error}).");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new GoogleSignInResult(null, null, "Google sign-in timed out. Please try again.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, bool success)
    {
        var message = success
            ? "DOF Production Manager is connecting to Google Calendar. You can close this tab and return to the app."
            : "Google sign-in did not complete. You can close this tab and try again from the app.";
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>DOF Production Manager</title></head>" +
                   $"<body style=\"font-family:-apple-system,Segoe UI,sans-serif;margin:15vh auto;max-width:32rem;color:#1c2333\">" +
                   $"<h2>{(success ? "Signed in" : "Not connected")}</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
