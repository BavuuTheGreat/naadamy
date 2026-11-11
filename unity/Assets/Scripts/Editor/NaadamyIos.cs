// iOS build: sets what the App Store needs, then writes the Xcode project to Builds/iOS.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class NaadamyIos
{
    /** Simulator project for testing on a Mac without a phone: Builds/iOSSim. */
    public static void BuildSim() { PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK; Build("Builds/iOSSim"); }

    [MenuItem("Naadamy/Build iOS (Xcode project)")]
    public static void BuildIos() { PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK; Build("Builds/iOS"); }

    static void Build(string path)
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.pineconeacademy.naadamy");
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.iOS.buildNumber = "1";
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.microphoneUsageDescription = "Naadamy uses the microphone only when you record a story for the memory book.";
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed; // the hosted server is https/wss
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.iOS, BuildTarget.iOS);
        const string scene = "Assets/Scenes/Nutag.unity";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = path, target = BuildTarget.iOS });
        Debug.Log($"Naadamy build iOS: {report.summary.result} → {path}");
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
