using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TMPro.EditorUtilities
{
    public static class TMP_MSDFAUguiPackagePatcher
    {
        private const string PackageName = "com.unity.ugui";
        private const string PatchFileName = "ugui-msdfa-package.patch";
        private const string MenuPath = "Tools/TextMeshPro MSDFA/Embed UGUI And Apply Patch";
        private const string PatchedDefine = "TMP_MSDFA_UGUI_PATCHED";
        private const string PackageVersionDefineName = "com.mitay-walle.textmeshpro-msdfa";
        private const string PackageVersionDefineExpression = "0.1.0";

        private static EmbedRequest embedRequest;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (IsUguiEmbedded() == false)
            {
                EmbedUguiPackage();
                return;
            }

            ApplyPatch();
        }

        private static void EmbedUguiPackage()
        {
            if (embedRequest != null && embedRequest.IsCompleted == false)
                return;

            embedRequest = Client.Embed(PackageName);
            EditorApplication.update += WaitForEmbed;
            Debug.Log("[MSDFA] com.unity.ugui is not embedded. Embedding package before applying patch.");
        }

        private static void WaitForEmbed()
        {
            if (embedRequest == null || embedRequest.IsCompleted == false)
                return;

            EditorApplication.update -= WaitForEmbed;
            if (embedRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"[MSDFA] Failed to embed {PackageName}: {embedRequest.Error.message}");
                embedRequest = null;
                return;
            }

            embedRequest = null;
            ApplyPatch();
        }

        private static void ApplyPatch()
        {
            string projectPath = Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory;
            string patchPath = GetPatchPath(projectPath);
            if (File.Exists(patchPath) == false)
            {
                Debug.LogError($"[MSDFA] Patch file not found: {patchPath}");
                return;
            }

            string gitPath = FindGitPath();
            if (string.IsNullOrEmpty(gitPath))
            {
                Debug.LogError("[MSDFA] Git executable was not found in PATH. Cannot apply ugui patch.");
                return;
            }

            if (IsUguiEmbedded() == false)
            {
                EmbedUguiPackage();
                return;
            }

            if (ArePatchTargetsPresent(projectPath) == false)
            {
                Debug.LogError("[MSDFA] com.unity.ugui is embedded, but expected patch target files are missing.");
                return;
            }

            GitResult checkResult = RunGit(gitPath, projectPath, "apply", "--check", "--whitespace=nowarn", patchPath);
            if (checkResult.ExitCode != 0)
            {
                if (IsPatchAlreadyApplied(projectPath))
                {
                    bool changed = EnsurePatchedDefines(projectPath);
                    AssetDatabase.Refresh();
                    if (changed)
                        Client.Resolve();
                    Debug.Log(changed ? "[MSDFA] com.unity.ugui patch was already applied; MSDFA compile define was added." : "[MSDFA] com.unity.ugui patch is already applied.");
                    return;
                }

                Debug.LogError($"[MSDFA] UGUI patch cannot be applied.\nSTDOUT:\n{checkResult.StandardOutput}\nSTDERR:\n{checkResult.StandardError}");
                return;
            }

            GitResult applyResult = RunGit(gitPath, projectPath, "apply", "--whitespace=nowarn", patchPath);
            if (applyResult.ExitCode != 0)
            {
                Debug.LogError($"[MSDFA] Failed to apply ugui patch. Exit code {applyResult.ExitCode}\nSTDOUT:\n{applyResult.StandardOutput}\nSTDERR:\n{applyResult.StandardError}");
                return;
            }

            EnsurePatchedDefines(projectPath);
            AssetDatabase.Refresh();
            Client.Resolve();
            Debug.Log("[MSDFA] com.unity.ugui embedded and MSDFA patch applied.");
        }

        private static string GetPatchPath(string projectPath)
        {
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageVersionDefineName);
            if (packageInfo != null && string.IsNullOrEmpty(packageInfo.resolvedPath) == false)
                return Path.Combine(packageInfo.resolvedPath, PatchFileName);

            return Path.Combine(projectPath, "Packages", PackageVersionDefineName, PatchFileName);
        }

        private static bool IsPatchAlreadyApplied(string projectPath)
        {
            string fontAssetPath = Path.Combine(projectPath, "Packages", "com.unity.ugui", "Runtime", "TMP", "TMP_FontAsset.cs");
            string asmdefPath = Path.Combine(projectPath, "Packages", "com.unity.ugui", "Runtime", "TMP", "Unity.TextMeshPro.asmdef");
            if (File.Exists(fontAssetPath) == false || File.Exists(asmdefPath) == false)
                return false;

            string fontAsset = File.ReadAllText(fontAssetPath);
            string asmdef = File.ReadAllText(asmdefPath);
            return fontAsset.Contains("m_IsMsdfaAtlasEnabled") && fontAsset.Contains("TMP_MSDFAAtlasRenderer") && asmdef.Contains("GUID:43b111c4a445f446abd2c02e77750ff5");
        }

        private static bool IsUguiEmbedded()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
            return packageInfo != null && packageInfo.source == PackageSource.Embedded;
        }

        private static bool ArePatchTargetsPresent(string projectPath)
        {
            return File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Runtime", "TMP", "TMP_FontAsset.cs"))
                   && File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Runtime", "TMP", "Unity.TextMeshPro.asmdef"))
                   && File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Editor", "TMP", "TMP_EditorResourceManager.cs"))
                   && File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Editor", "TMP", "TMP_FontAssetEditor.cs"))
                   && File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Editor", "TMP", "TMPro_FontAssetCreatorWindow.cs"))
                   && File.Exists(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Editor", "TMP", "Unity.TextMeshPro.Editor.asmdef"));
        }

        private static bool EnsurePatchedDefines(string projectPath)
        {
            bool changed = false;
            changed |= EnsurePatchedDefine(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Runtime", "TMP", "Unity.TextMeshPro.asmdef"));
            changed |= EnsurePatchedDefine(Path.Combine(projectPath, "Packages", "com.unity.ugui", "Editor", "TMP", "Unity.TextMeshPro.Editor.asmdef"));
            return changed;
        }

        private static bool EnsurePatchedDefine(string asmdefPath)
        {
            if (File.Exists(asmdefPath) == false)
                return false;

            string asmdef = File.ReadAllText(asmdefPath);
            if (asmdef.Contains(PatchedDefine))
                return false;

            const string versionDefinesProperty = "\"versionDefines\": [";
            int versionDefinesIndex = asmdef.IndexOf(versionDefinesProperty, StringComparison.Ordinal);
            if (versionDefinesIndex < 0)
                return false;

            int insertIndex = versionDefinesIndex + versionDefinesProperty.Length;
            string entry = $"\n        {{\n            \"name\": \"{PackageVersionDefineName}\",\n            \"expression\": \"{PackageVersionDefineExpression}\",\n            \"define\": \"{PatchedDefine}\"\n        }}";
            int nextNonWhitespaceIndex = insertIndex;
            while (nextNonWhitespaceIndex < asmdef.Length && char.IsWhiteSpace(asmdef[nextNonWhitespaceIndex]))
                nextNonWhitespaceIndex++;

            if (nextNonWhitespaceIndex < asmdef.Length && asmdef[nextNonWhitespaceIndex] != ']')
                entry += ",";

            asmdef = asmdef.Insert(insertIndex, entry);
            File.WriteAllText(asmdefPath, asmdef, new System.Text.UTF8Encoding(false));
            return true;
        }

        private static GitResult RunGit(string gitPath, string workingDirectory, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(gitPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo);
            if (process == null)
                return new GitResult(-1, string.Empty, "Failed to start git process.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new GitResult(process.ExitCode, stdout, stderr);
        }

        private static string FindGitPath()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                string candidate = Path.Combine(directory.Trim(), Application.platform == RuntimePlatform.WindowsEditor ? "git.exe" : "git");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private readonly struct GitResult
        {
            public readonly int ExitCode;
            public readonly string StandardOutput;
            public readonly string StandardError;

            public GitResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }
        }
    }
}
