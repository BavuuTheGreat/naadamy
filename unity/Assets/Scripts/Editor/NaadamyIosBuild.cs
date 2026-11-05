// The Xcode project of an iPhone build gets what Sign in with Apple needs (AppleSignIn.cs, Plugins/iOS/NaadamyAppleSignIn.mm):
// the AuthenticationServices framework and the "Sign in with Apple" capability (its entitlement). Without the capability
// Apple's sheet fails with error 1000. Only compiled when the build target is iOS, which needs iOS Build Support.
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class NaadamyIosBuild
{
    [PostProcessBuild(100)]
    public static void AddSignInWithApple(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
        project.WriteToFile(projectPath);
        var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone/Naadamy.entitlements", null, project.GetUnityMainTargetGuid());
        capabilities.AddSignInWithApple();
        capabilities.WriteToFile();
    }
}
#endif
