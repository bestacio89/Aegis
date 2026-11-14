using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AndroidSdkResolverTests
{
    [Fact]
    public void Resolves_compile_sdk_from_nearest_local_properties_without_running_gradle()
    {
        var root = CreateTemporaryDirectory();
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var sdk = CreateSdk(root, "android-35");
        File.WriteAllText(Path.Combine(root, "local.properties"), $"sdk.dir={sdk}");

        try
        {
            var resolver = new AndroidSdkResolver(environment: EmptyEnvironment());
            var result = resolver.Resolve(root, module, Android("35"));

            Assert.True(result.IsComplete);
            Assert.Equal(Path.GetFullPath(sdk), result.SdkRoot);
            Assert.Equal("LocalProperties", result.SdkRootSource);
            Assert.Equal(Path.Combine(sdk, "platforms", "android-35", "android.jar"), result.AndroidJar);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uses_module_local_properties_before_workspace_properties()
    {
        var root = CreateTemporaryDirectory();
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var workspaceSdk = CreateSdk(root, "android-35");
        var moduleSdkParent = Path.Combine(root, "module-sdk");
        var moduleSdk = CreateSdk(moduleSdkParent, "android-35");
        File.WriteAllText(Path.Combine(root, "local.properties"), $"sdk.dir={workspaceSdk}");
        File.WriteAllText(Path.Combine(module, "local.properties"), $"sdk.dir={moduleSdk}");

        try
        {
            var result = new AndroidSdkResolver(environment: EmptyEnvironment())
                .Resolve(root, module, Android("35"));

            Assert.Equal(Path.GetFullPath(moduleSdk), result.SdkRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uses_explicit_sdk_configuration_before_project_and_environment_settings()
    {
        var root = CreateTemporaryDirectory();
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var configuredSdk = Path.Combine(root, "configured-sdk");
        var localSdk = CreateSdk(root, "android-35");
        CreateSdk(configuredSdk, "android-35");
        File.WriteAllText(Path.Combine(root, "local.properties"), $"sdk.dir={localSdk}");

        try
        {
            var result = new AndroidSdkResolver(configuredSdk, new Dictionary<string, string>
            {
                ["ANDROID_HOME"] = localSdk,
                ["ANDROID_SDK_ROOT"] = localSdk
            }).Resolve(root, module, Android("35"));

            Assert.Equal(Path.GetFullPath(configuredSdk), result.SdkRoot);
            Assert.Equal("ExplicitConfiguration", result.SdkRootSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_conflicting_android_sdk_environment_roots()
    {
        var root = CreateTemporaryDirectory();
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var home = CreateSdk(root, "android-35");
        var sdkRoot = Path.Combine(root, "other-sdk");
        CreateSdk(sdkRoot, "android-35");

        try
        {
            var result = new AndroidSdkResolver(environment: new Dictionary<string, string>
            {
                ["ANDROID_HOME"] = home,
                ["ANDROID_SDK_ROOT"] = sdkRoot
            }).Resolve(root, module, Android("35"));

            Assert.False(result.IsComplete);
            Assert.Null(result.AndroidJar);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AndroidSdkEnvironmentConflict");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reports_missing_platform_as_partial_evidence()
    {
        var root = CreateTemporaryDirectory();
        var sdk = Directory.CreateDirectory(Path.Combine(root, "sdk")).FullName;
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        File.WriteAllText(Path.Combine(root, "local.properties"), $"sdk.dir={sdk}");

        try
        {
            var result = new AndroidSdkResolver(environment: EmptyEnvironment())
                .Resolve(root, module, Android("35"));

            Assert.False(result.IsComplete);
            Assert.Null(result.AndroidJar);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AndroidSdkPlatformNotFound");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Does_not_guess_a_platform_when_compile_sdk_is_missing()
    {
        var root = CreateTemporaryDirectory();
        var sdk = Directory.CreateDirectory(Path.Combine(root, "sdk")).FullName;
        var module = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        File.WriteAllText(Path.Combine(root, "local.properties"), $"sdk.dir={sdk}");

        try
        {
            var result = new AndroidSdkResolver(environment: EmptyEnvironment())
                .Resolve(root, module, Android(null));

            Assert.False(result.IsComplete);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AndroidCompileSdkUnavailable");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GradleAndroidBuildModel Android(string? compileSdk) => new()
    {
        PluginId = "com.android.application",
        CompileSdk = compileSdk
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisAndroidSdk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateSdk(string parent, string platform)
    {
        var sdk = Directory.CreateDirectory(Path.Combine(parent, "sdk")).FullName;
        var platformDirectory = Directory.CreateDirectory(Path.Combine(sdk, "platforms", platform)).FullName;
        File.WriteAllBytes(Path.Combine(platformDirectory, "android.jar"), []);
        return sdk;
    }

    private static IReadOnlyDictionary<string, string> EmptyEnvironment() =>
        new Dictionary<string, string>();
}
