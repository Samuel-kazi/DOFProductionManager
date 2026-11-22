using System.Collections.Concurrent;
using DOF.Application.Common.Abstractions;

namespace DOF.IntegrationTests;

/// <summary>
/// Stands in for the SMTP server in tests: records what would have been sent and can be told to fail
/// for one address (to exercise retries and failure recording).
/// </summary>
public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public bool IsConfigured => true;

    public void FailFor(string address, string error) => _failures[address] = error;

    public void StopFailing(string address) => _failures.TryRemove(address, out _);

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (_failures.TryGetValue(message.To, out var error))
        {
            throw new InvalidOperationException(error);
        }
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
