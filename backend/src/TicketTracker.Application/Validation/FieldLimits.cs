namespace TicketTracker.Application.Validation;

/// <summary>
/// Pragmatic field length bounds (A5, A12, A17). Source mandates no maxima, but the backend
/// applies sane limits to prevent abuse (e.g. Argon2id DoS via huge passwords) and to match
/// the DB column sizes in ARCHITECTURE §4.1. Overflow =&gt; 400 validation_error.
/// </summary>
public static class FieldLimits
{
    public const int EmailMax = 320;
    public const int PasswordMin = 8;
    public const int PasswordMax = 1024;
    public const int TeamNameMax = 200;
    public const int EpicTitleMax = 512;
    public const int EpicDescriptionMax = 20_000;
    public const int TicketTitleMax = 512;
    public const int TicketBodyMax = 100_000;
    public const int CommentBodyMax = 20_000;
}
