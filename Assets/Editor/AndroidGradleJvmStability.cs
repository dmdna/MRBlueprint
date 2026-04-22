using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class AndroidGradleJvmStability : IPreprocessBuildWithReport
{
    private static readonly string[] JavaToolOptions =
    {
        "-XX:TieredStopAtLevel=1",
        "-XX:CICompilerCount=2"
    };

    public int callbackOrder => -10000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        ApplyJavaToolOptions();
    }

    private static void ApplyJavaToolOptions()
    {
        string existingOptions = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS") ?? string.Empty;
        string updatedOptions = AppendMissingOptions(existingOptions, JavaToolOptions);
        if (updatedOptions == existingOptions)
        {
            return;
        }

        Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", updatedOptions, EnvironmentVariableTarget.Process);
        UnityEngine.Debug.Log("Applied Android Gradle JVM stability options through JAVA_TOOL_OPTIONS.");
    }

    private static string AppendMissingOptions(string existingOptions, IReadOnlyList<string> requiredOptions)
    {
        string result = existingOptions.Trim();
        foreach (string option in requiredOptions)
        {
            if (result.IndexOf(option, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            result = string.IsNullOrEmpty(result) ? option : result + " " + option;
        }

        return result;
    }
}
