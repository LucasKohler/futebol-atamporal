namespace FutebolAtemporal.Domain.Exceptions;

public class DomainException : Exception
{
    public string? Code { get; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    public DomainException(string message, string? code = null, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message)
    {
        Code = code;
        Extensions = extensions;
    }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public string? Code { get; }
    public NotFoundException(string message, string? code = null) : base(message)
    {
        Code = code;
    }
}

public class ConflictException : Exception
{
    public string? Code { get; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    public ConflictException(string message, string? code = null, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message)
    {
        Code = code;
        Extensions = extensions;
    }
}

public sealed class RegistrationWindowClosedException(bool monthlyWindowStarted, DateTimeOffset opensAt)
    : DomainException("A sua janela de inscrição ainda não abriu.",
        monthlyWindowStarted ? "casual_window_not_open" : "registration_window_closed",
        new Dictionary<string, object?> { ["opensAt"] = opensAt });
