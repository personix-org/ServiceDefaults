namespace ServiceDefaults;

public sealed class ServiceDefaultRegistrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
