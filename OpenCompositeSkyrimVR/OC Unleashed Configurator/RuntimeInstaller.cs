using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenCompositeConfigurator
{
    // Keeps the game-root runtime files (openvr_api.dll, upscaler loaders,
    // opencomposite.ini, Gestures) in sync with the mod's "root" payload for
    // installs that have no root-deployment mechanism (Vortex, manual).
    //
    // HARD RULE: under MO2 this class is strictly READ-ONLY and does nothing.
    // MO2 Root Builder owns root deployment there, and any write made from
    // inside the USVFS sandbox can get redirected into MO2's overwrite folder,
    // where it permanently shadows the real mod files.
    internal static class RuntimeInstaller
    {
        // Config/user content is copied only when missing, never overwritten.
        private static bool IsCopyIfMissingOnly(string relPath)
        {
            string name = Path.GetFileName(relPath);
            if (string.Equals(name, "opencomposite.ini", StringComparison.OrdinalIgnoreCase))
                return true;
            return relPath.StartsWith("Gestures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Mod-folder debris that must never be deployed to the game root
        private static bool IsExcluded(string fileName)
        {
            return fileName.Contains(".bak", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".PRE-", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".running-old", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".vanilla", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRunningUnderMo2()
        {
            try
            {
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    string name = m.ModuleName ?? "";
                    if (name.StartsWith("usvfs", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // If we cannot enumerate modules, err on the side of not writing.
                return true;
            }
            return false;
        }

        // Catches the case where an MO2 user launches the exe directly from the
        // mod folder (no USVFS injected). Writing to the real game dir from
        // there would fight Root Builder's deploy/clean cycle, so any exe that
        // lives inside an MO2 instance's mods\ tree is treated as MO2-managed.
        private static bool LooksLikeMo2ModFolder(string exeDir)
        {
            try
            {
                string? modsDir = Path.GetDirectoryName(exeDir);
                if (modsDir == null)
                    return false;
                if (!string.Equals(Path.GetFileName(modsDir), "mods", StringComparison.OrdinalIgnoreCase))
                    return false;
                string? instanceDir = Path.GetDirectoryName(modsDir);
                if (instanceDir == null)
                    return false;
                return File.Exists(Path.Combine(instanceDir, "ModOrganizer.ini"))
                    || Directory.Exists(Path.Combine(instanceDir, "profiles"));
            }
            catch
            {
                return false;
            }
        }

        // Returns a one-line status for the main window. Empty string = nothing
        // relevant to report (e.g. bare exe without a payload beside it).
        public static string RunStartupCheck(IWin32Window owner, string exeDir)
        {
            try
            {
                if (IsRunningUnderMo2() || LooksLikeMo2ModFolder(exeDir))
                    return "Runtime deployment: managed by MO2 (Root Builder)";

                string payloadDir = Path.Combine(exeDir, "root");
                if (!Directory.Exists(payloadDir))
                    return "";

                string gameDir = FindGameDir(owner, exeDir);
                if (string.IsNullOrEmpty(gameDir))
                    return "Skyrim VR folder not found - runtime not installed";

                List<(string src, string dst)> plan = BuildSyncPlan(payloadDir, gameDir);
                if (plan.Count == 0)
                    return "OCU runtime is up to date (" + gameDir + ")";

                if (Process.GetProcessesByName("SkyrimVR").Length > 0)
                    return "OCU runtime update pending - close Skyrim VR, then reopen the configurator";

                string mainDll = Path.Combine(gameDir, "openvr_api.dll");
                bool firstInstall = !File.Exists(mainDll);
                string verb = firstInstall ? "Install" : "Update";
                var reply = MessageBox.Show(owner,
                    verb + " the OCU runtime in your Skyrim VR folder?\n\n" +
                    gameDir + "\n\n" +
                    plan.Count + " file(s) will be " + (firstInstall ? "installed." : "updated.") + "\n" +
                    "(MO2 users: this prompt never appears under MO2 - Root Builder handles it.)",
                    "OCU Runtime " + verb,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (reply != DialogResult.Yes)
                    return "Runtime " + verb.ToLowerInvariant() + " skipped - run the configurator again to retry";

                int applied = ApplyPlan(plan, gameDir);
                return "OCU runtime " + (firstInstall ? "installed" : "updated") + ": " + applied + " file(s) -> " + gameDir;
            }
            catch (Exception ex)
            {
                return "Runtime check failed: " + ex.Message;
            }
        }

        private static List<(string src, string dst)> BuildSyncPlan(string payloadDir, string gameDir)
        {
            var plan = new List<(string src, string dst)>();
            foreach (string src in Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(src);
                if (IsExcluded(fileName))
                    continue;

                string rel = Path.GetRelativePath(payloadDir, src);
                string dst = Path.Combine(gameDir, rel);

                if (IsCopyIfMissingOnly(rel))
                {
                    if (!File.Exists(dst))
                        plan.Add((src, dst));
                    continue;
                }

                if (!File.Exists(dst) || !FilesIdentical(src, dst))
                    plan.Add((src, dst));
            }
            return plan;
        }

        private static int ApplyPlan(List<(string src, string dst)> plan, string gameDir)
        {
            // Preserve the original (SteamVR) openvr_api.dll exactly once, so
            // uninstalling OCU is a rename away.
            string mainDll = Path.Combine(gameDir, "openvr_api.dll");
            string vanillaBackup = mainDll + ".vanilla.bak";
            bool touchesMainDll = plan.Any(p =>
                string.Equals(p.dst, mainDll, StringComparison.OrdinalIgnoreCase));
            if (touchesMainDll && File.Exists(mainDll) && !File.Exists(vanillaBackup))
                File.Copy(mainDll, vanillaBackup);

            int applied = 0;
            foreach (var (src, dst) in plan)
            {
                string? dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(src, dst, overwrite: true);
                applied++;
            }
            return applied;
        }

        private static bool FilesIdentical(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length)
                return false;
            using var sha = SHA256.Create();
            using var sa = File.OpenRead(a);
            byte[] ha = sha.ComputeHash(sa);
            using var shb = SHA256.Create();
            using var sb = File.OpenRead(b);
            byte[] hb = shb.ComputeHash(sb);
            return ha.SequenceEqual(hb);
        }

        // ------------------------------------------------------------------
        // Game directory discovery
        // ------------------------------------------------------------------

        private static string FindGameDir(IWin32Window owner, string exeDir)
        {
            // 1. Walk upward from the exe. Covers Vortex (exe lands in
            //    <game>\Data) and manual unzips into the game tree.
            string? dir = exeDir;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "SkyrimVR.exe")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            // 2. A previously confirmed manual pick
            string pickFile = Path.Combine(exeDir, "RuntimeGameDir.txt");
            try
            {
                if (File.Exists(pickFile))
                {
                    string saved = File.ReadAllText(pickFile).Trim();
                    if (saved.Length > 0 && File.Exists(Path.Combine(saved, "SkyrimVR.exe")))
                        return saved;
                }
            }
            catch { }

            // 3. Bethesda registry key
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim VR");
                string? installed = key?.GetValue("Installed Path") as string;
                if (!string.IsNullOrEmpty(installed) && File.Exists(Path.Combine(installed, "SkyrimVR.exe")))
                    return installed;
            }
            catch { }

            // 4. Steam library folders
            foreach (string lib in EnumerateSteamLibraries())
            {
                string candidate = Path.Combine(lib, "steamapps", "common", "SkyrimVR");
                if (File.Exists(Path.Combine(candidate, "SkyrimVR.exe")))
                    return candidate;
            }

            // 5. Ask once, remember the answer
            var ask = MessageBox.Show(owner,
                "The OCU runtime needs to be installed into your Skyrim VR folder, " +
                "but it could not be located automatically.\n\nLocate it now?",
                "Locate Skyrim VR", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask == DialogResult.Yes)
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Select the folder containing SkyrimVR.exe"
                };
                if (dlg.ShowDialog(owner) == DialogResult.OK
                    && File.Exists(Path.Combine(dlg.SelectedPath, "SkyrimVR.exe")))
                {
                    try { File.WriteAllText(pickFile, dlg.SelectedPath); } catch { }
                    return dlg.SelectedPath;
                }
            }
            return "";
        }

        private static IEnumerable<string> EnumerateSteamLibraries()
        {
            string steamPath = "";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                steamPath = (key?.GetValue("SteamPath") as string) ?? "";
            }
            catch { }
            if (string.IsNullOrEmpty(steamPath))
                yield break;

            steamPath = steamPath.Replace('/', '\\');
            yield return steamPath;

            // Parse libraryfolders.vdf for additional library roots
            foreach (string vdf in new[]
                     {
                         Path.Combine(steamPath, "config", "libraryfolders.vdf"),
                         Path.Combine(steamPath, "steamapps", "libraryfolders.vdf")
                     })
            {
                if (!File.Exists(vdf))
                    continue;
                string[] lines;
                try { lines = File.ReadAllLines(vdf); }
                catch { continue; }
                foreach (string line in lines)
                {
                    // Lines look like:  "path"  "D:\\SteamLibrary"
                    var match = System.Text.RegularExpressions.Regex.Match(
                        line, "\"path\"\\s+\"(.+?)\"");
                    if (match.Success)
                        yield return match.Groups[1].Value.Replace("\\\\", "\\");
                }
            }
        }
    }
}
