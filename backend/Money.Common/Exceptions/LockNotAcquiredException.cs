namespace Money.Common.Exceptions;

public class LockNotAcquiredException(string key, int retryCount)
    : Exception($"Failed to acquire lock '{key}' after {retryCount} retries");
