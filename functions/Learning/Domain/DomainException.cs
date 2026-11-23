namespace LineLanguageLearn.Learning.Domain;

public enum DomainErrorKind { Validation, NotFound, Conflict, Forbidden, Unavailable }

/// <summary>Expected application error; the API layer maps the kind to an HTTP status.</summary>
public class DomainException(DomainErrorKind kind, string code, string message) : Exception(message)
{
    public DomainErrorKind Kind { get; } = kind;
    public string Code { get; } = code;

    public static DomainException NotFound(string what, string id) => new(DomainErrorKind.NotFound, "not-found", $"{what} '{id}' was not found.");
    public static DomainException Invalid(string code, string message) => new(DomainErrorKind.Validation, code, message);
    public static DomainException Conflict(string code, string message) => new(DomainErrorKind.Conflict, code, message);
}
