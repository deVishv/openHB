using System.Security.AccessControl;
using System.Security.Principal;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// The data folder holds the books, password hashes, and the TLS key, so only the system, administrators, and the
/// account the host runs as may open it. Folders under ProgramData are otherwise readable by every local user.
/// </summary>
internal static class DataFolderSecurity
{
    public static void Restrict(string folder)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using var me = WindowsIdentity.GetCurrent();
        foreach (var sid in new[]
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            me.User!,
        }.Distinct())
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        new DirectoryInfo(folder).SetAccessControl(security);
    }
}
