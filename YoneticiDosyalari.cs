using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BakimMerkezi
{
    internal static class MachineFiles
    {
        private static readonly SecurityIdentifier Administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier TrustedInstaller =
            new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        private const string PrimaryName = "WindowsBakimMerkezi", ProtectedName = "WindowsBakimMerkezi-Korumali";
        private const AccessControlSections AccessAndOwner = AccessControlSections.Access | AccessControlSections.Owner;
        private static readonly object Gate = new object();
        private static volatile string activeDirectory;

        private static string WindowsDirectory
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify); }
        }

        internal static string LogDirectory
        {
            get { return activeDirectory ?? Path.Combine(WindowsDirectory, "Logs", PrimaryName); }
        }

        internal static string Prepare() { return Locate(true); }

        internal static string FindExisting() { return Locate(false); }

        private static string Locate(bool create)
        {
            if (!Program.Admin)
                throw new UnauthorizedAccessException(L.Text("Bu günlük için yönetici oturumu gerekiyor.",
                    "An administrator session is required for this log."));

            lock (Gate)
            {
                string windows = WindowsDirectory;
                string parent = Path.Combine(windows, "Logs");
                if (!Startup.SafeFolder(parent) || !Directory.Exists(parent) ||
                    !TrustedParent(Directory.GetAccessControl(windows, AccessAndOwner)) ||
                    !TrustedParent(Directory.GetAccessControl(parent, AccessAndOwner)))
                    throw new IOException(L.Text("Windows günlük üst klasörünün sahipliği veya yazma izinleri güvenli değil: ",
                        "The Windows log parent has unsafe ownership or write permissions: ") + parent);

                var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string directory = SelectDirectory(Path.Combine(parent, PrimaryName), Path.Combine(parent, ProtectedName),
                    (path, createIfMissing) => CheckDirectory(path, createIfMissing, failures), create);
                if (directory != null)
                {
                    activeDirectory = directory;
                    return directory;
                }
                if (!create) return null;

                string details = string.Join(" / ", failures.Values);
                throw new IOException(L.Text("Korumalı yönetici günlüğü açılamadı. ",
                    "A protected administrator log directory could not be opened. ") + details);
            }
        }

        internal static string SelectDirectory(string primary, string alternate, Func<string, bool, bool> accept, bool create)
        {
            if (accept(primary, false)) return primary;
            if (accept(alternate, false)) return alternate;
            if (create && accept(primary, true)) return primary;
            if (create && accept(alternate, true)) return alternate;
            return null;
        }

        private static bool CheckDirectory(string path, bool createIfMissing, IDictionary<string, string> failures)
        {
            try
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException)
                {
                    if (!createIfMissing) return false;
                    Directory.CreateDirectory(path, CreateSecurity());
                    attributes = File.GetAttributes(path);
                }
                catch (DirectoryNotFoundException)
                {
                    if (!createIfMissing) return false;
                    Directory.CreateDirectory(path, CreateSecurity());
                    attributes = File.GetAttributes(path);
                }

                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory ||
                    !Startup.SafeFolder(path))
                    throw new IOException(L.Text("Yol bağlantı içeriyor veya klasör değil.", "The path contains a link or is not a directory."));
                if (!Trusted(Directory.GetAccessControl(path, AccessAndOwner)))
                    throw new IOException(L.Text("Klasör izinleri yalnızca Yöneticiler ve SYSTEM ile sınırlı değil.",
                        "Directory permissions are not restricted to Administrators and SYSTEM."));
                return true;
            }
            catch (IOException ex) { failures[path] = Path.GetFileName(path) + ": " + ex.Message; }
            catch (UnauthorizedAccessException ex) { failures[path] = Path.GetFileName(path) + ": " + ex.Message; }
            catch (SecurityException ex) { failures[path] = Path.GetFileName(path) + ": " + ex.Message; }
            return false;
        }

        internal static DirectorySecurity CreateSecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(Administrators);
            foreach (var sid in new[] { Administrators, SystemAccount })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                    AccessControlType.Allow));
            return security;
        }

        internal static bool Trusted(DirectorySecurity security)
        {
            if (!security.AreAccessRulesProtected || !SimpleDacl(security) ||
                !TrustedSid(security.GetOwner(typeof(SecurityIdentifier)))) return false;
            bool admin = false, system = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || !TrustedSid(rule.IdentityReference)) return false;
                const InheritanceFlags children = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                if (rule.PropagationFlags != PropagationFlags.None || (rule.InheritanceFlags & children) != children ||
                    (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl) continue;
                admin |= Administrators.Equals(rule.IdentityReference);
                system |= SystemAccount.Equals(rule.IdentityReference);
            }
            return admin && system;
        }

        internal static bool TrustedParent(DirectorySecurity security)
        {
            if (!SimpleDacl(security) || !TrustedParentSid(security.GetOwner(typeof(SecurityIdentifier)))) return false;
            // ACLs may retain generic read/execute bits on Windows directories.
            const uint readOnlyRights = (uint)(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize) | 0xA0000000;
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow || TrustedParentSid(rule.IdentityReference) ||
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                if ((unchecked((uint)rule.FileSystemRights) & ~readOnlyRights) != 0) return false;
            }
            return rules.Count != 0;
        }

        private static bool SimpleDacl(DirectorySecurity security)
        {
            var acl = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl;
            if (acl == null || acl.Count == 0) return false;
            foreach (GenericAce ace in acl)
            {
                var rule = ace as CommonAce;
                if (rule == null || rule.IsCallback ||
                    (rule.AceQualifier != AceQualifier.AccessAllowed && rule.AceQualifier != AceQualifier.AccessDenied)) return false;
            }
            return true;
        }

        private static bool TrustedSid(IdentityReference identity)
        {
            return Administrators.Equals(identity) || SystemAccount.Equals(identity);
        }

        private static bool TrustedParentSid(IdentityReference identity)
        {
            return TrustedSid(identity) || TrustedInstaller.Equals(identity);
        }
    }
}
