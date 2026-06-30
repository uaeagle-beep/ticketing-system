namespace TicketTracker.Infrastructure.Email;

/// <summary>
/// SMTP configuration, all bound from environment (ARCHITECTURE §8, ADR-0004). No secrets
/// in source. Default host is relay1.dataart.com per source §3. Credentials are optional
/// (the relay may be unauthenticated).
/// </summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "relay1.dataart.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseStartTls { get; set; } = true;
    public string From { get; set; } = "no-reply@ticketing.local";
}
