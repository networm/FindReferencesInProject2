using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class FindReferencesInProject2
{
    [MenuItem("Assets/Find References In Project %&#f", false, 25)]
    public static void Find()
    {
        bool isMacOS = Application.platform == RuntimePlatform.OSXEditor;
        int totalWaitMilliseconds = isMacOS ? 2 * 1000 : 300 * 1000;

        var selectedObject = Selection.activeObject;
        string selectedAssetPath = AssetDatabase.GetAssetPath(selectedObject);
        string selectedAssetGUID = AssetDatabase.AssetPathToGUID(selectedAssetPath);
        string selectedAssetMetaPath = selectedAssetPath + ".meta";
        string appDataPath = Application.dataPath;
        int cpuCount = Environment.ProcessorCount;

        List<string> references = new List<string>();
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
            psi.FileName = Path.Combine(Environment.CurrentDirectory, @"Tools\FindReferencesInProject2\rg.exe");
            psi.Arguments = string.Format("--case-sensitive --follow --files-with-matches --no-text --fixed-strings " +
                                          "--ignore-file Assets/Editor/FindReferencesInProject2/ignore.txt " +
                                          "--threads {0} --regexp {1} -- {2}",
                cpuCount, selectedAssetGUID, appDataPath);
        }

        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        Process process = new Process();
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

        process.Start();
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

        foreach (var file in references)
        {
            output.AppendLine(file);

            string assetPath = file;
            if (file.EndsWith(".meta"))
            {
                assetPath = file.Substring(0, file.Length - ".meta".Length);
            }

            UnityEngine.Debug.Log(assetPath, AssetDatabase.LoadMainAssetAtPath(assetPath));
        }

        EditorUtility.ClearProgressBar();
        stopwatch.Stop();

        string content;
        if (references.Count < 2)
        {
            content = string.Format("{0} reference found for object: \"{1}\" path: \"{2}\" total time: {4}s\n\n{3}",
                references.Count, selectedObject.name, selectedAssetPath, output, stopwatch.ElapsedMilliseconds / 1000d);
        }
        else
        {
            content = string.Format("{0} references found for object: \"{1}\" path: \"{2}\" total time: {4}s\n\n{3}",
                references.Count, selectedObject.name, selectedAssetPath, output, stopwatch.ElapsedMilliseconds / 1000d);
        }
        UnityEngine.Debug.LogWarning(content, selectedObject);
    }

    [MenuItem("Assets/Find References In Project %&#f", true)]
    private static bool FindValidate()
    {
        var obj = Selection.activeObject;
        if (obj != null)
        {
            if (AssetDatabase.Contains(obj))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                return !AssetDatabase.IsValidFolder(path);
            }
        }

        return false;
    }
}
