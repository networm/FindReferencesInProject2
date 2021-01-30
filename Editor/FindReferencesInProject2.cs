using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class FindReferencesInProject2
{
    private const string MenuItemName = "Assets/Find References In Project %#&f";
    private const string MetaExtension = ".meta";

    private static string InstallDirectory = "Assets/Editor/FindReferencesInProject2";

    private static void UpdateInstallDirectory([CallerFilePath] string executingFilePath = "")
    {
        InstallDirectory = Path.GetDirectoryName(executingFilePath);
    }

    [MenuItem(MenuItemName, false, 25)]
    public static void Find()
    {
        UpdateInstallDirectory();

        bool isMacOS = Application.platform == RuntimePlatform.OSXEditor;
        int totalWaitMilliseconds = isMacOS ? 2 * 1000 : 300 * 1000;
        int cpuCount = Environment.ProcessorCount;
        string appDataPath = Application.dataPath;

        var selectedObject = Selection.activeObject;
        string selectedAssetPath = AssetDatabase.GetAssetPath(selectedObject);
        string selectedAssetGUID = AssetDatabase.AssetPathToGUID(selectedAssetPath);
        string selectedAssetMetaPath = selectedAssetPath + MetaExtension;

        var references = new List<string>();
        var output = new System.Text.StringBuilder();

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var psi = new ProcessStartInfo();
        psi.WindowStyle = ProcessWindowStyle.Minimized;

        if (isMacOS)
        {
            psi.FileName = "/usr/bin/mdfind";
            psi.Arguments = string.Format("-onlyin {0} {1}", appDataPath, selectedAssetGUID);
        }
        else
        {
            var ignore_file = Path.Combine(InstallDirectory, "ignore.txt");
            var filepath = Path.Combine(Environment.CurrentDirectory, @"Tools\FindReferencesInProject2\rg.exe");
            if (!File.Exists(filepath))
            {
                // Assume it's in our path.
                filepath = "rg.exe";
            }
            psi.FileName = filepath;
            psi.Arguments = string.Format("--case-sensitive --follow --files-with-matches --no-text --fixed-strings " +
                                          "--ignore-file {3} " +
                                          "--threads {0} --regexp {1} -- {2}",
                cpuCount, selectedAssetGUID, appDataPath, ignore_file);
        }

        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        var process = new Process();
        process.StartInfo = psi;

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            string relativePath = e.Data.Replace(appDataPath, "Assets").Replace("\\", "/");

            // skip the meta file of whatever we have selected
            if (relativePath == selectedAssetMetaPath)
                return;

            references.Add(relativePath);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            output.AppendLine("Error: " + e.Data);
        };

        try
        {
            process.Start();
        }
        catch (SystemException)
        {
            if (!isMacOS)
            {
                var destination = Path.Combine(Environment.CurrentDirectory, @"Tools\FindReferencesInProject2");
                UnityEngine.Debug.LogError($"Couldn't find ripgrep. Download ripgrep from https://github.com/BurntSushi/ripgrep/releases/latest and extract rg.exe to {destination} or add it to your PATH.");
            }
            throw;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        while (!process.HasExited)
        {
            if (stopwatch.ElapsedMilliseconds < totalWaitMilliseconds)
            {
                float progress = (float)((double)stopwatch.ElapsedMilliseconds / totalWaitMilliseconds);
                string info = string.Format("Finding {0}/{1}s {2:P2}", stopwatch.ElapsedMilliseconds / 1000,
                    totalWaitMilliseconds / 1000, progress);
                bool canceled = EditorUtility.DisplayCancelableProgressBar("Find References in Project", info, progress);

                if (canceled)
                {
                    process.Kill();
                    break;
                }

                Thread.Sleep(100);
            }
            else
            {
                process.Kill();
                break;
            }
        }

        foreach (string file in references)
        {
            string guid = AssetDatabase.AssetPathToGUID(file);
            output.AppendLine(string.Format("{0} {1}", guid, file));

            string assetPath = file;
            if (file.EndsWith(MetaExtension))
            {
                assetPath = file.Substring(0, file.Length - MetaExtension.Length);
            }

            UnityEngine.Debug.Log(string.Format("{0}\n{1}", file, guid), AssetDatabase.LoadMainAssetAtPath(assetPath));
        }

        EditorUtility.ClearProgressBar();
        stopwatch.Stop();

        string content = string.Format(
            "{0} {1} found for object: \"{2}\" path: \"{3}\" guid: \"{4}\" total time: {5}s\n\n{6}",
            references.Count, references.Count > 2 ? "references" : "reference", selectedObject.name, selectedAssetPath,
            selectedAssetGUID, stopwatch.ElapsedMilliseconds / 1000d, output);
        UnityEngine.Debug.LogWarning(content, selectedObject);
    }

    [MenuItem(MenuItemName, true)]
    private static bool FindValidate()
    {
        var obj = Selection.activeObject;
        if (obj != null && AssetDatabase.Contains(obj))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            return !AssetDatabase.IsValidFolder(path);
        }

        return false;
    }
}
