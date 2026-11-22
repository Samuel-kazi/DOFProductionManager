namespace DOF.Domain.Exceptions;

/// <summary>
/// Raised when an operation would violate a business rule.
/// </summary>
public class DomainException(string message) : Exception(message);
