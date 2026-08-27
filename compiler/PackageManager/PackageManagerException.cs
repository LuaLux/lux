namespace Nebra.PackageManager;

/// <summary>
/// Thrown by package-manager code for all user-visible install/resolve failures.
/// The message is expected to be human-readable and is printed as-is.
/// </summary>
public sealed class PackageManagerException(string message) : Exception(message);
