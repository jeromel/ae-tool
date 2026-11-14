using Microsoft.AspNetCore.DataProtection;

namespace AeTool.Api;

/// <summary>
/// Clés de chiffrement de l'application (cookie de session, jetons antifalsification, secrets des sources externes).
/// Elles vivent dans le dossier de données, sous « cles » : sans cela, chaque redémarrage déconnecterait tout le monde
/// et rendrait illisibles les secrets enregistrés. Un seul réglage, partagé par l'authentification et les sources.
/// </summary>
public static class KeyStorage
{
    public const string DirectoryName = "cles";

    public static IServiceCollection AddKeyStorage(this IServiceCollection services, DataPaths paths)
    {
        var directory = Path.Combine(paths.Directory, DirectoryName);
        Directory.CreateDirectory(directory);
        var protection = services.AddDataProtection().SetApplicationName("ae-tool").PersistKeysToFileSystem(new DirectoryInfo(directory));
        // Sous Windows, les clés sont elles-mêmes chiffrées par DPAPI, liées au compte qui exécute l'application.
        // Ailleurs, elles sont en clair dans le dossier : celui-ci est réservé au compte de service.
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return services;
    }
}
